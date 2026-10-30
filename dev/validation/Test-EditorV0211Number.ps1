[CmdletBinding()]
param([Parameter(Mandatory)][int]$TargetProcessId)

# Isolated blank editor; UIA only. Placement itself is tested in the Core model.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $TargetProcessId
Select-TestTool Number
$tool=Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId ToolNumber
if($tool.Current.HelpText -notlike '*クリック位置を中心*'){throw '中心基準のツール情報がありません。'}
$number=Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId NumberStartBox
$number.GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(7)
Invoke-TestAction UndoButton
if($number.GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value -ne 1){throw '開始番号のUndoに失敗しました。'}
Invoke-TestAction RedoButton
if($number.GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value -ne 7){throw '開始番号のRedoに失敗しました。'}
[Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
[pscustomobject]@{NumberToolCenterMetadata=$true;StartNumberAndUndoRedo=$true;PhysicalPlacementVerified=$false} | ConvertTo-Json
