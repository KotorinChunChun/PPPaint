[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SourceDirectory = $PSScriptRoot,
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\ChunPaint')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-SafeInstallDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $programsRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs')).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $requiredPrefix = $programsRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase) -or $resolved -eq $programsRoot) {
        throw "InstallDirectory must be a child of '$programsRoot'."
    }
    return $resolved
}

$sourcePath = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
$installPath = Resolve-SafeInstallDirectory $InstallDirectory
$sourceExecutable = Join-Path $sourcePath 'ChunPaint.exe'
if (-not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf)) {
    throw "ChunPaint.exe was not found in '$sourcePath'."
}
if ($installPath.StartsWith(($sourcePath + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'InstallDirectory cannot be inside SourceDirectory.'
}

if (-not $sourcePath.Equals($installPath, [StringComparison]::OrdinalIgnoreCase) -and
    $PSCmdlet.ShouldProcess($installPath, 'ChunPaintのself-contained配布物をインストールまたは更新')) {
    New-Item -ItemType Directory -Path $installPath -Force | Out-Null
    Copy-Item -Path (Join-Path $sourcePath '*') -Destination $installPath -Recurse -Force
}

$executablePath = Join-Path $installPath 'ChunPaint.exe'
$iconPath = Join-Path $installPath 'Assets\ChunPaintIcon.ico'
$uninstallerPath = Join-Path $installPath 'Uninstall-ChunPaint.ps1'
$classesRoot = 'HKCU:\Software\Classes'
$extensionKey = Join-Path $classesRoot '.chunpaint'
$programKey = Join-Path $classesRoot 'ChunPaint.Project'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ChunPaint'

if ($PSCmdlet.ShouldProcess('現在のユーザー', '.chunpaint関連付けとアンインストール情報を登録')) {
    New-Item -Path $extensionKey -Force | Out-Null
    Set-Item -Path $extensionKey -Value 'ChunPaint.Project'
    New-Item -Path $programKey -Force | Out-Null
    Set-Item -Path $programKey -Value 'ChunPaint プロジェクト'
    New-Item -Path (Join-Path $programKey 'DefaultIcon') -Force | Out-Null
    Set-Item -Path (Join-Path $programKey 'DefaultIcon') -Value ('"{0}",0' -f $iconPath)
    New-Item -Path (Join-Path $programKey 'shell\open\command') -Force | Out-Null
    Set-Item -Path (Join-Path $programKey 'shell\open\command') -Value ('"{0}" "%1"' -f $executablePath)

    New-Item -Path $uninstallKey -Force | Out-Null
    Set-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'ChunPaint'
    Set-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value '0.1.0'
    Set-ItemProperty -Path $uninstallKey -Name Publisher -Value 'ChunPaint contributors'
    Set-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installPath
    Set-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value $iconPath
    Set-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -Type DWord
    Set-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -Type DWord
    Set-ItemProperty -Path $uninstallKey -Name UninstallString -Value ('pwsh -NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $uninstallerPath)
}

Write-Host "ChunPaint was installed for the current user: $installPath"
