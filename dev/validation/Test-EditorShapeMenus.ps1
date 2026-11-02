[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath,[Parameter(Mandatory)][string]$SettingsDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $SettingsDirectory){throw '新しい専用設定先を指定してください。'}
New-Item -ItemType Directory -Path $SettingsDirectory,$OutputDirectory -Force|Out-Null
. "$env:USERPROFILE/.codex/skills/windows-ui-automation/scripts/ui-toolkit.ps1"
$process=$null;$script:shapeHandle=[IntPtr]::Zero
$report=[ordered]@{Status='running';PhysicalInputUsed=$false;Checks=[Collections.Generic.List[string]]::new();Closed=$false}
function Root {[Windows.Automation.AutomationElement]::FromHandle($script:shapeHandle)}
function Element([string]$id){Find-ByAutomationId -WindowHandle $script:shapeHandle -AutomationId $id}
function Press([string]$id){
    $element=Element $id;$pattern=$null
    if($element.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)){$pattern.Invoke()}
    else{Invoke-ElementAction -Element $element -Action Toggle}
    Start-Sleep -Milliseconds 150
}
function WaitUntil([scriptblock]$probe,[string]$label){$deadline=[DateTime]::UtcNow.AddSeconds(12);while(-not (&$probe)){if([DateTime]::UtcNow -gt $deadline){throw "待機期限: $label"};Start-Sleep -Milliseconds 100};$report.Checks.Add($label)}
function Preferences { $path=Join-Path $SettingsDirectory screenshot-preferences.json;if(Test-Path -LiteralPath $path){Get-Content -LiteralPath $path -Raw|ConvertFrom-Json} }
try{
    $process=Start-Process -FilePath $AppPath -ArgumentList @('--settings-dir',('"'+$SettingsDirectory+'"')) -WindowStyle Hidden -PassThru
    $filter=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
    WaitUntil {$windows=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$filter)|Where-Object {$_.Current.Name -like '* - PPPaint - v*'});if($windows.Count -eq 1){$script:shapeHandle=[IntPtr]$windows[0].Current.NativeWindowHandle};$script:shapeHandle -ne [IntPtr]::Zero} 'window-ready'
    $redact=(Root).FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'ToolRedact'))
    if($redact){throw '情報隠しの単独ボタンが残っています。'}
    Press ToolRectangleStyles;Press ShapeRectangleFill
    WaitUntil {(Preferences).Styles.Rectangle.Filled -eq $true} 'rectangle-fill'
    Press ToolRectangleStyles;Press ShapeRectangleOutline
    WaitUntil {(Preferences).Styles.Rectangle.Filled -eq $false} 'rectangle-outline'
    Press ToolEllipseStyles;Press ShapeEllipseFill
    WaitUntil {(Preferences).Styles.Ellipse.Filled -eq $true} 'ellipse-fill'
    foreach($style in @('Line','DoubleEnded','Block','ThreeDimensional')){
        Press ToolArrowStyles;Press ('ShapeArrow'+$style)
        WaitUntil {(Preferences).Styles.Arrow.Arrow -eq $style} ('arrow-'+$style)
    }
    Press ToolArrowStyles;Press ShapeArrowFill
    WaitUntil {(Preferences).Styles.Arrow.Filled -eq $true} 'arrow-fill'
    foreach($style in @('Circle','Square','Plain')){
        Press ToolNumberStyles;Press ('ShapeNumber'+$style)
        WaitUntil {(Preferences).Styles.Number.Number -eq $style} ('number-'+$style)
    }
    Press ToolNumberStyles
    if((Element ShapeNumberFill).Current.IsEnabled){throw '囲みなしで塗りつぶしが有効です。'}
    Press ShapeNumberCircle
    Invoke-ElementAction -Element (Element ToolCallout) -Action Toggle
    $text=Element TextInput
    if($text.Current.IsOffscreen -or $text.Current.BoundingRectangle.IsEmpty){throw '吹き出しの上部文字欄が見えません。'}
    $report.Checks.Add('callout-text-visible')
    $report.Status='passed'
}catch{$report.Error=$_.Exception.Message;$report.Stack=$_.ScriptStackTrace}
finally{
    if($process -and -not $process.HasExited -and $script:shapeHandle -ne [IntPtr]::Zero){try{(Root).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close();$report.Closed=$process.WaitForExit(5000)}catch{}}
    $report.ProcessId=if($process){$process.Id}else{$null}
    $report|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $OutputDirectory shape-menus.json) -Encoding utf8
}
$report|ConvertTo-Json -Depth 5
if($report.Status -ne 'passed' -or -not $report.Closed){throw '図形メニューのUIA検証に失敗しました。'}
