[CmdletBinding()]
param([string]$OutputDirectory=(Join-Path $PSScriptRoot '../../artifacts/workflow-guards'))
$ErrorActionPreference='Stop'
$checker=Join-Path $PSScriptRoot Test-CommitContent.ps1
$fixture=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture src/PPPaint),(Join-Path $fixture config) -Force | Out-Null
$checks=[Collections.Generic.List[string]]::new()
Push-Location $fixture
try {
    git init --quiet
    git config user.name KotorinChunChun
    git config user.email 55196383+KotorinChunChun@users.noreply.github.com
    git config core.autocrlf false
    function Put([string]$path,[string]$text){[IO.File]::WriteAllText((Join-Path $fixture $path),$text+"`n")}
    Put tool.json '{"version":"0.2.35"}'
    Put src/PPPaint/PPPaint.csproj '<Project><PropertyGroup><Version>0.2.35</Version><AssemblyVersion>0.2.35.0</AssemblyVersion><FileVersion>0.2.35.0</FileVersion><InformationalVersion>0.2.35</InformationalVersion></PropertyGroup></Project>'
    Put config/shortcuts.json '{"shortcuts":[{"keys":"V","id":"select"}]}'
    git add .
    function Check([string]$label,[bool]$expected){
        $output=& pwsh -NoProfile -File $checker -RepositoryRoot $fixture 2>&1
        $passed=$LASTEXITCODE -eq 0
        if($passed -ne $expected){throw "$label unexpected result: $output"}
        $checks.Add($label)
    }
    Check valid $true
    Put tool.json '{"version":"0.2.99"}';git add tool.json
    Put tool.json '{"version":"0.2.35"}'
    Check staged-version-hidden-by-worktree $false
    git add tool.json
    Put config/shortcuts.json '{"shortcuts":[{"keys":"V","id":"a"},{"keys":"V","id":"b"}]}';git add config/shortcuts.json
    Check duplicate-key $false
    Put config/shortcuts.json '{"shortcuts":[]}';git add config/shortcuts.json
    Put test.md ('prohibited: '+('ibi'+'soku'));git add test.md
    Put test.md 'clean working tree'
    Check staged-forbidden-hidden-by-worktree $false
    Put test.md '[missing](missing.md)';git add test.md
    Check missing-index-link $false
    Put missing.md 'not indexed yet'
    Check untracked-link-target $false
    git add missing.md
    Check indexed-link-target $true
    Put test.md '[directory](src/PPPaint)';git add test.md
    Check indexed-directory-link $true
    Put test.md '[directory](untracked-directory)';git add test.md
    New-Item -ItemType Directory untracked-directory | Out-Null
    Check untracked-directory-link $false
    Put test.md 'clean';git add test.md
    git config user.name WrongIdentity
    Check wrong-identity $false
    git config user.name KotorinChunChun
    Put broken.ps1 'if (';git add broken.ps1
    Check invalid-powershell $false
    Put broken.ps1 "'ok'";git add broken.ps1
    Check final-valid $true
    [ordered]@{Status='passed';Checks=$checks;Fixture=$fixture}|ConvertTo-Json -Depth 3|Tee-Object -FilePath (Join-Path $fixture result.json)
} finally {Pop-Location}
