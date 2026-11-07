[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$ReleaseReceipt,
    [Parameter(Mandatory)][string]$NotesPath,
    [switch]$Publish
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$receipt=Get-Content -LiteralPath $ReleaseReceipt -Raw | ConvertFrom-Json
$notes=Get-Content -LiteralPath $NotesPath -Raw
if($receipt.Status -ne 'passed' -or (Get-FileHash -LiteralPath $receipt.Archive).Hash -ne $receipt.ArchiveHash){throw 'Release archive receipt/hash mismatch'}
$checksum=$receipt.Archive+'.sha256'
if((Get-Content -LiteralPath $checksum -Raw).Trim() -ne ($receipt.ArchiveHash+'  '+[IO.Path]::GetFileName($receipt.Archive))){throw 'Checksum file mismatch'}
Push-Location $root
$headers=$null;$fields=@{};$credentialReply=$null
$oldInteractive=$env:GCM_INTERACTIVE;$oldPrompt=$env:GIT_TERMINAL_PROMPT
try {
    $remote=git remote get-url origin
    if($remote -notin @('https://KotorinChunChun@github.com/KotorinChunChun/PPPaint.git','https://github.com/KotorinChunChun/PPPaint.git')){throw 'Unexpected origin'}
    if((git branch --show-current) -ne 'main' -or (git status --porcelain)){throw 'Use a committed, clean main branch'}
    git merge-base --is-ancestor $receipt.SourceCommit HEAD
    if($LASTEXITCODE){throw 'Release source is not an ancestor of HEAD'}
    $tag='v'+$receipt.Version
    $plan=[ordered]@{Repository='KotorinChunChun/PPPaint';Branch='main';Tag=$tag;TargetCommit=$receipt.SourceCommit;Archive=$receipt.Archive;ArchiveHash=$receipt.ArchiveHash;NotesPath=[IO.Path]::GetFullPath($NotesPath);Published=$false}
    if(-not $Publish){$plan|ConvertTo-Json;return}
    if(-not $PSCmdlet.ShouldProcess('KotorinChunChun/PPPaint',"Push main and create $tag with ZIP/checksum")){return}
    $env:GCM_INTERACTIVE='Never';$env:GIT_TERMINAL_PROMPT='0'
    $credentialReply=(@('protocol=https','host=github.com','username=KotorinChunChun','','') -join "`n") | git -c credential.interactive=false credential fill 2>$null
    if($LASTEXITCODE){throw 'Authenticate KotorinChunChun on this PC before publishing.'}
    foreach($line in $credentialReply){if($line -match '^([^=]+)=(.*)$'){$fields[$matches[1]]=$matches[2]}}
    if(-not $fields.password){throw 'No stored GitHub credential'}
    $headers=@{Authorization=('Bearer '+$fields.password);Accept='application/vnd.github+json';'User-Agent'='PPPaint-release'}
    $account=Invoke-RestMethod https://api.github.com/user -Headers $headers
    if($account.login -ne 'KotorinChunChun'){throw 'Authenticated account is not KotorinChunChun'}
    $api='https://api.github.com/repos/KotorinChunChun/PPPaint'
    $existing=@(Invoke-RestMethod ($api+'/releases?per_page=100') -Headers $headers | ForEach-Object {$_})
    if(@($existing | Where-Object tag_name -eq $tag).Count){throw 'Release already exists; inspect it without replacing assets.'}
    $remoteTag=git -c credential.interactive=false ls-remote --tags origin "refs/tags/$tag"
    if($LASTEXITCODE -or $remoteTag){throw 'Remote tag exists or cannot be checked'}
    $localTag=git tag --list $tag
    if($localTag){throw 'Local tag exists; inspect it before publishing'}
    git -c credential.interactive=false fetch origin main
    if($LASTEXITCODE){throw 'Fetch failed'}
    git merge-base --is-ancestor origin/main HEAD
    if($LASTEXITCODE){throw 'Remote main is not an ancestor; reconcile without force push'}
    git -c credential.interactive=false push origin HEAD:main
    if($LASTEXITCODE){throw 'Push failed'}
    git tag $tag $receipt.SourceCommit
    if($LASTEXITCODE){throw 'Tag creation failed'}
    git -c credential.interactive=false push origin "refs/tags/$tag"
    if($LASTEXITCODE){throw 'Tag push failed; inspect local/remote state before retrying'}
    # Keep the release as a draft until both uploads have completed.
    $payload=@{tag_name=$tag;target_commitish=$receipt.SourceCommit;name="PPPaint $tag";body=$notes;draft=$true;prerelease=$false}|ConvertTo-Json
    $release=Invoke-RestMethod ($api+'/releases') -Headers $headers -Method Post -ContentType application/json -Body ([Text.Encoding]::UTF8.GetBytes($payload))
    foreach($asset in @($receipt.Archive,$checksum)){
        $url=($release.upload_url -replace '\{.*$','')+'?name='+[Uri]::EscapeDataString([IO.Path]::GetFileName($asset))
        $null=Invoke-RestMethod $url -Headers $headers -Method Post -ContentType application/octet-stream -InFile $asset
    }
    $published=Invoke-RestMethod ($api+'/releases/'+$release.id) -Headers $headers -Method Patch -ContentType application/json -Body '{"draft":false}'
    [ordered]@{Published=$true;Url=$published.html_url;Tag=$tag;TargetCommit=$receipt.SourceCommit}|ConvertTo-Json
} finally {
    if($headers){$headers.Clear()};$fields.Clear();$credentialReply=$null
    $env:GCM_INTERACTIVE=$oldInteractive;$env:GIT_TERMINAL_PROMPT=$oldPrompt
    Pop-Location
}
