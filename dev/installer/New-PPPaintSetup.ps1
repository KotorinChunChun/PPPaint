[CmdletBinding(DefaultParameterSetName = 'Signed')]
param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$InnoCompiler,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory, ParameterSetName = 'Signed')]
    [ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CertificateThumbprint,
    [Parameter(Mandatory, ParameterSetName = 'Signed')][string]$CertificateSubject,
    [Parameter(Mandatory, ParameterSetName = 'Signed')][string]$SignTool,
    [Parameter(Mandatory, ParameterSetName = 'Preview')][switch]$UnsignedPreview
)

$ErrorActionPreference = 'Stop'
$archivePath = (Resolve-Path -LiteralPath $Archive).Path
$compilerPath = (Resolve-Path -LiteralPath $InnoCompiler).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw '既存の出力フォルダーは指定できません。' }
if ((Get-FileHash -LiteralPath $compilerPath).Hash -ne 'D06EBD38F38E3CEE60A3C50CC45BD449D77E0BC6A5CABC607EA9886808E4DE1A') {
    throw '確認済みのInno Setup 7.1.0コンパイラーを指定してください。'
}

$signingArguments = @()
if (-not $UnsignedPreview) {
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
    $now = Get-Date
    if ($certificate.Subject -cne $CertificateSubject -or -not $certificate.HasPrivateKey -or
        $now -lt $certificate.NotBefore -or $now -ge $certificate.NotAfter -or
        '1.3.6.1.5.5.7.3.3' -notin @($certificate.EnhancedKeyUsageList | ForEach-Object { $_.ObjectId.ToString() })) {
        throw '指定の証明書の発行先、有効期限、秘密鍵、コード署名用途を確認してください。'
    }
    $signToolPath = (Resolve-Path -LiteralPath $SignTool).Path
    if ((Get-AuthenticodeSignature -LiteralPath $signToolPath).Status -ne 'Valid') {
        throw 'Windows SDKの署名検証済みSignToolを指定してください。'
    }
    $signCommand = '$q{0}$q sign /s My /sha1 {1} /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /d PPPaint $f' -f $signToolPath, $CertificateThumbprint
    $signingArguments = @('--define=DeveloperSigned=1', "--signtool=PPPaintDeveloperSigning=$signCommand")
}

$payload = Join-Path $output payload
$installerFolder = Join-Path $output installer
New-Item -ItemType Directory -Path $payload, $installerFolder | Out-Null
[IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $payload)
$receipt = Get-Content -LiteralPath (Join-Path $payload 'build-receipt.json') -Raw | ConvertFrom-Json
$executable = Get-Item -LiteralPath (Join-Path $payload 'PPPaint.exe')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $root
try { $sourceCommit = (& git rev-parse HEAD).Trim() } finally { Pop-Location }
$project = [xml](Get-Content -LiteralPath (Join-Path $root 'src/PPPaint/PPPaint.csproj') -Raw)
$version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ($version -notmatch '^0\.2\.\d+$') { throw 'PPPaintの製品版が不正です。' }
if ($receipt.SourceCommit -ne $sourceCommit -or $receipt.Version -ne $version -or
    $receipt.ProductVersion -ne "$version+$sourceCommit" -or $executable.VersionInfo.ProductVersion -ne $receipt.ProductVersion -or
    (Get-FileHash -LiteralPath $executable.FullName).Hash -ne $receipt.ExeHash) {
    throw 'ZIPの実行ファイルとビルド記録が一致しません。'
}
$metadataPath = Join-Path $payload 'tool.json'
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) { throw 'ZIPにKotorinToys用tool.jsonがありません。' }
$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
if ($metadata.id -ne 'pppaint' -or $metadata.version -ne $version -or $metadata.appId -ne 'KotorinToys.PPPaint' -or
    @($metadata.capabilities).Count -ne 4 -or @($metadata.capabilities | Sort-Object) -join ',' -ne 'show,start,status,stop') {
    throw 'ZIP内tool.jsonがKotorinToysの管理契約と一致しません。'
}
foreach ($file in @('tool.json','README.md','docs/kotorintoys-integration.md','config/shortcuts.json')) {
    $packaged = Join-Path $payload $file
    $source = Join-Path $root $file
    if (-not (Test-Path -LiteralPath $packaged -PathType Leaf) -or (Get-FileHash -LiteralPath $packaged).Hash -ne (Get-FileHash -LiteralPath $source).Hash) {
        throw "ZIP内の管理用ファイルが現在のソースと一致しません: $file"
    }
}
$unexpected = Get-ChildItem -LiteralPath $payload -Recurse -File |
    Where-Object { $_.Name -match '^\.env($|\.)|\.(pfx|p12|key|pem|db|sqlite|sqlite3)$' }
if ($unexpected) { throw 'ZIPに配布対象外のファイルがあります。' }

$arguments = @('--quiet-progress', "--define=Payload=$payload", "--define=OutputDirectory=$installerFolder") + $signingArguments
& $compilerPath @arguments (Join-Path $PSScriptRoot 'PPPaint.iss')
if ($LASTEXITCODE -ne 0) { throw 'インストーラーのコンパイルに失敗しました。' }
$setup = Join-Path $installerFolder "PPPaint-$version-setup.exe"
if ($UnsignedPreview) {
    $preview = Join-Path $installerFolder "PPPaint-$version-setup-unsigned-preview.exe"
    Move-Item -LiteralPath $setup -Destination $preview
    Get-Item -LiteralPath $preview
    return
}

$signature = Get-AuthenticodeSignature -LiteralPath $setup
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint -or
    $signature.SignerCertificate.Subject -cne $CertificateSubject -or -not $signature.TimeStamperCertificate) {
    throw '開発者署名またはタイムスタンプの照合に失敗しました。'
}
& $signToolPath verify /pa /all /tw $setup
if ($LASTEXITCODE -ne 0) { throw 'SignToolでの公開用署名検証に失敗しました。' }
$hash = (Get-FileHash -LiteralPath $setup).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$setup.sha256" -Value "$hash  $([IO.Path]::GetFileName($setup))" -Encoding ascii
Get-Item -LiteralPath $setup, "$setup.sha256"
