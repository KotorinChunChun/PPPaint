[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference='Stop'
$folder=(Resolve-Path -LiteralPath $ReleaseDirectory).Path
if(-not(Test-Path -LiteralPath (Join-Path $folder PPPaint.exe))){throw 'PPPaint.exeのある配布フォルダーを指定してください。'}
$lines=@('@echo off','start "" "%~dp0PPPaint.exe" --settings-dir "%LOCALAPPDATA%\PPPaint\profiles\editor"')
$path=Join-Path $folder Start-PPPaint.cmd
[IO.File]::WriteAllText($path,($lines -join "`r`n")+"`r`n",[Text.ASCIIEncoding]::new())
Get-Item -LiteralPath $path
