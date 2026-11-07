[CmdletBinding()]
param([string]$DotnetPath = 'dotnet', [switch]$NoRestore, [string]$ReceiptPath)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Import-Module (Join-Path $PSScriptRoot EditorWorkflow.psm1) -Force
Push-Location $projectRoot
try {
    if ($ReceiptPath -and (Test-Path -LiteralPath $ReceiptPath)) { throw 'Use a new validation receipt path.' }
    $before = Get-EditorSourceHash $projectRoot
    $restoreArguments = @(); if ($NoRestore) { $restoreArguments += '--no-restore' }
    & $DotnetPath build PPPaint.slnx -c Release --nologo @restoreArguments
    if ($LASTEXITCODE -ne 0) { throw 'Releaseビルドに失敗しました。' }
    & $DotnetPath test PPPaint.slnx -c Release --no-build --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Core/Imagingの試験に失敗しました。' }
    git diff --check
    if ($LASTEXITCODE -ne 0) { throw '差分に空白の問題があります。' }
    $after = Get-EditorSourceHash $projectRoot
    if ($before -ne $after) { throw 'Source changed during validation; no receipt issued.' }
    if ($ReceiptPath) {
        Write-EditorJson ([ordered]@{Status='passed';Version=(Get-EditorVersion $projectRoot);SourceHash=$after;SourceCommit=(git rev-parse HEAD);Dotnet=(& $DotnetPath --version);CompletedAt=(Get-Date -Format o)}) $ReceiptPath
    }
} finally { Pop-Location }
