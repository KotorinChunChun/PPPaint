[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$PipeName = 'PPPaint.Capture',
    [string]$ExecutablePath = '',
    [switch]$Build,
    [switch]$SkipInitialCapture,
    [switch]$DoNotMaximize
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$captureServiceScript = Join-Path $PSScriptRoot 'Start-CaptureService.ps1'
$captureRequestScript = Join-Path $PSScriptRoot 'Send-CaptureRequest.ps1'
$expectedCaptureRoot = [IO.Path]::GetFullPath((Join-Path $ProjectRoot 'docs\pppaint-captures'))
$dotnetPath = Join-Path $ProjectRoot '.tools\dotnet\dotnet.exe'
$projectPath = Join-Path $ProjectRoot 'src\PPPaint\PPPaint.csproj'

if (-not ('PPPaintCapture.WindowControl' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace PPPaintCapture
{
    public static class WindowControl
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int command);
    }
}
'@
}

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $ProjectRoot 'src\PPPaint\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\PPPaint.exe'
}

if ($Build -or -not (Test-Path -LiteralPath $ExecutablePath)) {
    if (-not (Test-Path -LiteralPath $dotnetPath)) {
        throw "The local .NET SDK was not found at '$dotnetPath'."
    }

    & $dotnetPath build $projectPath -c Debug -r win-x64 -p:Platform=x64
    if ($LASTEXITCODE -ne 0) {
        throw "PPPaint build failed with exit code $LASTEXITCODE."
    }
}

$captureServiceProcess = $null
$serviceStatus = $null
$serviceWasRunning = $false
try {
    $serviceStatus = & $captureRequestScript -Action Status -PipeName $PipeName -TimeoutMs 300
    $serviceWasRunning = $true
}
catch {
    $pwshPath = (Get-Command pwsh -ErrorAction Stop).Source
    $serviceArguments = @(
        '-NoProfile',
        '-File', ('"{0}"' -f $captureServiceScript),
        '-ProjectRoot', ('"{0}"' -f $ProjectRoot),
        '-PipeName', ('"{0}"' -f $PipeName)
    )
    $captureServiceProcess = Start-Process -FilePath $pwshPath -ArgumentList $serviceArguments -WindowStyle Hidden -PassThru

    $deadline = [DateTimeOffset]::Now.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        try {
            $serviceStatus = & $captureRequestScript -Action Status -PipeName $PipeName -TimeoutMs 300
            break
        }
        catch {
            if ($captureServiceProcess.HasExited) {
                throw "The capture service exited with code $($captureServiceProcess.ExitCode)."
            }
        }
    } while ([DateTimeOffset]::Now -lt $deadline)

    if ($null -eq $serviceStatus) {
        throw 'The capture service did not become ready within 10 seconds.'
    }
}

$actualCaptureRoot = [IO.Path]::GetFullPath([string]$serviceStatus.outputRoot)
if (-not $actualCaptureRoot.Equals($expectedCaptureRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The active capture service belongs to another project: '$actualCaptureRoot'."
}

$applicationProcess = Start-Process -FilePath $ExecutablePath -WorkingDirectory (Split-Path -Parent $ExecutablePath) -PassThru
$windowDeadline = [DateTimeOffset]::Now.AddSeconds(20)
do {
    Start-Sleep -Milliseconds 100
    $applicationProcess.Refresh()
    if ($applicationProcess.HasExited) {
        throw "PPPaint exited before opening a window. Exit code: $($applicationProcess.ExitCode)."
    }
} while ($applicationProcess.MainWindowHandle -eq [IntPtr]::Zero -and [DateTimeOffset]::Now -lt $windowDeadline)

if ($applicationProcess.MainWindowHandle -eq [IntPtr]::Zero) {
    throw 'PPPaint did not create a main window within 20 seconds.'
}

if (-not $DoNotMaximize) {
    [PPPaintCapture.WindowControl]::ShowWindowAsync($applicationProcess.MainWindowHandle, 3) | Out-Null
    Start-Sleep -Milliseconds 500
    $applicationProcess.Refresh()
}

$initialCapture = $null
if (-not $SkipInitialCapture) {
    $initialCapture = & $captureRequestScript `
        -Action Capture `
        -PipeName $PipeName `
        -ProcessId $applicationProcess.Id `
        -Purpose '起動直後のUI基準記録' `
        -Progress 'development-startup' `
        -Instruction 'Paint比較前の全体画面を保存' `
        -DelayMs 800
}

[pscustomobject]@{
    applicationProcessId = $applicationProcess.Id
    captureServiceProcessId = $serviceStatus.serviceProcessId
    captureServiceWasAlreadyRunning = $serviceWasRunning
    initialCapture = if ($null -eq $initialCapture) { $null } else { $initialCapture.file }
    sendCaptureCommand = "pwsh -NoProfile -File dev/capture/Send-CaptureRequest.ps1 -Purpose '<目的>' -Progress '<進捗>' -Instruction '<指示>' -ProcessId $($applicationProcess.Id)"
}
