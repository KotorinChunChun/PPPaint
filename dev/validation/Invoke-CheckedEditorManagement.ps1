[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$TargetProcessId,
    [Parameter(Mandatory)][string]$ExecutablePath,
    [Parameter(Mandatory)][string]$SettingsDirectory,
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet('status','show','stop')][string]$Command = 'status'
)
$ErrorActionPreference = 'Stop'
$expectedPath = [IO.Path]::GetFullPath($ExecutablePath)
$process = Get-Process -Id $TargetProcessId -ErrorAction SilentlyContinue
if (-not $process) { throw '対象プロセスは終了しています。' }
if ($process.Path -ne $expectedPath) { throw '対象の実行パスが一致しません。' }
if ($process.SessionId -ne (Get-Process -Id $PID).SessionId) { throw '対象セッションが一致しません。' }
$normalized = [IO.Path]::GetFullPath($SettingsDirectory).Replace('/','\').ToLowerInvariant()
$key = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($normalized))).ToLowerInvariant().Substring(0,16)
$pipeName = 'KotorinToys.' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value + '.pppaint-' + $key
if (-not ('PPPaintValidationPipe' -as [type])) {
    Add-Type @'
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class PPPaintValidationPipe {
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
}
'@
}
function Send-Request([string]$Operation) {
    $client = [IO.Pipes.NamedPipeClientStream]::new('.', $pipeName, [IO.Pipes.PipeDirection]::InOut,
        [IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly)
    try {
        $client.Connect(1000)
        [uint32]$serverId = 0
        if (-not [PPPaintValidationPipe]::GetNamedPipeServerProcessId($client.SafePipeHandle, [ref]$serverId) -or $serverId -ne $TargetProcessId) { throw '管理窓口PIDが一致しません。' }
        $server = Get-Process -Id $serverId
        if ($server.Path -ne $expectedPath -or $server.SessionId -ne $process.SessionId) { throw '管理窓口の実行先が一致しません。' }
        $reader = [IO.StreamReader]::new($client, [Text.UTF8Encoding]::new($false), $false, 1024, $true)
        $writer = [IO.StreamWriter]::new($client, [Text.UTF8Encoding]::new($false), 1024, $true)
        $writer.AutoFlush = $true
        try {
            $requestId = [guid]::NewGuid().ToString()
            $request = @{ command=$Operation; expectedPid=$TargetProcessId; expectedSessionId=$process.SessionId; requestId=$requestId; deadlineUtc=[DateTimeOffset]::UtcNow.AddSeconds(2).ToString('O') }
            $writer.WriteLine(($request | ConvertTo-Json -Compress))
            $read = $reader.ReadLineAsync()
            if (-not $read.Wait(2500)) { throw '管理応答がタイムアウトしました。' }
            $reply = $read.GetAwaiter().GetResult() | ConvertFrom-Json
            if ($reply.pid -ne $TargetProcessId -or $reply.version -ne $Version -or $reply.requestId -ne $requestId) { throw '管理応答のPID・版・要求IDが一致しません。' }
            return $reply
        } finally { $writer.Dispose(); $reader.Dispose() }
    } finally { $client.Dispose() }
}
$status = Send-Request status
if ($Command -eq 'status') { $status | ConvertTo-Json; return }
if (-not $status.ready) { throw '対象は準備中です。変更要求は送りません。' }
# stop is the application's guarded operation: dirty/busy windows are retained.
Send-Request $Command | ConvertTo-Json
