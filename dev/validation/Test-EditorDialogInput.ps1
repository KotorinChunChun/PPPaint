[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath,[Parameter(Mandatory)][string]$SettingsDirectory,[Parameter(Mandatory)][string]$OutputDirectory,[switch]$AltOnly,[switch]$CaptureWindows)
# Announce exclusive PC input before running. Only disposable documents are created.
$ErrorActionPreference='Stop'
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/'))
foreach($path in @($SettingsDirectory,$OutputDirectory)) {
    if(-not [IO.Path]::GetFullPath($path).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw '設定/出力はartifacts配下に限定してください。'}
}
New-Item -ItemType Directory -Path $SettingsDirectory,$OutputDirectory -Force | Out-Null
. "$env:USERPROFILE/.codex/skills/windows-ui-automation/scripts/ui-toolkit.ps1"
$sent=0; $checks=[Collections.Generic.List[string]]::new(); $processIds=[Collections.Generic.List[int]]::new()
function Control([string]$Id,[switch]$Optional) { Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id -AllowMissing:$Optional }
function Invoke-TestAction([string]$Id) { Invoke-ElementAction -Element (Control $Id) -Action Invoke; Start-Sleep -Milliseconds 150 }
function Open-Editor {
    $script:TestProcess=Start-Process -FilePath $AppPath -ArgumentList @('--settings-dir',"`"$SettingsDirectory`"") -PassThru
    $processIds.Add($script:TestProcess.Id)
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$script:TestProcess.Id)
    $deadline=[DateTime]::UtcNow.AddSeconds(12)
    do {
        $windows=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$condition)|Where-Object{$_.Current.Name -like '* - PPPaint - v*'})
        if($windows.Count -ne 1){Start-Sleep -Milliseconds 100}
    }while($windows.Count -ne 1 -and [DateTime]::UtcNow -lt $deadline)
    if($windows.Count -ne 1){throw '専用ウィンドウを確認できません。'}
    $script:EditorTestHandle=[IntPtr]$windows[0].Current.NativeWindowHandle
}
function Open-UnsavedPrompt {
    Invoke-TestAction DimensionsButton
    (Control DialogImageWidth).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(1010)
    Invoke-TestAction DialogPrimaryButton
    Show-UnsavedPrompt
}
function Show-UnsavedPrompt {
    [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    $null=Control DialogSecondaryButton
}
function Send-CheckedKey([string]$Key) {
    if(-not(Set-ForegroundForced -WindowHandle $script:EditorTestHandle)){throw 'INPUT_UNAVAILABLE: 対象を前面化できません。キーは送信しません。'}
    Assert-ForegroundIs -WindowHandle $script:EditorTestHandle
    Send-TextInput -WindowHandle $script:EditorTestHandle -Text $Key -Raw
    $script:sent++; Start-Sleep -Milliseconds 250
}
function Assert-PromptClosed {
    if(Control DialogSecondaryButton -Optional){throw '確認画面が閉じません。'}
    if($script:TestProcess.HasExited){throw '取消で編集ウィンドウが終了しました。'}
}
$availability='verified'; $failure=$null
try {
    Open-Editor; Open-UnsavedPrompt
    if($CaptureWindows -and -not(Save-WindowScreenshot -WindowHandle $script:EditorTestHandle -Path (Join-Path $OutputDirectory unsaved-confirmation.png))){throw '確認画面を前面撮影できません。'}
    (Control DialogCloseButton).SetFocus(); Send-CheckedKey $(if($AltOnly){'%n'}else{'n'})
    if(-not $script:TestProcess.WaitForExit(5000)){throw '指定したNショートカットで終了しません。'}
    $checks.Add($(if($AltOnly){'Alt+N closes disposable unsaved document'}else{'N closes disposable unsaved document'}))
    if(-not $AltOnly) {
    Open-Editor; Open-UnsavedPrompt
    (Control DialogCloseButton).SetFocus(); Send-CheckedKey '%n'
    if(-not $script:TestProcess.WaitForExit(5000)){throw 'Alt+Nで終了しません。'}
    $checks.Add('Alt+N closes disposable unsaved document')
    Open-Editor; Open-UnsavedPrompt
    (Control DialogCloseButton).SetFocus(); Send-CheckedKey '{ENTER}'; Assert-PromptClosed
    $checks.Add('Enter cancels without discarding')
    Show-UnsavedPrompt; (Control DialogCloseButton).SetFocus(); Send-CheckedKey '^n'
    $null=Control DialogSecondaryButton; $checks.Add('Ctrl+N does not invoke dialog N')
    Send-CheckedKey '{ESC}'; Assert-PromptClosed; $checks.Add('Esc cancels without discarding')
    Show-UnsavedPrompt; (Control DialogCloseButton).SetFocus(); Send-CheckedKey 'n'
    if(-not $script:TestProcess.WaitForExit(5000)){throw '取消後に単独Nで終了しません。'}
    Open-Editor; Invoke-TestAction FileButton; Invoke-TestAction OpenMenuItem
    $name=Control DialogFileName; Set-ElementValue -Element $name -Value ''; $name.SetFocus(); Send-CheckedKey 'n'
    if($name.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne 'n'){throw 'ファイル名のN入力が奪われました。'}
    $checks.Add('N remains filename input')
    Send-CheckedKey '%l'
    if([Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId -ne 'DialogFolder'){throw 'Alt+Lでフォルダー欄へ移動しません。'}
    Send-CheckedKey '%n'
    if([Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId -ne 'DialogFileName'){throw 'Alt+Nでファイル名欄へ移動しません。'}
    $checks.Add('Alt+L and Alt+N focus file fields')
    $folder=Control DialogFolder; Set-ElementValue -Element $folder -Value (Join-Path $OutputDirectory missing-folder); $folder.SetFocus(); Send-CheckedKey '{ENTER}'
    if([string]::IsNullOrWhiteSpace((Control DialogValidationError).Current.Name)){throw '存在しないフォルダーのエラーがありません。'}
    $focused=[Windows.Automation.AutomationElement]::FocusedElement
    if($focused.Current.AutomationId -ne 'DialogFolder'){throw '訂正対象のフォルダー欄からフォーカスが外れました。'}
    $checks.Add('Invalid folder retains focus'); Send-CheckedKey '%c'
    if(Control DialogCloseButton -Optional){throw 'Alt+Cでキャンセルしません。'}
    $checks.Add('Alt+C cancels input dialog')
    Send-CheckedKey '%f'; $null=Control NewMenuItem
    if($CaptureWindows -and -not(Save-WindowScreenshot -WindowHandle $script:EditorTestHandle -Path (Join-Path $OutputDirectory file-menu.png))){throw 'ファイルメニューを前面撮影できません。'}
    $checks.Add('Toolbar Alt+F restored after dialog')
    Invoke-TestAction OpenMenuItem; Invoke-TestAction DialogCloseButton
    Invoke-TestAction DimensionsButton
    (Control DialogImageWidth).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(1010)
    (Control DialogImageWidth).SetFocus(); Send-CheckedKey '%s'
    if(Control DialogCloseButton -Optional){throw 'Alt+Sで適用しません。'}
    if((Control DimensionsButton).Current.Name -notlike '1010 × *'){throw 'Alt+Sで寸法が適用されません。'}
    $checks.Add('Alt+S applies numeric dialog')
    }
} catch {
    $failure=$_.Exception.Message
    if($failure -like '*INPUT_UNAVAILABLE*' -and $sent -eq 0){$availability='unavailable'} else {$availability='failed'}
} finally {
    if($script:TestProcess -and -not $script:TestProcess.HasExited) {
        $discard=Control DialogSecondaryButton -Optional
        if($discard){Invoke-ElementAction -Element $discard -Action Invoke}
        else {
            $cancel=Control DialogCloseButton -Optional
            if($cancel){Invoke-ElementAction -Element $cancel -Action Invoke;Start-Sleep -Milliseconds 150}
            $undo=Control UndoButton -Optional
            if($undo -and $undo.Current.IsEnabled){Invoke-ElementAction -Element $undo -Action Invoke;Start-Sleep -Milliseconds 150}
            [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
        }
        if(-not $script:TestProcess.WaitForExit(5000)){throw '専用テスト窓が終了していません。'}
    }
    [pscustomobject]@{Availability=$availability;PhysicalKeysSent=$sent;Checks=$checks.ToArray();Error=$failure;Processes=$processIds.ToArray()} |
        ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $OutputDirectory dialog-input.json)
}
if($availability -eq 'failed'){throw $failure}
