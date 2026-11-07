[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [string]$SettingsDirectory=(Join-Path $env:LOCALAPPDATA PPPaint/profiles/editor),
    [string]$CopyPreferencesFrom,
    [Parameter(Mandatory)][string]$ReportPath
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot EditorWorkflow.psm1) -Force
$app=(Resolve-Path -LiteralPath (Join-Path $ReleaseDirectory PPPaint.exe)).Path
$fileVersion=(Get-Item -LiteralPath $app).VersionInfo.FileVersion
if($fileVersion -notmatch '^(\d+\.\d+\.\d+)\.0$'){throw 'Unexpected PPPaint file version'}
$version=$matches[1]
$profile=[IO.Path]::GetFullPath($SettingsDirectory)
if(Test-Path -LiteralPath $ReportPath){throw 'Use a new startup report path.'}
if(-not('EditorDesktopWindows' -as [type])){
Add-Type @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class EditorDesktopWindows {
 public delegate bool EnumWindow(IntPtr window,IntPtr state);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern IntPtr OpenDesktop(string name,uint flags,bool inherit,uint access);
 [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr handle);
 [DllImport("user32.dll")] public static extern bool EnumDesktopWindows(IntPtr desktop,EnumWindow callback,IntPtr state);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr window,StringBuilder text,int capacity);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window,int command);
 [DllImport("kernel32.dll",SetLastError=true)] public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint pid);
 public static long[] Windows(int pid) {
  var desktop=OpenDesktop("Default",0,false,1);
  if(desktop==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
  var result=new List<long>();
  try {EnumDesktopWindows(desktop,(w,s)=>{uint owner;GetWindowThreadProcessId(w,out owner);if(owner==pid)result.Add(w.ToInt64());return true;},IntPtr.Zero);}
  finally {CloseDesktop(desktop);} return result.ToArray();
 }
 public static string Title(IntPtr window){var text=new StringBuilder(512);GetWindowText(window,text,text.Capacity);return text.ToString();}
}
'@
}
$normalized=$profile.Replace('/','\').ToLowerInvariant()
$key=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($normalized))).ToLowerInvariant().Substring(0,16)
$pipeName='KotorinToys.'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value+'.pppaint-'+$key
$pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$pipeName,[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly)
[uint32]$owner=0
try {
    try{$pipe.Connect(250)}catch [TimeoutException]{}
    if($pipe.IsConnected -and -not [EditorDesktopWindows]::GetNamedPipeServerProcessId($pipe.SafePipeHandle,[ref]$owner)){throw 'Cannot identify profile owner'}
}finally{$pipe.Dispose()}
if($owner -and (Get-Process -Id $owner).Path -ne $app){throw 'Another executable owns this profile. Specify a separate SettingsDirectory; existing windows were retained.'}
$destination=Join-Path $profile screenshot-preferences.json
$source=$null;$copied=$false
if($CopyPreferencesFrom){
    $source=Join-Path ([IO.Path]::GetFullPath($CopyPreferencesFrom)) screenshot-preferences.json
    if(-not(Test-Path -LiteralPath $source -PathType Leaf)){throw 'Preferences source does not exist'}
}
New-Item -ItemType Directory -Path $profile -Force | Out-Null
if($source -and -not(Test-Path -LiteralPath $destination)){
    Copy-Item -LiteralPath $source -Destination $destination
    if((Get-FileHash $source).Hash -ne (Get-FileHash $destination).Hash){throw 'Preferences copy hash mismatch'}
    $copied=$true
}
$initialHash=if(Test-Path -LiteralPath $destination){(Get-FileHash $destination).Hash}else{$null}
$appPid=if($owner){[int]$owner}else{& (Join-Path $PSScriptRoot Start-DefaultDesktopProcess.ps1) -Executable $app -Arguments ('--settings-dir "'+$profile+'"')}
$management=Join-Path $PSScriptRoot ../validation/Invoke-CheckedEditorManagement.ps1
$window=[IntPtr]::Zero;$deadline=[DateTime]::UtcNow.AddSeconds(20)
do{
    foreach($handle in [EditorDesktopWindows]::Windows($appPid)){
        $title=[EditorDesktopWindows]::Title([IntPtr]$handle)
        if($title -like "* - PPPaint - v$version"){$window=[IntPtr]$handle;break}
    }
    if($window -eq [IntPtr]::Zero){Start-Sleep -Milliseconds 200}
}while($window -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
if($window -eq [IntPtr]::Zero){throw "Default desktop window not found for PID $appPid; no existing process was stopped."}
[EditorDesktopWindows]::ShowWindow($window,9)|Out-Null
$status=& $management -TargetProcessId $appPid -ExecutablePath $app -SettingsDirectory $profile -Version $version -Command status | ConvertFrom-Json
if(-not $status.ready -or -not [EditorDesktopWindows]::IsWindowVisible($window)){throw 'Editor is not ready/visible'}
$report=[ordered]@{Status='passed';ProcessId=$appPid;Path=$app;Version=$version;Desktop='Default';Title=[EditorDesktopWindows]::Title($window);Visible=$true;Ready=$true;ReusedProcess=([bool]$owner);SettingsDirectory=$profile;PreferencesCopied=$copied;PreferencesSource=$source;InitialPreferencesHash=$initialHash;CheckedAt=(Get-Date -Format o)}
Write-EditorJson $report $ReportPath
$report|ConvertTo-Json
