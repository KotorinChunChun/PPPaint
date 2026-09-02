[CmdletBinding(SupportsShouldProcess)]
param(
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

$installPath = Resolve-SafeInstallDirectory $InstallDirectory
$registrationKeys = @(
    'HKCU:\Software\Classes\.chunpaint',
    'HKCU:\Software\Classes\ChunPaint.Project',
    'HKCU:\Software\Classes\chunpaint',
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ChunPaint'
)

foreach ($registrationKey in $registrationKeys) {
    if ((Test-Path -LiteralPath $registrationKey) -and
        $PSCmdlet.ShouldProcess($registrationKey, 'ChunPaintのユーザー登録を削除')) {
        Remove-Item -LiteralPath $registrationKey -Recurse -Force
    }
}

if ((Test-Path -LiteralPath $installPath) -and
    $PSCmdlet.ShouldProcess($installPath, 'ChunPaintのインストール済みファイルを削除')) {
    Set-Location -LiteralPath ([IO.Path]::GetTempPath())
    Remove-Item -LiteralPath $installPath -Recurse -Force
}

Write-Host 'ChunPaint was uninstalled for the current user.'
