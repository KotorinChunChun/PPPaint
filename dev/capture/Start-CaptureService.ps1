[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$OutputRoot = '',
    [string]$PipeName = 'ChunPaint.Capture',
    [string]$DefaultWindowTitle = 'ChunPaint',
    [ValidateSet('Screen', 'PrintWindow')]
    [string]$DefaultCaptureMode = 'Screen'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $ProjectRoot 'docs\chunpaint-captures'
}

$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$relativeOutputRoot = [IO.Path]::GetRelativePath($ProjectRoot, $OutputRoot)
if ($relativeOutputRoot -eq '..' -or
    $relativeOutputRoot.StartsWith(('..' + [IO.Path]::DirectorySeparatorChar), [StringComparison]::Ordinal) -or
    [IO.Path]::IsPathRooted($relativeOutputRoot)) {
    throw "OutputRoot must remain inside the ChunPaint project directory: '$ProjectRoot'."
}

[IO.Directory]::CreateDirectory($OutputRoot) | Out-Null
$serviceLogPath = Join-Path $OutputRoot 'capture-service.log'
$indexPath = Join-Path $OutputRoot 'capture-index.jsonl'

function Write-ServiceLog {
    param([string]$Message)

    $line = '{0:o} {1}' -f [DateTimeOffset]::Now, $Message
    Add-Content -LiteralPath $serviceLogPath -Value $line -Encoding utf8
}

function Get-MessageValue {
    param(
        [hashtable]$Message,
        [string]$Name,
        $DefaultValue
    )

    if ($Message.ContainsKey($Name) -and $null -ne $Message[$Name]) {
        return $Message[$Name]
    }

    return $DefaultValue
}

function ConvertTo-FileToken {
    param(
        [AllowEmptyString()]
        [string]$Value,
        [string]$Fallback,
        [int]$MaximumLength = 36
    )

    $token = if ([string]::IsNullOrWhiteSpace($Value)) { $Fallback } else { $Value.Trim() }
    foreach ($character in [IO.Path]::GetInvalidFileNameChars()) {
        $token = $token.Replace([string]$character, '_')
    }

    $token = $token -replace '\s+', '-'
    $token = $token -replace '_+', '_'
    $token = $token.Trim('.', '-', '_')
    if ([string]::IsNullOrWhiteSpace($token)) {
        $token = $Fallback
    }

    if ($token.Length -gt $MaximumLength) {
        $token = $token.Substring(0, $MaximumLength).TrimEnd('.', '-', '_')
    }

    return $token
}

if (-not ('ChunPaintCapture.NativeMethods' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace ChunPaintCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out Rect rectangle);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(
            IntPtr hWnd,
            int attribute,
            out Rect value,
            int valueSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint flags);
    }
}
'@
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

function Find-TargetWindow {
    param(
        [int]$ProcessId,
        [string]$WindowTitle
    )

    if ($ProcessId -gt 0) {
        $process = Get-Process -Id $ProcessId -ErrorAction Stop
        $process.Refresh()
        if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
            throw "Process $ProcessId does not currently have a main window."
        }

        return $process
    }

    $title = if ([string]::IsNullOrWhiteSpace($WindowTitle)) { $DefaultWindowTitle } else { $WindowTitle }
    $process = Get-Process | Where-Object {
        $_.MainWindowHandle -ne [IntPtr]::Zero -and
        $_.MainWindowTitle.Contains($title, [StringComparison]::OrdinalIgnoreCase)
    } | Sort-Object StartTime -Descending | Select-Object -First 1

    if ($null -eq $process) {
        throw "No visible window containing title '$title' was found."
    }

    return $process
}

