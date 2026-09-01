[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [switch]$SkipBuild,
    [switch]$SmokeLaunch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$dotnetPath = Join-Path $ProjectRoot '.tools\dotnet\dotnet.exe'
$solutionPath = Join-Path $ProjectRoot 'ChunPaint.slnx'
$applicationPath = Join-Path $ProjectRoot 'src\ChunPaint\ChunPaint.csproj'
$sourceRoot = Join-Path $ProjectRoot 'src'
$manifestPath = Join-Path $ProjectRoot 'src\ChunPaint\app.manifest'
$projectFilePath = Join-Path $ProjectRoot 'src\ChunPaint\ChunPaint.csproj'
$xamlPath = Join-Path $ProjectRoot 'src\ChunPaint\MainWindow.xaml'
$iconPath = Join-Path $ProjectRoot 'src\ChunPaint\Assets\ChunPaintIcon.svg'
$exeIconPath = Join-Path $ProjectRoot 'src\ChunPaint\Assets\ChunPaintIcon.ico'
$cursorModulePath = Join-Path $ProjectRoot 'src\ChunPaint\Assets\ChunPaintCursors.dll'
$ribbonIconRoot = Join-Path $ProjectRoot 'src\ChunPaint\Assets\RibbonIcons'
$finalRibbonIconPath = Join-Path $ribbonIconRoot 'final'
$temporaryRibbonIconPath = Join-Path $ribbonIconRoot 'temporary'
$iconProvenancePath = Join-Path $ProjectRoot 'docs\icon-provenance.md'
$qualityGatePath = Join-Path $ProjectRoot 'dev\quality\Invoke-QualityGate.ps1'
$installerPath = Join-Path $ProjectRoot 'dev\installer\Install-ChunPaint.ps1'
$uninstallerPath = Join-Path $ProjectRoot 'dev\installer\Uninstall-ChunPaint.ps1'
$buildOutputPath = Join-Path $ProjectRoot 'src\ChunPaint\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64'
$releaseOutputPath = Join-Path $ProjectRoot 'artifacts\release\win-x64'
$textExtensions = @('.cs', '.csproj', '.manifest', '.props', '.targets', '.xaml', '.xml', '.json', '.svg', '.config', '.txt', '.md', '.ps1')
$forbiddenPattern = '(?i)(?<![a-z])mspaint|(?<![a-z])paint\.exe|microsoft\s+paint'
$requiredRibbonIcons = @('resize.png', 'eraser.png', 'fill.png', 'brush.png')

foreach ($requiredPath in @($dotnetPath, $solutionPath, $applicationPath, $manifestPath, $projectFilePath, $xamlPath, $iconPath, $exeIconPath, $cursorModulePath, $iconProvenancePath, $qualityGatePath, $installerPath, $uninstallerPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required release-validation path is missing: '$requiredPath'."
    }
}

function Invoke-NativeStep {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$Name
    )

    Write-Host "[release] $Name"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Reset-ReleaseDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $resolvedProjectRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedProjectRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a directory outside the project root: '$resolvedPath'."
    }
    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

function Assert-TextContract {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Description
    )

    if (-not $Text.Contains($Description, [StringComparison]::Ordinal)) {
        throw "Release contract is missing '$Description' in '$Path'."
    }
}

function Test-ForbiddenReferences {
    param(
        [Parameter(Mandatory)][string]$Root,
        [switch]$SkipDocumentation
    )

    foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File) {
        if ($file.FullName -match '\\(bin|obj|artifacts)\\') {
            continue
        }

        if ($file.Name -match $forbiddenPattern) {
            throw "Forbidden Paint reference appears in file name: '$($file.FullName)'."
        }

        if ($SkipDocumentation -and $file.Extension -eq '.md') {
            continue
        }

        if ($textExtensions -contains $file.Extension.ToLowerInvariant()) {
            $text = [string](Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8)
            if ([Regex]::IsMatch($text, $forbiddenPattern)) {
                throw "Forbidden Paint reference appears in source/artifact: '$($file.FullName)'."
            }
        }
    }
}

