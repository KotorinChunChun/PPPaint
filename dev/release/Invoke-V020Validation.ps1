[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [switch]$SkipBuild,
    [switch]$SmokeLaunch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Core') { throw 'pwshで実行してください。' }
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$dotnetPath = Join-Path $ProjectRoot '.tools\dotnet\dotnet.exe'
$releaseScript = Join-Path $ProjectRoot 'dev\release\Invoke-ReleaseValidation.ps1'
$imagingProject = Join-Path $ProjectRoot 'tests\PPPaint.Imaging.Tests\PPPaint.Imaging.Tests.csproj'
foreach ($required in @($dotnetPath, $releaseScript, $imagingProject)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "検証対象がありません: $required" }
}

# 既存検証にCore/solutionテスト、版・資産・日本語資源検査、品質ゲート、WhatIfが含まれる。
& $releaseScript -ProjectRoot $ProjectRoot -SkipBuild:$SkipBuild -SmokeLaunch:$SmokeLaunch

# Windows実コーデック経路は対象プロジェクトとx64を明示して確認する。
Write-Host '[v0.2.0] Windows Imaging統合テスト'
$testArguments = @('test', $imagingProject, '-c', 'Release', '-p:Platform=x64')
if ($SkipBuild) { $testArguments += '--no-build' }
& $dotnetPath @testArguments
if ($LASTEXITCODE -ne 0) { throw "Windows Imaging統合テストが失敗しました: $LASTEXITCODE" }
Write-Host '[v0.2.0] 自動検証成功。実AI・署名配布・物理デバイス・UI受入は別途確認してください。'
