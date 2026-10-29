[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$OutputDirectory,
    [string]$ReferenceDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Core') { throw 'pwshで実行してください。' }
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $ProjectRoot 'artifacts'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $artifactRoot ('brush-comparison-' + [guid]::NewGuid().ToString('N')) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith($artifactRoot.TrimEnd('\') + '\',[StringComparison]::OrdinalIgnoreCase)) { throw '出力はプロジェクト内artifacts配下に限定してください。' }
if (Test-Path -LiteralPath $output) { throw '出力先は未作成のディレクトリを指定してください。上書きはしません。' }
$ancestor = [IO.Path]::GetDirectoryName($output)
while ($ancestor) {
    if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '出力経路に再解析ポイントを含められません。' }
    $ancestor = [IO.Path]::GetDirectoryName($ancestor)
}
$reference = if ($ReferenceDirectory) { (Resolve-Path -LiteralPath $ReferenceDirectory).Path } else { '' }
$brushSourcePath = Join-Path $ProjectRoot 'src\PPPaint\MainWindow.TextAndBrushes.cs'
$source = Get-Content -LiteralPath $brushSourcePath -Raw
$switchStart = $source.IndexOf('        var profile = BrushStrokePlanner.GetProfile(_brushKind);',[StringComparison]::Ordinal)
$methodStart = $source.IndexOf('    private static void DrawRoundStroke',[StringComparison]::Ordinal)
$methodEnd = $source.IndexOf('    private async Task ShowTextDialogAndInsertAsync',[StringComparison]::Ordinal)
if ($switchStart -lt 0 -or $methodStart -le $switchStart -or $methodEnd -le $methodStart) { throw '描画メソッドの境界が変わっています。生成ツールを現行ソースへ合わせてください。' }
$switchBody = $source.Substring($switchStart,$methodStart-$switchStart).TrimEnd()
if (-not $switchBody.EndsWith('}')) { throw '描画メソッド末尾を確認できません。' }
$switchBody = $switchBody.Substring(0,$switchBody.Length-1).Replace('_brushKind','kind')
$methods = $source.Substring($methodStart,$methodEnd-$methodStart)
$template = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'New-BrushComparison.Program.cs') -Raw
$program = $template.Replace('/*DRAW_SWITCH*/',$switchBody).Replace('/*DRAW_METHODS*/',$methods)
$generator = Join-Path $output 'generator'
New-Item -ItemType Directory -Path $generator -Force | Out-Null
$program | Set-Content -LiteralPath (Join-Path $generator 'Comparison.cs') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'src\PPPaint.Core\BrushStrokePlanner.cs') -Destination $generator
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0-windows10.0.19041.0</TargetFramework><RuntimeIdentifier>win-x64</RuntimeIdentifier><PlatformTarget>x64</PlatformTarget><IsTestProject>true</IsTestProject><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
    <PackageReference Include="MSTest.TestAdapter" Version="4.3.3" />
    <PackageReference Include="MSTest.TestFramework" Version="4.3.3" />
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="2.4.0" />
    <PackageReference Include="Microsoft.Graphics.Win2D" Version="1.4.0" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $generator 'Comparison.csproj') -Encoding utf8
$previousOutput = $env:PPPAINT_BRUSH_OUTPUT
$previousReference = $env:PPPAINT_BRUSH_REFERENCE
try {
    $env:PPPAINT_BRUSH_OUTPUT = $output
    $env:PPPAINT_BRUSH_REFERENCE = $reference
    & (Join-Path $ProjectRoot '.tools\dotnet\dotnet.exe') test (Join-Path $generator 'Comparison.csproj') -c Release -v quiet
    if ($LASTEXITCODE -ne 0) { throw "ブラシ比較fixtureが失敗しました: $LASTEXITCODE" }
} finally {
    $env:PPPAINT_BRUSH_OUTPUT = $previousOutput
    $env:PPPAINT_BRUSH_REFERENCE = $previousReference
}
@{ BrushSourceSha256=(Get-FileHash -LiteralPath $brushSourcePath -Algorithm SHA256).Hash; PlannerSourceSha256=(Get-FileHash -LiteralPath (Join-Path $generator 'BrushStrokePlanner.cs') -Algorithm SHA256).Hash; ReferenceProvided=[bool]$reference } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'source-hashes.json') -Encoding utf8
Write-Host "ブラシ比較結果: $output"
