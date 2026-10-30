[CmdletBinding()]
param([Parameter(Mandatory)][int]$TargetProcessId, [Parameter(Mandatory)][string]$OutputDirectory)

# Only for a disposable editor with synthetic input and isolated --settings-dir.
# UIA patterns only: this does not verify physical Enter/Esc/Tab or Alt input.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $TargetProcessId
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $outputRoot.StartsWith([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/')), [StringComparison]::OrdinalIgnoreCase)) { throw '出力先はこのリポジトリのartifacts配下に限定してください。' }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$script:DialogEvidence = [Collections.Generic.List[object]]::new()

function Test-DialogCommands([string]$Kind, [string]$Default, [switch]$Primary, [switch]$Secondary) {
    $ids = @('CloseButton'); if ($Primary) { $ids += 'PrimaryButton' }; if ($Secondary) { $ids += 'SecondaryButton' }
    foreach ($id in $ids) {
        $control = Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId "Dialog$id"
        $state = $control.Current
        $key = switch ($id) { PrimaryButton { 'Alt+S' } SecondaryButton { 'Alt+D' } default { 'Alt+C' } }
        if (-not $state.AcceleratorKey.Contains($key) -or -not $state.HelpText.Contains($key) -or -not $state.IsKeyboardFocusable) { throw "ダイアログのキー情報/フォーカスが不足: $Kind/$id" }
        if ($id -eq $Default -and -not $state.AcceleratorKey.Contains('Enter')) { throw "既定操作が不足: $Kind" }
        if ($id -eq 'CloseButton' -and -not $state.AcceleratorKey.Contains('Esc')) { throw "取消操作が不足: $Kind" }
        if ($state.BoundingRectangle.IsEmpty -or $state.IsOffscreen) { throw "ボタンが表示されていません: $Kind/$id" }
        $script:DialogEvidence.Add([pscustomobject]@{ Dialog=$Kind; Id=$id; Name=$state.Name; Key=$state.AcceleratorKey; Bounds=$state.BoundingRectangle.ToString() })
    }
}
function Set-DialogText([string]$Id, [string]$Text) { Set-ElementValue -Element (Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id) -Value $Text }
function Set-DialogWidth([double]$Value) {
    $field = Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId DialogImageWidth
    $field.GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($Value)
}
function Get-ImageDimensions { (Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId DimensionsButton).Current.Name }

$initialDimensions = Get-ImageDimensions
foreach ($iteration in 1..3) {
    Invoke-TestAction DimensionsButton
    Test-DialogCommands "寸法-$iteration" PrimaryButton -Primary
    foreach ($pair in @(@('DialogImageWidth', 'Alt+W'), @('DialogImageHeight', 'Alt+H'))) {
        $field = Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $pair[0]
        if ($field.Current.AcceleratorKey -ne $pair[1]) { throw '寸法欄のキーが不正です。' }
    }
    Invoke-TestAction DialogCloseButton
    if ((Get-ImageDimensions) -ne $initialDimensions) { throw '寸法取消で画像が変わりました。' }
}
Invoke-TestAction DimensionsButton
Set-DialogWidth 450
Invoke-TestAction DialogPrimaryButton
if ((Get-ImageDimensions) -notlike '450 × 275 px*') { throw '寸法変更または縦横比固定が反映されません。' }
Invoke-TestAction UndoButton
if ((Get-ImageDimensions) -ne $initialDimensions) { throw '寸法変更のUndoが反映されません。' }

Invoke-TestAction SettingsButton
Test-DialogCommands 設定 CloseButton
Invoke-TestAction DialogCloseButton
Invoke-TestAction CustomColorButton
Test-DialogCommands 色 PrimaryButton -Primary
Invoke-TestAction DialogCloseButton

Invoke-TestAction OpenButton
Test-DialogCommands 開く PrimaryButton -Primary
Set-DialogText DialogFolder $outputRoot
Set-DialogText DialogFileName ('missing-' + [guid]::NewGuid().ToString('N') + '.png')
Invoke-TestAction DialogPrimaryButton
$inlineError = Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId DialogValidationError
if ([string]::IsNullOrWhiteSpace($inlineError.Current.Name)) { throw 'ファイル名の検証エラーがありません。' }
Invoke-TestAction DialogCloseButton

$badName = 'invalid-' + [guid]::NewGuid().ToString('N') + '.png'
[IO.File]::WriteAllText((Join-Path $outputRoot $badName), 'synthetic invalid image')
Invoke-TestAction OpenButton
Set-DialogText DialogFolder $outputRoot
Set-DialogText DialogFileName $badName
Invoke-TestAction DialogPrimaryButton
Test-DialogCommands エラー CloseButton
Invoke-TestAction DialogCloseButton

$savedPath = Join-Path $outputRoot ('dialog-save-' + [guid]::NewGuid().ToString('N') + '.png')
Invoke-TestAction SaveButton
Test-DialogCommands 上書き確認 CloseButton -Primary
Invoke-TestAction DialogCloseButton
Invoke-TestAction MoreButton
Invoke-TestAction SaveAsMenuItem
Test-DialogCommands 保存 PrimaryButton -Primary
Set-DialogText DialogFolder $outputRoot
Set-DialogText DialogFileName ([IO.Path]::GetFileName($savedPath))
Invoke-TestAction DialogPrimaryButton
$deadline = (Get-Date).AddSeconds(5)
while (-not (Test-Path -LiteralPath $savedPath) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
if (-not (Test-Path -LiteralPath $savedPath)) { throw 'PNGが保存されません。' }
$bitmap = [Drawing.Image]::FromFile($savedPath)
try { if ($bitmap.Width -ne 900 -or $bitmap.Height -ne 550) { throw '保存PNGの寸法が違います。' } } finally { $bitmap.Dispose() }

Invoke-TestAction DimensionsButton
Set-DialogWidth 450
Invoke-TestAction DialogPrimaryButton
$root = [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle)
$window = $root.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)
$window.Close()
Test-DialogCommands 未保存確認 CloseButton -Primary -Secondary
Invoke-TestAction DialogCloseButton
if ((Get-ImageDimensions) -notlike '450 × 275 px*') { throw '未保存確認の取消で編集が失われました。' }
$window.Close()
Test-DialogCommands 未保存確認再表示 CloseButton -Primary -Secondary
Invoke-TestAction DialogSecondaryButton
[pscustomobject]@{ DialogButtons=$script:DialogEvidence; RepeatedOpenVerified=$true; ResizeUndoVerified=$true; FileValidationVerified=$true; PngSave=$savedPath; UnsavedCancelVerified=$true; PhysicalKeyboardVerified=$false } | ConvertTo-Json -Depth 5
