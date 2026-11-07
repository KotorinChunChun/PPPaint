[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    $existing=git config --get core.hooksPath
    if ($existing -and $existing -ne '.githooks') { throw 'An existing hooksPath is configured; inspect it before replacing.' }
    $legacy=git rev-parse --git-path hooks/pre-commit
    if (-not $existing -and (Test-Path -LiteralPath $legacy)) { throw 'An existing pre-commit hook must be reviewed before installation.' }
    git config --local core.hooksPath .githooks
    if ($LASTEXITCODE) { throw 'Hook installation failed' }
    'Installed repository-local .githooks'
} finally { Pop-Location }
