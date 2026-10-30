[CmdletBinding()]
param([Parameter(Mandatory)][int]$TargetProcessId, [Parameter(Mandatory)][string]$OutputDirectory)

# Disposable editor, synthetic 900x550 source, isolated settings. No physical input.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $TargetProcessId
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $outputRoot.StartsWith([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/')), [StringComparison]::OrdinalIgnoreCase)) { throw '試験出力はartifacts配下に限定してください。' }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$evidence = [Collections.Generic.List[object]]::new()
function Find-TestElement([string]$Id) { Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id }
function Set-TestText([string]$Id, [string]$Value) { Set-ElementValue -Element (Find-TestElement $Id) -Value $Value }
function Assert-Dimensions([int]$Width, [int]$Height) {
    $name = (Find-TestElement DimensionsButton).Current.Name
    if ($name -notlike "$Width × $Height px*") { throw "寸法が違います: $name" }
}
function Resize-TestImage([int]$Width, [int]$Height) {
    Invoke-TestAction DimensionsButton
    (Find-TestElement DialogImageWidth).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($Width)
    Invoke-TestAction DialogPrimaryButton
    Assert-Dimensions $Width $Height
}
function Save-TestAs([string]$Path) {
    Invoke-TestAction MoreButton
    Invoke-TestAction SaveAsMenuItem
    Set-TestText DialogFolder ([IO.Path]::GetDirectoryName($Path))
    Set-TestText DialogFileName ([IO.Path]::GetFileName($Path))
    Invoke-TestAction DialogPrimaryButton
}
function Wait-TestFile([string]$Path) {
    $deadline = (Get-Date).AddSeconds(8)
    while (-not (Test-Path -LiteralPath $Path) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path -LiteralPath $Path)) { throw "ファイルが保存されません: $Path" }
}
function Assert-ImageFile([string]$Path, [string]$Format, [int]$Width, [int]$Height) {
    Wait-TestFile $Path
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($Format -eq 'jpeg' -and ($bytes[0] -ne 255 -or $bytes[1] -ne 216)) { throw 'JPEGで保存されていません。' }
    if ($Format -eq 'png' -and ($bytes[0] -ne 137 -or $bytes[1] -ne 80)) { throw 'PNGで保存されていません。' }
    $image = [Drawing.Image]::FromFile($Path)
    try { if ($image.Width -ne $Width -or $image.Height -ne $Height) { throw '保存画像の寸法が違います。' } } finally { $image.Dispose() }
}
function Assert-Bytes([string]$Path, [byte[]]$Expected) {
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($Path)) -ne [Convert]::ToBase64String($Expected)) { throw "保持する画像が変更されました: $Path" }
}
function Assert-EditorDirty {
    $root = [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle)
    if (-not $root.Current.Name.StartsWith('* ')) { throw '未保存の編集が保存済み扱いになっています。' }
    if (-not (Find-TestElement UndoButton).Current.IsEnabled) { throw 'Undo履歴が失われました。' }
}

Assert-Dimensions 900 550
$root = [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle)
$tools = @($root.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.AutomationId -like 'Tool*' -and $_.Current.ControlType -eq [Windows.Automation.ControlType]::Button })
if ($tools.Count -ne 16) { throw '16ツールが揃っていません。' }
foreach ($id in @('NewButton','OpenButton','PasteButton','CopyButton','SaveButton','UndoButton','RedoButton','MoreButton','SettingsButton')) {
    $button = Find-TestElement $id; $state = $button.Current
    if ([string]::IsNullOrWhiteSpace($state.AcceleratorKey) -or -not $state.HelpText.Contains($state.AcceleratorKey)) { throw '1行目のキー情報が不足しています。' }
    $captionParts = @($button.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::Text } | ForEach-Object { $_.Current.Name })
    if (($captionParts -join '') -match '\((Ctrl|Alt)\+|\([A-Z]\)') { throw "1行目のキャプションにキーが残っています: $id" }
    $evidence.Add([pscustomobject]@{ Id=$id; Name=$state.Name; Key=$state.AcceleratorKey; CaptionParts=$captionParts; Bounds=$state.BoundingRectangle.ToString() })
}
if ((Find-TestElement NewButton).Current.BoundingRectangle.Width -gt 100) { throw '新規ボタンがキー表示時の幅のままです。' }
foreach ($item in @(@('Callout','TextInput'), @('Mosaic','MosaicSizeBox'), @('Watermark','OpacityBox'))) {
    Select-TestTool $item[0]
    $option = Find-TestElement $item[1]
    if ($option.Current.IsOffscreen -or -not $option.Current.IsEnabled) { throw "ツール設定が操作できません: $($item[0])" }
}
(Find-TestElement OpacityBox).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(55)
Select-TestTool Mosaic
(Find-TestElement MosaicSizeBox).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(24)
Select-TestTool Select
Invoke-TestAction SettingsButton
Select-TestTool Mosaic
if ((Find-TestElement ToolSelect).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [Windows.Automation.ToggleState]::On -or (Find-TestElement ToolMosaic).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [Windows.Automation.ToggleState]::Off) { throw 'ダイアログ中に背後のツール選択が変わりました。' }
Invoke-TestAction DialogCloseButton

