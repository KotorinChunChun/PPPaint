[CmdletBinding()]
param(
    [ValidateSet('Capture', 'Status', 'Stop')]
    [string]$Action = 'Capture',
    [string]$Purpose = '',
    [string]$Progress = '',
    [string]$Instruction = '',
    [int]$ProcessId = 0,
    [string]$WindowTitle = 'ChunPaint',
    [ValidateSet('Screen', 'PrintWindow')]
    [string]$CaptureMode = 'Screen',
    [ValidateRange(0, 60000)]
    [int]$DelayMs = 0,
    [string]$PipeName = 'ChunPaint.Capture',
    [ValidateRange(100, 60000)]
    [int]$TimeoutMs = 5000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Action -eq 'Capture' -and [string]::IsNullOrWhiteSpace($Purpose)) {
    throw 'Purpose is required for a capture request.'
}

$request = [ordered]@{
    action = $Action.ToLowerInvariant()
    purpose = $Purpose
    progress = $Progress
    instruction = $Instruction
    processId = $ProcessId
    windowTitle = $WindowTitle
    captureMode = $CaptureMode
    delayMs = $DelayMs
    requestedAt = [DateTimeOffset]::Now.ToString('o')
}

$pipe = [IO.Pipes.NamedPipeClientStream]::new(
    '.',
    $PipeName,
    [IO.Pipes.PipeDirection]::InOut,
    [IO.Pipes.PipeOptions]::None)
try {
    $pipe.Connect($TimeoutMs)
    $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 4096, $true)
    $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 4096, $true)
    $writer.AutoFlush = $true
    try {
        $writer.WriteLine(($request | ConvertTo-Json -Depth 6 -Compress))
        $responseLine = $reader.ReadLine()
        if ([string]::IsNullOrWhiteSpace($responseLine)) {
            throw 'The capture service returned an empty response.'
        }

        $response = ConvertFrom-Json -InputObject $responseLine
        if (-not $response.ok) {
            throw "Capture service error: $($response.error)"
        }

        return $response
    }
    finally {
        $writer.Dispose()
        $reader.Dispose()
    }
}
finally {
    $pipe.Dispose()
}