function Test-PeArchitecture {
    param([Parameter(Mandatory)][string]$Path)

    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $machine = $reader.PEHeaders.CoffHeader.Machine.ToString()
            if ($machine -ne 'Amd64') {
                throw "Release executable is not x64: '$Path' reports '$machine'."
            }
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Test-ReleaseArtifact {
    $executablePath = Join-Path $releaseOutputPath 'ChunPaint.exe'
    $assemblyPath = Join-Path $releaseOutputPath 'ChunPaint.dll'
    $publishedIconPath = Join-Path $releaseOutputPath 'Assets\ChunPaintIcon.svg'
    $publishedExeIconPath = Join-Path $releaseOutputPath 'Assets\ChunPaintIcon.ico'
    $publishedCursorModulePath = Join-Path $releaseOutputPath 'Assets\ChunPaintCursors.dll'
    $publishedInstallerPath = Join-Path $releaseOutputPath 'Install-ChunPaint.ps1'
    $publishedUninstallerPath = Join-Path $releaseOutputPath 'Uninstall-ChunPaint.ps1'
    $requiredArtifacts = @(
        $executablePath,
        $assemblyPath,
        $publishedIconPath,
        $publishedExeIconPath,
        $publishedCursorModulePath,
        $publishedInstallerPath,
        $publishedUninstallerPath,
        (Join-Path $releaseOutputPath 'App.xbf'),
        (Join-Path $releaseOutputPath 'MainWindow.xbf'),
        (Join-Path $releaseOutputPath 'ChunPaint.pri'),
        (Join-Path $releaseOutputPath 'coreclr.dll'),
        (Join-Path $releaseOutputPath 'hostfxr.dll')
    )
    foreach ($requiredArtifact in $requiredArtifacts) {
        if (-not (Test-Path -LiteralPath $requiredArtifact -PathType Leaf)) {
            throw "Release artifact is missing: '$requiredArtifact'."
        }
    }

    $publishedRibbonIconPath = Join-Path $releaseOutputPath 'Assets\RibbonIcons\final'
    $publishedTemporaryIconPath = Join-Path $releaseOutputPath 'Assets\RibbonIcons\temporary'
    foreach ($ribbonIcon in $requiredRibbonIcons) {
        $publishedRibbonIcon = Join-Path $publishedRibbonIconPath $ribbonIcon
        if (-not (Test-Path -LiteralPath $publishedRibbonIcon -PathType Leaf)) {
            throw "Final ribbon icon is missing from the Release artifact: '$publishedRibbonIcon'."
        }
    }
    if (Test-Path -LiteralPath $publishedTemporaryIconPath) {
        throw "Temporary ribbon assets remain in the Release artifact: '$publishedTemporaryIconPath'."
    }

    Test-PeArchitecture $executablePath
    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath)
    if ($versionInfo.ProductName -ne 'ChunPaint' -or $versionInfo.FileVersion -notmatch '^0\.1\.0\.0') {
        throw "Release assembly metadata is invalid: Product='$($versionInfo.ProductName)', FileVersion='$($versionInfo.FileVersion)'."
    }

    $publishedIcon = Get-Content -LiteralPath $publishedIconPath -Raw -Encoding UTF8
    Assert-TextContract $publishedIconPath $publishedIcon 'ChunPaint'
    $publishedInstaller = Get-Content -LiteralPath $publishedInstallerPath -Raw -Encoding UTF8
    Assert-TextContract $publishedInstallerPath $publishedInstaller 'HKCU:\Software\Classes'
    Assert-TextContract $publishedInstallerPath $publishedInstaller '.chunpaint'
    Assert-TextContract $publishedInstallerPath $publishedInstaller '"%1"'
    Test-ForbiddenReferences $releaseOutputPath -SkipDocumentation

    $releaseDocs = @(
        'README.md',
        'LICENSE',
        'PRIVACY.md',
        'CHANGELOG.md',
        'docs\v0.1.0-release-notes.md'
    )
    foreach ($releaseDoc in $releaseDocs) {
        $artifactDocPath = Join-Path $releaseOutputPath $releaseDoc
        if (-not (Test-Path -LiteralPath $artifactDocPath -PathType Leaf)) {
            throw "Release documentation is missing: '$artifactDocPath'."
        }
    }

    Write-Host "[release] artifact validated: $releaseOutputPath"
}

