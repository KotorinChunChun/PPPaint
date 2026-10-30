[CmdletBinding()]
param([string]$DotnetPath = 'dotnet')

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $projectRoot
try {
    & $DotnetPath build PPPaint.slnx -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Releaseビルドに失敗しました。' }
    & $DotnetPath test PPPaint.slnx -c Release --no-build --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Core/Imagingの試験に失敗しました。' }
    git diff --check
    if ($LASTEXITCODE -ne 0) { throw '差分に空白の問題があります。' }
} finally { Pop-Location }
