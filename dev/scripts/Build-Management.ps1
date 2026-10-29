[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sdk = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
& $sdk publish (Join-Path $projectRoot 'src/PPPaint/PPPaint.csproj') -c $Configuration -p:Platform=x64 -o (Join-Path $projectRoot 'artifacts/management')
if ($LASTEXITCODE -ne 0) { throw 'PPPaintの管理用ビルドに失敗しました。' }
foreach ($asset in @('PPPaint.exe', 'App.xbf', 'MainWindow.xbf', 'PPPaint.pri')) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot "artifacts/management/$asset"))) { throw "管理用の生成物がありません: $asset" }
}
