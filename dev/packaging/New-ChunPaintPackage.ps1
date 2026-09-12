[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$Publisher,
    [ValidatePattern('^[A-Za-z0-9.-]{3,50}$')][string]$IdentityName = 'ChunPaint',
    [string]$CertificateThumbprint,
    [string]$TimestampUri,
    [string]$UpdateBaseUri,
    [string]$SdkBinDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Core') { throw 'pwshで実行してください。' }
$packageVersion = [version]$Version
if (@($packageVersion.Major,$packageVersion.Minor,$packageVersion.Build,$packageVersion.Revision | Where-Object { $_ -gt 65535 }).Count) { throw '版番号の各要素は65535以下が必要です。' }
if ([string]::IsNullOrWhiteSpace($Publisher)) { throw 'Publisherが必要です。' }
function Assert-Https([string]$Value) {
    $parsed = $null
    if (-not [uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$parsed) -or $parsed.Scheme -ne 'https' -or $parsed.UserInfo -or $parsed.Fragment -or $parsed.Query) { throw '資格情報、クエリ、フラグメントのないHTTPS URLが必要です。' }
}
if ($UpdateBaseUri) { Assert-Https $UpdateBaseUri }
if ($TimestampUri) { Assert-Https $TimestampUri }
if ($CertificateThumbprint -and $CertificateThumbprint -notmatch '^[A-Fa-f0-9]{40}$') { throw '証明書拇印は40桁の16進数が必要です。' }
$sourcePath = (Resolve-Path -LiteralPath $PublishDirectory).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$sourcePrefix = $sourcePath.TrimEnd('\') + '\'
$outputPrefix = $outputPath.TrimEnd('\') + '\'
if ($outputPath -eq $sourcePath -or $outputPath.StartsWith($sourcePrefix,[StringComparison]::OrdinalIgnoreCase) -or $sourcePath.StartsWith($outputPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw '入力と出力は重複しない別ディレクトリにしてください。' }
if (-not (Test-Path -LiteralPath (Join-Path $sourcePath 'ChunPaint.exe'))) { throw 'publish済みChunPaint.exeがありません。' }
if (Get-ChildItem -LiteralPath $sourcePath -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw '入力に再解析ポイントを含められません。' }
$ancestor = $outputPath
while ($ancestor) {
    if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '出力経路に再解析ポイントを含められません。' }
    $ancestor = [IO.Path]::GetDirectoryName($ancestor)
}
if (-not $PSCmdlet.ShouldProcess($outputPath, 'MSIXと更新情報を新規作成')) { return }
function Find-SdkTool([string]$Name) {
    if ($SdkBinDirectory) {
        $explicit = Join-Path $SdkBinDirectory $Name
        if (Test-Path -LiteralPath $explicit -PathType Leaf) { return $explicit }
        throw "SDKツールがありません: $explicit"
    }
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $sdkRoot) {
        foreach ($directory in (Get-ChildItem -LiteralPath $sdkRoot -Directory | Sort-Object Name -Descending)) {
            $candidate = Join-Path $directory.FullName "x64\$Name"
            if (Test-Path -LiteralPath $candidate) { return $candidate }
        }
    }
    throw "Windows SDKの$Nameが必要です。-SdkBinDirectoryで指定できます。"
}
function Invoke-Tool([string]$Tool, [string[]]$Arguments) {
    & $Tool @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Tool が終了コード $LASTEXITCODE で失敗しました。" }
}
$makeAppx = Find-SdkTool 'makeappx.exe'
$signTool = $null
if ($CertificateThumbprint) { $signTool = Find-SdkTool 'signtool.exe' }
$packageName = "$IdentityName-$Version-x64.msix"
$packagePath = Join-Path $outputPath $packageName
if (Test-Path -LiteralPath $packagePath) { throw "既存の成果物を上書きできません: $packagePath" }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$stage = Join-Path $outputPath ('stage-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
Get-ChildItem -LiteralPath $sourcePath -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $stage -Recurse }
Add-Type -AssemblyName PresentationCore
$iconPath = Join-Path $sourcePath 'Assets\ChunPaintIcon.ico'
$decoder = [Windows.Media.Imaging.BitmapDecoder]::Create([uri]$iconPath,[Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,[Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
$frame = $decoder.Frames | Sort-Object PixelWidth -Descending | Select-Object -First 1
$assetPath = Join-Path $stage 'PackageAssets'
New-Item -ItemType Directory -Path $assetPath | Out-Null
foreach ($size in @(50,150,44)) {
    $bitmap = [Windows.Media.Imaging.TransformedBitmap]::new($frame,[Windows.Media.ScaleTransform]::new($size / $frame.PixelWidth,$size / $frame.PixelHeight))
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create((Join-Path $assetPath "Logo$size.png"))
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}
$escapedPublisher = [Security.SecurityElement]::Escape($Publisher)
@"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities" IgnorableNamespaces="uap rescap">
  <Identity Name="$IdentityName" Publisher="$escapedPublisher" Version="$Version" ProcessorArchitecture="x64" />
  <Properties><DisplayName>ChunPaint</DisplayName><PublisherDisplayName>ChunPaint</PublisherDisplayName><Logo>PackageAssets\Logo50.png</Logo></Properties>
  <Resources><Resource Language="ja-JP" /></Resources>
  <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
  <Applications><Application Id="App" Executable="ChunPaint.exe" EntryPoint="Windows.FullTrustApplication">
    <uap:VisualElements DisplayName="ChunPaint" Description="画像の描画と編集" BackgroundColor="transparent" Square150x150Logo="PackageAssets\Logo150.png" Square44x44Logo="PackageAssets\Logo44.png" />
    <Extensions><uap:Extension Category="windows.fileTypeAssociation"><uap:FileTypeAssociation Name="chunpaint"><uap:DisplayName>ChunPaintプロジェクト</uap:DisplayName><uap:SupportedFileTypes><uap:FileType>.chunpaint</uap:FileType></uap:SupportedFileTypes></uap:FileTypeAssociation></uap:Extension><uap:Extension Category="windows.protocol"><uap:Protocol Name="chunpaint" /></uap:Extension></Extensions>
  </Application></Applications>
  <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
</Package>
"@ | Set-Content -LiteralPath (Join-Path $stage 'AppxManifest.xml') -Encoding utf8
Invoke-Tool $makeAppx @('pack','/d',$stage,'/p',$packagePath,'/h','SHA256')
if ($CertificateThumbprint) {
    $signArguments = @('sign','/fd','SHA256','/sha1',$CertificateThumbprint)
    if ($TimestampUri) { $signArguments += @('/tr',$TimestampUri,'/td','SHA256') }
    Invoke-Tool $signTool ($signArguments + $packagePath)
    Invoke-Tool $signTool @('verify','/pa','/v',$packagePath)
} else { Write-Warning '未署名MSIXです。配布・インストール検証は完了していません。' }
if ($UpdateBaseUri) {
    $base = $UpdateBaseUri.TrimEnd('/')
    $packageUri = [Security.SecurityElement]::Escape("$base/$packageName")
    $installerUri = [Security.SecurityElement]::Escape("$base/ChunPaint.appinstaller")
@"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2017/2" Uri="$installerUri" Version="$Version">
  <MainPackage Name="$IdentityName" Publisher="$escapedPublisher" Version="$Version" ProcessorArchitecture="x64" Uri="$packageUri" />
  <UpdateSettings><OnLaunch HoursBetweenUpdateChecks="12" /></UpdateSettings>
</AppInstaller>
"@ | Set-Content -LiteralPath (Join-Path $outputPath 'ChunPaint.appinstaller') -Encoding utf8
    @{ Version=$Version; Publisher=$Publisher; DownloadUrl="$base/$packageName"; Sha256=(Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'update.json') -Encoding utf8
}
[pscustomobject]@{ Package=$packagePath; Signed=[bool]$CertificateThumbprint; StagingDirectory=$stage }
