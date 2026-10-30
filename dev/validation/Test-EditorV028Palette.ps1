[CmdletBinding()]
param([Parameter(Mandatory)][int]$TargetProcessId, [Parameter(Mandatory)][string]$SettingsDirectory, [Parameter(Mandatory)][string]$OutputDirectory)

# Disposable editor and synthetic image, UIA patterns only. Does not verify native double-click/keys.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $TargetProcessId
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/'))
foreach($directory in @($SettingsDirectory,$OutputDirectory)) {
    if(-not [IO.Path]::GetFullPath($directory).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)) {throw '検証用のartifacts配下に限定してください。'}
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
function Find-Control([string]$Id) { Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id }
function Assert-Selected([int]$Index) {
    foreach($i in 0..7) {
        $button=Find-Control "PaletteColor$i"
        $checked=$button.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [Windows.Automation.ToggleState]::On
        if($checked -ne ($i -eq $Index) -or (($button.Current.ItemStatus -eq '選択中') -ne ($i -eq $Index))) {throw 'パレットの選択表示が不正です。'}
    }
}
Assert-Selected 0
$button=Find-Control PaletteColor1
Invoke-ElementAction -Element $button -Action Toggle
Assert-Selected 1
$before=$button.Current.Name
Invoke-TestAction CustomColorButton
Set-ElementValue -Element (Find-Control PaletteColorHex) -Value '#123456'
Invoke-TestAction DialogCloseButton
if((Find-Control PaletteColor1).Current.Name -ne $before) {throw '取消でパレットが変更されました。'}
Invoke-TestAction CustomColorButton
Set-ElementValue -Element (Find-Control PaletteColorHex) -Value '#123456'
Invoke-TestAction DialogPrimaryButton
if((Find-Control PaletteColor1).Current.Name -ne '#123456') {throw '同じパレットスロットを編集できません。'}
Assert-Selected 1
$preferences=Get-Content (Join-Path $SettingsDirectory 'screenshot-preferences.json') -Raw | ConvertFrom-Json
if($preferences.Palette[1] -ne '#123456' -or $preferences.Color -ne '#123456') {throw 'パレットと描画色が設定へ保存されません。'}
$picker=Find-Control EyedropperButton
Invoke-ElementAction -Element $picker -Action Toggle
if((Find-Control EyedropperButton).Current.ItemStatus -ne '色を採取中') {throw 'スポイトを開始できません。'}
Invoke-ElementAction -Element $picker -Action Toggle
if((Find-Control EyedropperButton).Current.ItemStatus -ne '') {throw 'スポイトを解除できません。'}
Invoke-TestAction MoreButton; Invoke-TestAction SelectAllMenuItem
if(-not (Find-Control CropButton).Current.IsEnabled -or (Find-Control CropButton).Current.AcceleratorKey -notlike '*Ctrl+Shift+X*') {throw '切抜コマンドのキー情報がありません。'}
Invoke-TestAction CanvasBackgroundButton
Invoke-ElementAction -Element (Find-Control CanvasTransparentBox) -Action Toggle
Invoke-TestAction DialogPrimaryButton
$root=[Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle)
$root.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
$discard=Find-Control DialogSecondaryButton
if($discard.Current.Name -ne '保存せずに閉じる' -or $discard.Current.AcceleratorKey -ne 'Alt+N') {throw '保存せずに閉じるのNキーが不正です。'}
Invoke-TestAction DialogCloseButton
$savePath=Join-Path $OutputDirectory ('palette-' + [guid]::NewGuid().ToString('N') + '.png')
Invoke-TestAction MoreButton; Invoke-TestAction SaveAsMenuItem
Set-ElementValue -Element (Find-Control DialogFolder) -Value $OutputDirectory
Set-ElementValue -Element (Find-Control DialogFileName) -Value ([IO.Path]::GetFileName($savePath))
Invoke-TestAction DialogPrimaryButton
if(-not (Test-Path $savePath)) {throw '検証PNGを保存できません。'}
$root.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
[pscustomobject]@{PaletteSingleSelection=$true;PaletteEditAndCancel=$true;PalettePreferences=$true;EyedropperToggle=$true;CropKeyMetadata=$true;DiscardKeyN=$true;SavedPng=$savePath;PhysicalInputVerified=$false} | ConvertTo-Json