function Invoke-StartupSmoke {
    if (-not $SmokeLaunch) {
        return
    }

    $executablePath = Join-Path $releaseOutputPath 'ChunPaint.exe'
    Write-Host '[release] startup smoke'
    $process = Start-Process -FilePath $executablePath -WorkingDirectory $releaseOutputPath -PassThru
    try {
        try {
            $process.WaitForInputIdle(10000) | Out-Null
        }
        catch [InvalidOperationException] {
            # WinUI can finish startup on a thread that does not expose an input-idle wait.
        }
        Start-Sleep -Seconds 2
        $process.Refresh()
        if ($process.HasExited) {
            throw "ChunPaint exited during startup smoke with code $($process.ExitCode)."
        }
        if ($process.MainWindowHandle -eq 0) {
            Write-Warning 'ChunPaint stayed alive, but MainWindowHandle was unavailable; verify the visible window in a UI session.'
        }
        else {
            Write-Host "[release] startup smoke window detected: $($process.MainWindowHandle)"
        }
        Write-Host "[release] startup smoke process passed: PID $($process.Id)"
    }
    finally {
        $process.Refresh()
        if (-not $process.HasExited) {
            $process.CloseMainWindow() | Out-Null
            if (-not $process.WaitForExit(5000)) {
                Stop-Process -Id $process.Id -Force
            }
        }
    }
}

function Test-InstallerWhatIf {
    $publishedInstallerPath = Join-Path $releaseOutputPath 'Install-ChunPaint.ps1'
    $publishedUninstallerPath = Join-Path $releaseOutputPath 'Uninstall-ChunPaint.ps1'
    $whatIfInstallPath = Join-Path $env:LOCALAPPDATA 'Programs\ChunPaint-ReleaseValidation-WhatIf'
    if (Test-Path -LiteralPath $whatIfInstallPath) {
        throw "Installer WhatIf target already exists: '$whatIfInstallPath'."
    }

    & $publishedInstallerPath -SourceDirectory $releaseOutputPath -InstallDirectory $whatIfInstallPath -WhatIf
    & $publishedUninstallerPath -InstallDirectory $whatIfInstallPath -WhatIf
    if (Test-Path -LiteralPath $whatIfInstallPath) {
        throw "Installer WhatIf unexpectedly created '$whatIfInstallPath'."
    }
    Write-Host '[release] installer WhatIf validation passed'
}