function Get-WindowBounds {
    param([IntPtr]$WindowHandle)

    $rectangle = [ChunPaintCapture.Rect]::new()
    $attributeResult = [ChunPaintCapture.NativeMethods]::DwmGetWindowAttribute(
        $WindowHandle,
        9,
        [ref]$rectangle,
        [Runtime.InteropServices.Marshal]::SizeOf([type][ChunPaintCapture.Rect]))

    if ($attributeResult -ne 0) {
        $success = [ChunPaintCapture.NativeMethods]::GetWindowRect($WindowHandle, [ref]$rectangle)
        if (-not $success) {
            throw 'Unable to read the target window bounds.'
        }
    }

    $width = $rectangle.Right - $rectangle.Left
    $height = $rectangle.Bottom - $rectangle.Top
    if ($width -le 0 -or $height -le 0) {
        throw "The target window has invalid bounds: ${width}x${height}."
    }

    return [ordered]@{
        left = $rectangle.Left
        top = $rectangle.Top
        width = $width
        height = $height
    }
}

function Save-ScreenCapture {
    param(
        [hashtable]$Bounds,
        [string]$Path
    )

    $bitmap = [Drawing.Bitmap]::new(
        $Bounds.width,
        $Bounds.height,
        [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen(
            $Bounds.left,
            $Bounds.top,
            0,
            0,
            [Drawing.Size]::new($Bounds.width, $Bounds.height),
            [Drawing.CopyPixelOperation]::SourceCopy)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Save-PrintWindowCapture {
    param(
        [IntPtr]$WindowHandle,
        [hashtable]$Bounds,
        [string]$Path
    )

    $bitmap = [Drawing.Bitmap]::new(
        $Bounds.width,
        $Bounds.height,
        [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $deviceContext = $graphics.GetHdc()
    try {
        $success = [ChunPaintCapture.NativeMethods]::PrintWindow($WindowHandle, $deviceContext, 2)
        if (-not $success) {
            throw 'PrintWindow could not capture the target window.'
        }
    }
    finally {
        $graphics.ReleaseHdc($deviceContext)
        $graphics.Dispose()
    }

    try {
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

function New-CapturePath {
    param(
        [DateTimeOffset]$CapturedAt,
        [string]$Progress,
        [string]$Purpose,
        [string]$Instruction
    )

    $dateDirectory = Join-Path $OutputRoot $CapturedAt.ToString('yyyy-MM-dd')
    [IO.Directory]::CreateDirectory($dateDirectory) | Out-Null

    $progressToken = ConvertTo-FileToken $Progress 'progress-unspecified' 28
    $purposeToken = ConvertTo-FileToken $Purpose 'purpose-unspecified' 40
    $instructionToken = ConvertTo-FileToken $Instruction 'instruction-none' 40
    $baseName = '{0}__{1}__{2}__{3}' -f $CapturedAt.ToString('yyyyMMdd_HHmmss_fff'), $progressToken, $purposeToken, $instructionToken
    $candidate = Join-Path $dateDirectory ($baseName + '.png')
    $suffix = 1
    while (Test-Path -LiteralPath $candidate) {
        $candidate = Join-Path $dateDirectory ('{0}__{1:D2}.png' -f $baseName, $suffix)
        $suffix++
    }

    return $candidate
}

function Invoke-Capture {
    param([hashtable]$Message)

    $purpose = [string](Get-MessageValue $Message 'purpose' 'purpose-unspecified')
    $progress = [string](Get-MessageValue $Message 'progress' 'progress-unspecified')
    $instruction = [string](Get-MessageValue $Message 'instruction' 'instruction-none')
    $windowTitle = [string](Get-MessageValue $Message 'windowTitle' $DefaultWindowTitle)
    $processId = [int](Get-MessageValue $Message 'processId' 0)
    $delayMs = [Math]::Max(0, [int](Get-MessageValue $Message 'delayMs' 0))
    $captureMode = [string](Get-MessageValue $Message 'captureMode' $DefaultCaptureMode)

    if ($captureMode -notin @('Screen', 'PrintWindow')) {
        throw "Unsupported capture mode '$captureMode'."
    }

    if ($delayMs -gt 0) {
        Start-Sleep -Milliseconds $delayMs
    }

    $process = Find-TargetWindow -ProcessId $processId -WindowTitle $windowTitle
    $windowHandle = $process.MainWindowHandle
    $bounds = Get-WindowBounds -WindowHandle $windowHandle
    $capturedAt = [DateTimeOffset]::Now
    $capturePath = New-CapturePath -CapturedAt $capturedAt -Progress $progress -Purpose $purpose -Instruction $instruction

    if ($captureMode -eq 'PrintWindow') {
        Save-PrintWindowCapture -WindowHandle $windowHandle -Bounds $bounds -Path $capturePath
    }
    else {
        Save-ScreenCapture -Bounds $bounds -Path $capturePath
    }

    $relativeCapturePath = [IO.Path]::GetRelativePath($ProjectRoot, $capturePath).Replace('\', '/')
    $metadata = [ordered]@{
        schemaVersion = 1
        capturedAt = $capturedAt.ToString('o')
        progress = $progress
        purpose = $purpose
        instruction = $instruction
        captureMode = $captureMode
        file = $relativeCapturePath
        sha256 = (Get-FileHash -LiteralPath $capturePath -Algorithm SHA256).Hash.ToLowerInvariant()
        target = [ordered]@{
            processId = $process.Id
            processName = $process.ProcessName
            windowTitle = $process.MainWindowTitle
            bounds = $bounds
        }
        request = $Message
    }

    $metadataPath = [IO.Path]::ChangeExtension($capturePath, '.json')
    $metadataJson = $metadata | ConvertTo-Json -Depth 8
    Set-Content -LiteralPath $metadataPath -Value $metadataJson -Encoding utf8
    Add-Content -LiteralPath $indexPath -Value ($metadata | ConvertTo-Json -Depth 8 -Compress) -Encoding utf8

    Write-ServiceLog "Captured '$relativeCapturePath' for '$purpose'."
    return [ordered]@{
        ok = $true
        action = 'capture'
        file = $relativeCapturePath
        metadata = [IO.Path]::GetRelativePath($ProjectRoot, $metadataPath).Replace('\', '/')
        capturedAt = $capturedAt.ToString('o')
        bounds = $bounds
    }
}

Write-ServiceLog "Capture service started. Pipe='$PipeName'; Output='$OutputRoot'; PID=$PID."
$stopRequested = $false
while (-not $stopRequested) {
    $pipe = [IO.Pipes.NamedPipeServerStream]::new(
        $PipeName,
        [IO.Pipes.PipeDirection]::InOut,
        1,
        [IO.Pipes.PipeTransmissionMode]::Byte,
        [IO.Pipes.PipeOptions]::CurrentUserOnly)
    try {
        $pipe.WaitForConnection()
        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 4096, $true)
        $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 4096, $true)
        $writer.AutoFlush = $true
        try {
            $requestLine = $reader.ReadLine()
            if ([string]::IsNullOrWhiteSpace($requestLine)) {
                throw 'The capture request was empty.'
            }

            $message = ConvertFrom-Json -InputObject $requestLine -AsHashtable
            $action = [string](Get-MessageValue $message 'action' 'capture')
            switch ($action.ToLowerInvariant()) {
                'capture' {
                    $response = Invoke-Capture -Message $message
                }
                'status' {
                    $response = [ordered]@{
                        ok = $true
                        action = 'status'
                        serviceProcessId = $PID
                        pipeName = $PipeName
                        outputRoot = $OutputRoot
                    }
                }
                'stop' {
                    $response = [ordered]@{
                        ok = $true
                        action = 'stop'
                        serviceProcessId = $PID
                    }
                    $stopRequested = $true
                }
                default {
                    throw "Unknown capture action '$action'."
                }
            }
        }
        catch {
            $response = [ordered]@{
                ok = $false
                error = $_.Exception.Message
            }
            Write-ServiceLog "Request failed: $($_.Exception.Message)"
        }

        $writer.WriteLine(($response | ConvertTo-Json -Depth 8 -Compress))
        $writer.Dispose()
        $reader.Dispose()
    }
    catch {
        Write-ServiceLog "Pipe failure: $($_.Exception.Message)"
    }
    finally {
        $pipe.Dispose()
    }
}

Write-ServiceLog 'Capture service stopped.'
