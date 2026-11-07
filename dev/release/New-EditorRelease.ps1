[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ValidationReceipt,
    [string]$DotnetPath='dotnet',
    [string]$ArchivePath
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Import-Module (Join-Path $PSScriptRoot EditorWorkflow.psm1) -Force
Push-Location $root
try {
    if(git status --porcelain){throw 'Commit changes before creating a release.'}
    $version=Get-EditorVersion $root
    $receipt=Get-Content -LiteralPath $ValidationReceipt -Raw | ConvertFrom-Json
    if($receipt.Status -ne 'passed' -or $receipt.Version -ne $version -or $receipt.SourceHash -ne (Get-EditorSourceHash $root)){throw 'Validation receipt does not match current source.'}
    $output=[IO.Path]::GetFullPath($OutputDirectory)
    if(-not $ArchivePath){$ArchivePath=Join-Path (Split-Path $output) "PPPaint-v$version-win-x64.zip"}
    $archive=[IO.Path]::GetFullPath($ArchivePath)
    if(Test-Path -LiteralPath $output){throw 'Release directory already exists; it will not be overwritten.'}
    if(Test-Path -LiteralPath $archive){throw 'Release archive already exists; it will not be overwritten.'}
    if($archive.StartsWith($output+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Archive must be outside the release directory.'}
    $commit=git rev-parse HEAD
    & $DotnetPath publish src/PPPaint/PPPaint.csproj -c Release -p:Platform=x64 --no-restore --self-contained true -o $output --nologo
    if($LASTEXITCODE){throw 'Publish failed'}
    & (Join-Path $PSScriptRoot New-EditorLauncher.ps1) -ReleaseDirectory $output | Out-Null
    [IO.File]::WriteAllText((Join-Path $output Start-PPPaint-Parallel.cmd),('@echo off'+"`r`n"+'start "" "%~dp0PPPaint.exe" --settings-dir "%LOCALAPPDATA%\PPPaint\profiles\editor-v'+$version+'"'+"`r`n"),[Text.ASCIIEncoding]::new())
    Copy-Item -LiteralPath (Join-Path $root LICENSE) -Destination (Join-Path $output LICENSE.txt)
    $assets=Get-Content src/PPPaint.Imaging/obj/project.assets.json -Raw | ConvertFrom-Json
    $library=@($assets.libraries.PSObject.Properties | Where-Object Name -like 'SkiaSharp.NativeAssets.Win32/*')
    if($library.Count -ne 1){throw 'Cannot identify SkiaSharp native package'}
    $licenseSource=$null
    foreach($folder in $assets.packageFolders.PSObject.Properties.Name){
        $candidate=Join-Path $folder $library[0].Value.path
        if(Test-Path -LiteralPath (Join-Path $candidate THIRD-PARTY-NOTICES.txt)){$licenseSource=$candidate;break}
    }
    if(-not $licenseSource){throw 'SkiaSharp notices not found'}
    $licenses=Join-Path $output licenses/SkiaSharp
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    foreach($name in @('LICENSE.txt','THIRD-PARTY-NOTICES.txt')){Copy-Item -LiteralPath (Join-Path $licenseSource $name) -Destination (Join-Path $licenses $name)}
    foreach($file in @('PPPaint.exe','PPPaint.dll','App.xbf','EditorWindow.xbf','PPPaint.pri','Assets/PPPaintCursors.dll','coreclr.dll','Start-PPPaint.cmd','Start-PPPaint-Parallel.cmd','LICENSE.txt','licenses/SkiaSharp/LICENSE.txt','licenses/SkiaSharp/THIRD-PARTY-NOTICES.txt')){
        if(-not(Test-Path -LiteralPath (Join-Path $output $file) -PathType Leaf)){throw "Required release file missing: $file"}
    }
    $product=(Get-Item -LiteralPath (Join-Path $output PPPaint.exe)).VersionInfo.ProductVersion
    if($product -ne "$version+$commit"){throw "ProductVersion/commit mismatch: $product"}
    if($receipt.SourceHash -ne (Get-EditorSourceHash $root)){throw 'Source changed during publish'}
    Write-EditorJson ([ordered]@{Version=$version;SourceCommit=$commit;SourceHash=$receipt.SourceHash;ProductVersion=$product;ExeHash=(Get-FileHash (Join-Path $output PPPaint.exe)).Hash;CreatedAt=(Get-Date -Format o)}) (Join-Path $output build-receipt.json)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($output,$archive,[IO.Compression.CompressionLevel]::Optimal,$false)
    $hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    [IO.File]::WriteAllText($archive+'.sha256',($hash+'  '+[IO.Path]::GetFileName($archive)+"`n"),[Text.UTF8Encoding]::new($false))
    Write-EditorJson ([ordered]@{Status='passed';Version=$version;SourceCommit=$commit;SourceHash=$receipt.SourceHash;OutputDirectory=$output;Archive=$archive;ArchiveHash=$hash;CreatedAt=(Get-Date -Format o)}) ($archive+'.json')
    $archive
} finally {Pop-Location}