$projectText = Get-Content -LiteralPath $projectFilePath -Raw -Encoding UTF8
$manifestText = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
$xamlText = Get-Content -LiteralPath $xamlPath -Raw -Encoding UTF8
$iconText = Get-Content -LiteralPath $iconPath -Raw -Encoding UTF8
$iconProvenanceText = Get-Content -LiteralPath $iconProvenancePath -Raw -Encoding UTF8
Assert-TextContract $projectFilePath $projectText '<Version>0.1.0</Version>'
Assert-TextContract $projectFilePath $projectText '<AssemblyVersion>0.1.0.0</AssemblyVersion>'
Assert-TextContract $projectFilePath $projectText '<FileVersion>0.1.0.0</FileVersion>'
Assert-TextContract $projectFilePath $projectText '<WindowsPackageType>None</WindowsPackageType>'
Assert-TextContract $projectFilePath $projectText '<ApplicationIconSource>Assets\ChunPaintIcon.svg</ApplicationIconSource>'
Assert-TextContract $projectFilePath $projectText '<ApplicationIcon>Assets\ChunPaintIcon.ico</ApplicationIcon>'
Assert-TextContract $projectFilePath $projectText 'Assets\ChunPaintCursors.dll'
Assert-TextContract $projectFilePath $projectText 'Assets\ChunPaintIcon.svg'
Assert-TextContract $manifestPath $manifestText 'version="0.1.0.0"'
Assert-TextContract $manifestPath $manifestText 'requestedExecutionLevel level="asInvoker"'
Assert-TextContract $manifestPath $manifestText 'PerMonitorV2'
Assert-TextContract $xamlPath $xamlText 'Assets/ChunPaintIcon.svg'
Assert-TextContract $iconPath $iconText 'aria-label="ChunPaint"'
foreach ($ribbonIcon in $requiredRibbonIcons) {
    $sourceRibbonIcon = Join-Path $finalRibbonIconPath $ribbonIcon
    if (-not (Test-Path -LiteralPath $sourceRibbonIcon -PathType Leaf)) {
        throw "Final ribbon icon is missing from source: '$sourceRibbonIcon'."
    }
    Assert-TextContract $xamlPath $xamlText "Assets/RibbonIcons/final/$ribbonIcon"
    Assert-TextContract $iconProvenancePath $iconProvenanceText "Assets/RibbonIcons/final/$ribbonIcon"
}
if (Test-Path -LiteralPath $temporaryRibbonIconPath) {
    throw "Temporary ribbon asset directory remains in source: '$temporaryRibbonIconPath'."
}
if ($xamlText.Contains('RibbonIcons/temporary', [StringComparison]::OrdinalIgnoreCase) -or
    $iconProvenanceText.Contains('| temporary |', [StringComparison]::OrdinalIgnoreCase) -or
    $iconProvenanceText.Contains('status: temporary', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Temporary ribbon asset references remain in XAML or icon provenance.'
}
Test-ForbiddenReferences $sourceRoot

if (-not $SkipBuild) {
    Reset-ReleaseDirectory $buildOutputPath
    Reset-ReleaseDirectory $releaseOutputPath
    Invoke-NativeStep $dotnetPath @('test', $solutionPath, '-c', 'Release') 'Core and solution Release tests'
    Invoke-NativeStep $dotnetPath @('build', $applicationPath, '-c', 'Release', '-p:Platform=x64') 'application Release|x64 build'
    Invoke-NativeStep $dotnetPath @('build', $solutionPath, '-c', 'Release') 'solution Release build'

    if (-not (Test-Path -LiteralPath (Join-Path $buildOutputPath 'ChunPaint.exe') -PathType Leaf)) {
        throw "The x64 Release build output is missing: '$buildOutputPath'."
    }
    New-Item -ItemType Directory -Path $releaseOutputPath -Force | Out-Null
    Copy-Item -Path (Join-Path $buildOutputPath '*') -Destination $releaseOutputPath -Recurse -Force
    Write-Host "[release] staged self-contained win-x64 build: $releaseOutputPath"

    $documentationTargets = @(
        @{ Source = 'README.md'; Destination = 'README.md' },
        @{ Source = 'LICENSE'; Destination = 'LICENSE' },
        @{ Source = 'PRIVACY.md'; Destination = 'PRIVACY.md' },
        @{ Source = 'CHANGELOG.md'; Destination = 'CHANGELOG.md' },
        @{ Source = 'docs\v0.1.0-release-notes.md'; Destination = 'docs\v0.1.0-release-notes.md' }
        @{ Source = 'dev\installer\Install-ChunPaint.ps1'; Destination = 'Install-ChunPaint.ps1' }
        @{ Source = 'dev\installer\Uninstall-ChunPaint.ps1'; Destination = 'Uninstall-ChunPaint.ps1' }
    )
    foreach ($documentationTarget in $documentationTargets) {
        $destination = Join-Path $releaseOutputPath $documentationTarget.Destination
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $ProjectRoot $documentationTarget.Source) -Destination $destination -Force
    }
}

Test-ReleaseArtifact
Test-InstallerWhatIf
Invoke-StartupSmoke

$pwshPath = (Get-Command pwsh -ErrorAction Stop).Source
Invoke-NativeStep $pwshPath @('-NoProfile', '-File', $qualityGatePath, '-SkipBuild') 'existing quality gate'
Invoke-NativeStep 'git' @('-c', "safe.directory=$ProjectRoot", 'diff', '--check') 'git diff --check'
Write-Host '[release] release validation passed'
