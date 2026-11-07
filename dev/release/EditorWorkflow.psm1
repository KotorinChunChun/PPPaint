$ErrorActionPreference='Stop'
function Get-EditorVersion([string]$RepositoryRoot) {
    [xml]$project=Get-Content -LiteralPath (Join-Path $RepositoryRoot src/PPPaint/PPPaint.csproj) -Raw
    $version=@($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    $metadata=Get-Content -LiteralPath (Join-Path $RepositoryRoot tool.json) -Raw | ConvertFrom-Json
    if($version -notmatch '^0\.2\.\d+$' -or $metadata.version -ne $version){throw 'App/tool version mismatch'}
    return $version
}
function Get-EditorSourceHash([string]$RepositoryRoot) {
    Push-Location $RepositoryRoot
    try {
        $files=@(git -c core.quotepath=false ls-files -- src tests config dev/release dev/validation global.json NuGet.Config Directory.Build.props Directory.Build.targets PPPaint.slnx)
        if($LASTEXITCODE){throw 'Cannot inventory source files'}
        $lines=foreach($file in ($files | Sort-Object -CaseSensitive)) {
            if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing source file: $file"}
            $file+' '+(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        }
        return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))))
    } finally {Pop-Location}
}
function Write-EditorJson([object]$Value,[string]$Path) {
    $full=[IO.Path]::GetFullPath($Path)
    New-Item -ItemType Directory -Path (Split-Path $full) -Force | Out-Null
    $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $full -Encoding utf8
}
Export-ModuleMember -Function Get-EditorVersion,Get-EditorSourceHash,Write-EditorJson