$jpegPath = Join-Path $outputRoot ('saved-' + [guid]::NewGuid().ToString('N') + '.jpg')
Save-TestAs $jpegPath
Assert-ImageFile $jpegPath jpeg 900 550
$jpegOriginal = [IO.File]::ReadAllBytes($jpegPath)
Resize-TestImage 450 275
Invoke-TestAction SaveButton
Assert-ImageFile $jpegPath jpeg 450 275
$backups = @(Get-ChildItem -LiteralPath $outputRoot -File | Where-Object Name -Like '*.pppaint-backup-*')
if ($backups.Count -ne 1) { throw '最初の保存のバックアップ数が不正です。' }
Assert-Bytes $backups[0].FullName $jpegOriginal
$beforeFailure = [IO.File]::ReadAllBytes($jpegPath)

Resize-TestImage 360 220
[IO.File]::SetAttributes($jpegPath,[IO.FileAttributes]::ReadOnly)
try {
    Invoke-TestAction SaveButton
    Invoke-TestAction DialogCloseButton
    Assert-Bytes $jpegPath $beforeFailure
    Assert-Dimensions 360 220
    Assert-EditorDirty
    Invoke-TestAction UndoButton
    Assert-Dimensions 450 275
    Invoke-TestAction RedoButton
    Assert-Dimensions 360 220
} finally { [IO.File]::SetAttributes($jpegPath,[IO.FileAttributes]::Normal) }

$invalid = Join-Path $outputRoot ('invalid-' + [guid]::NewGuid().ToString('N') + '.png')
[IO.File]::WriteAllText($invalid,'synthetic invalid image')
Invoke-TestAction OpenButton
Set-TestText DialogFolder $outputRoot
Set-TestText DialogFileName ([IO.Path]::GetFileName($invalid))
Invoke-TestAction DialogPrimaryButton
Invoke-TestAction DialogCloseButton
Assert-Dimensions 360 220
Assert-EditorDirty
Invoke-TestAction SaveButton
Assert-ImageFile $jpegPath jpeg 360 220
Resize-TestImage 180 110

# Replace an unrelated target after its overwrite confirmation has already opened.
$competing = Join-Path $outputRoot ('competing-' + [guid]::NewGuid().ToString('N') + '.jpg')
[IO.File]::WriteAllBytes($competing,$jpegOriginal)
Save-TestAs $competing
$overwrite = Find-TestElement DialogPrimaryButton
if ($overwrite.Current.Name -ne '上書き') { throw '上書き確認がありません。' }
[IO.File]::WriteAllBytes($competing,$beforeFailure)
Invoke-TestAction DialogPrimaryButton
$conflict = Find-TestElement DialogPrimaryButton
if ($conflict.Current.Name -ne '別名保存') { throw '確認中の変更が競合として検出されません。' }
Assert-Bytes $competing $beforeFailure
Invoke-TestAction DialogCloseButton
Assert-EditorDirty
Assert-Dimensions 180 110

# Change the actual source, then recover the current edit to a new PNG.
[IO.File]::WriteAllBytes($jpegPath,$jpegOriginal)
Invoke-TestAction SaveButton
$conflict = Find-TestElement DialogPrimaryButton
if ($conflict.Current.Name -ne '別名保存') { throw '元ファイルの変更が検出されません。' }
Invoke-TestAction DialogPrimaryButton
$recovered = Join-Path $outputRoot ('recovered-' + [guid]::NewGuid().ToString('N') + '.png')
Set-TestText DialogFolder $outputRoot
Set-TestText DialogFileName ([IO.Path]::GetFileName($recovered))
Invoke-TestAction DialogPrimaryButton
Assert-ImageFile $recovered png 180 110
Assert-Bytes $jpegPath $jpegOriginal
Assert-Bytes $competing $beforeFailure
$root.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
[pscustomobject]@{ ToolCount=$tools.Count; TopRow=$evidence; NewOptionsVerified=$true; ModalToolbarGuardVerified=$true; JpegSaveVerified=$true; BackupBytesVerified=$true; SaveFailureRetryAndUndoVerified=$true; DecodeFailurePreservesEdit=$true; ConflictDuringConfirmationVerified=$true; ChangedSourcePreserved=$true; RecoveryPng=$recovered; PhysicalInputVerified=$false } | ConvertTo-Json -Depth 5
