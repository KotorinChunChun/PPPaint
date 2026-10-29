[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [switch]$SkipBuild,
    [switch]$Capture,
    [int]$CaptureProcessId = 0,
    [ValidateSet('Screen', 'PrintWindow')]
    [string]$CaptureMode = 'Screen'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$dotnetPath = Join-Path $ProjectRoot '.tools\dotnet\dotnet.exe'
$solutionPath = Join-Path $ProjectRoot 'PPPaint.slnx'
$applicationPath = Join-Path $ProjectRoot 'src\PPPaint\PPPaint.csproj'
$captureIndexPath = Join-Path $ProjectRoot 'dev\history\v0.2.0\logs\captures\capture-index.jsonl'
$captureRequestPath = Join-Path $ProjectRoot 'dev\capture\Send-CaptureRequest.ps1'

foreach ($requiredPath in @($dotnetPath, $solutionPath, $applicationPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required quality-gate path is missing: '$requiredPath'."
    }
}

function Invoke-NativeStep {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [Parameter(Mandatory)]
        [string]$Name
    )

    Write-Host "[quality] $Name"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Assert-PathInsideProject {
    param([Parameter(Mandatory)][string]$RelativePath)

    if ([IO.Path]::IsPathRooted($RelativePath)) {
        throw "Capture path must be relative to the project: '$RelativePath'."
    }

    $fullPath = [IO.Path]::GetFullPath([IO.Path]::Combine($ProjectRoot, $RelativePath))
    $backToRelative = [IO.Path]::GetRelativePath($ProjectRoot, $fullPath)
    if ($backToRelative -eq '..' -or
        $backToRelative.StartsWith(('..' + [IO.Path]::DirectorySeparatorChar), [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($backToRelative)) {
        throw "Capture path escapes the project root: '$RelativePath'."
    }

    return $fullPath
}

function Test-CaptureIndex {
    if (-not (Test-Path -LiteralPath $captureIndexPath -PathType Leaf)) {
        if ($Capture) { throw "撮影索引がありません: '$captureIndexPath'。" }
        Write-Host '[quality] ローカルの撮影索引がないため、画面証跡の検査を省略します。'
        return
    }
    $lineNumber = 0
    $entryCount = 0
    foreach ($line in Get-Content -LiteralPath $captureIndexPath -Encoding UTF8) {
        $lineNumber++
        if ([string]::IsNullOrWhiteSpace($line)) {
            throw "Capture index contains an empty line at $lineNumber."
        }

        try {
            $entry = ConvertFrom-Json -InputObject $line
        }
        catch {
            throw "Capture index line $lineNumber is not valid JSON: $($_.Exception.Message)"
        }

        if ($entry.schemaVersion -ne 1 -or
            [string]::IsNullOrWhiteSpace([string]$entry.file) -or
            [string]$entry.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
            throw "Capture index line $lineNumber has an invalid schema or hash."
        }

        $capturePath = Assert-PathInsideProject ([string]$entry.file)
        if (-not (Test-Path -LiteralPath $capturePath -PathType Leaf)) {
            throw "Capture file is missing: '$($entry.file)'."
        }

        $actualHash = (Get-FileHash -LiteralPath $capturePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne ([string]$entry.sha256).ToLowerInvariant()) {
            throw "Capture hash does not match: '$($entry.file)'."
        }

        $metadataPath = [IO.Path]::ChangeExtension($capturePath, '.json')
        if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
            throw "Capture metadata is missing: '$metadataPath'."
        }

        try {
            $metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
        }
        catch {
            throw "Capture metadata is not valid JSON: '$metadataPath'."
        }
        if ($metadata.schemaVersion -ne 1 -or [string]$metadata.file -ne [string]$entry.file) {
            throw "Capture metadata does not match its index entry: '$metadataPath'."
        }

        $entryCount++
    }

    if ($entryCount -eq 0) {
        throw 'Capture index contains no entries.'
    }

    Write-Host "[quality] capture-index validated: $entryCount entries"
}

if ($Capture) {
    if ($CaptureProcessId -le 0) {
        throw '-Capture requires -CaptureProcessId for an already running PPPaint window.'
    }
    if (-not (Test-Path -LiteralPath $captureRequestPath -PathType Leaf)) {
        throw "Capture request script is missing: '$captureRequestPath'."
    }

    $pwshPath = (Get-Command pwsh -ErrorAction Stop).Source
    Invoke-NativeStep $pwshPath @(
        '-NoProfile',
        '-File',
        $captureRequestPath,
        '-Action',
        'Capture',
        '-Purpose',
        'C6 quality gate regression capture',
        '-Progress',
        'c6-quality-gate',
        '-Instruction',
        'Automated quality-gate capture for the existing PPPaint process',
        '-ProcessId',
        $CaptureProcessId,
        '-CaptureMode',
        $CaptureMode
    ) 'capture request'
}

if (-not $SkipBuild) {
    Invoke-NativeStep $dotnetPath @('test', $solutionPath, '-c', 'Debug') 'solution tests'
    Invoke-NativeStep $dotnetPath @('build', $applicationPath, '-c', 'Debug', '-p:Platform=x64') 'application Debug|x64 build'
    Invoke-NativeStep $dotnetPath @('build', $solutionPath, '-c', 'Debug') 'solution Debug build'
}

Test-CaptureIndex
Invoke-NativeStep 'git' @('-c', "safe.directory=$ProjectRoot", 'diff', '--check') 'git diff --check'
Write-Host '[quality] quality gate passed'
