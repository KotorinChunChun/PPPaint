[CmdletBinding()]
param([string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..'))
$ErrorActionPreference = 'Stop'
Push-Location $RepositoryRoot
try {
    function GitText([string[]]$Arguments) {
        $result = & git -c core.quotepath=false @Arguments
        if ($LASTEXITCODE) { throw "Git read failed: $($Arguments[0])" }
        return ($result -join "`n")
    }
    function Indexed([string]$Path) { GitText @('show', ":$Path") }
    foreach ($identity in @('GIT_AUTHOR_IDENT','GIT_COMMITTER_IDENT')) {
        if ((GitText @('var',$identity)) -notmatch '^KotorinChunChun <55196383\+KotorinChunChun@users\.noreply\.github\.com> ') { throw "Unexpected Git identity: $identity" }
    }
    $tracked = @( (GitText @('ls-files')) -split "`n" | Where-Object { $_ })
    $changed = @( (GitText @('diff','--cached','--name-only','--diff-filter=ACMR')) -split "`n" | Where-Object { $_ })
    $metadata = Indexed 'tool.json' | ConvertFrom-Json
    [xml]$project = Indexed 'src/PPPaint/PPPaint.csproj'
    $version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    if ($version -notmatch '^0\.2\.\d+$' -or $metadata.version -ne $version) { throw 'App/tool version mismatch' }
    foreach ($property in @('AssemblyVersion','FileVersion')) {
        if (@($project.Project.PropertyGroup.$property | Where-Object { $_ })[0] -ne "$version.0") { throw "Version mismatch: $property" }
    }
    if (@($project.Project.PropertyGroup.InformationalVersion | Where-Object { $_ })[0] -ne $version) { throw 'InformationalVersion mismatch' }
    $shortcuts = (Indexed 'config/shortcuts.json' | ConvertFrom-Json).shortcuts
    foreach ($property in @('keys','id')) {
        if (@($shortcuts | Group-Object $property | Where-Object Count -gt 1).Count) { throw "Duplicate shortcut $property" }
    }
    # Split the banned terms so the checker does not introduce those terms itself.
    $forbidden = '(?i)' + [regex]::Escape(('k_'+'watanabe')) + '|' + [regex]::Escape(('ibi'+'soku'))
    $textExtensions = @('.cs','.csproj','.xaml','.json','.md','.ps1','.psm1','.xml','.props','.targets','.cmd','.sh','.yml','.yaml','.txt','.config','.svg')
    foreach ($path in $changed) {
        if ([IO.Path]::GetExtension($path) -notin $textExtensions -and $path -notmatch '^\.githooks/') { continue }
        $text = Indexed $path
        if ($text -match $forbidden) { throw "Forbidden identifier in staged file: $path" }
        if ($path.EndsWith('.json')) { $null = ConvertFrom-Json -InputObject $text }
        if ([IO.Path]::GetExtension($path) -in @('.ps1','.psm1')) {
            $parseErrors=$null; $tokens=$null
            $null=[Management.Automation.Language.Parser]::ParseInput($text,[ref]$tokens,[ref]$parseErrors)
            if ($parseErrors.Count) { throw "Invalid PowerShell in staged file: $path" }
        }
        if ($path.EndsWith('.md')) {
            foreach ($match in [regex]::Matches($text,'\[[^\]]*\]\(([^)]+)\)')) {
                $target=$match.Groups[1].Value.Trim('<','>')
                if ($target -match '^(https?://|#|mailto:)') { continue }
                $target=($target -split '#')[0]
                $full=[IO.Path]::GetFullPath((Join-Path (Split-Path (Join-Path $PWD $path)) $target))
                $relative=[IO.Path]::GetRelativePath($PWD,$full).Replace('\','/')
                if ($relative -notin $tracked) { throw "Missing staged document target: $path -> $target" }
            }
        }
    }
    & git diff --cached --check
    if ($LASTEXITCODE) { throw 'Whitespace errors in staged changes' }
    [ordered]@{Status='passed';Version=$version;ChangedFiles=$changed.Count;Source='Git index'} | ConvertTo-Json -Compress
} finally { Pop-Location }
