[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath,[Parameter(Mandatory)][string]$SettingsDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts'))+[IO.Path]::DirectorySeparatorChar
foreach($path in @($AppPath,$SettingsDirectory,$OutputDirectory)){
    if(-not [IO.Path]::IsPathFullyQualified($path) -or -not [IO.Path]::GetFullPath($path).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'artifacts配下の絶対パスで指定してください。'}
}
if(Test-Path -LiteralPath $SettingsDirectory){throw '新しい専用設定先を指定してください。'}
New-Item -ItemType Directory -Path $SettingsDirectory,$OutputDirectory -Force|Out-Null
. "$env:USERPROFILE/.codex/skills/windows-ui-automation/scripts/ui-toolkit.ps1"
$tools=@('Pen','Eraser','Arrow','Line','Rectangle','Ellipse','Callout')
$processes=[Collections.Generic.List[Diagnostics.Process]]::new()
$report=[ordered]@{Status='running';DefaultWidth=3;Tools=$tools;IndependentAfterRestart=$false;Closed=$false;PhysicalInputUsed=$false}
function OpenEditor {
    $process=Start-Process -FilePath $AppPath -ArgumentList @('--settings-dir',('"'+$SettingsDirectory+'"')) -WindowStyle Hidden -PassThru
    $processes.Add($process)
    $processFilter=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
    $deadline=[DateTime]::UtcNow.AddSeconds(12)
    do {
        $windows=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$processFilter)|Where-Object {$_.Current.Name -like '* - PPPaint - v*'})
        if($windows.Count -ne 1){Start-Sleep -Milliseconds 100}
    }while($windows.Count -ne 1 -and [DateTime]::UtcNow -lt $deadline)
    if($windows.Count -ne 1){throw '専用編集窓が見つかりません。'}
    $script:EditorTestHandle=[IntPtr]$windows[0].Current.NativeWindowHandle
    return $process
}
function Width { (Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId WidthBox).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value }
function SetWidth([double]$value) { (Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId WidthBox).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($value) }
function CloseEditor($process) {
    [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    if(-not $process.WaitForExit(5000)){throw '専用編集窓が終了しません。'}
}
try {
    $first=OpenEditor
    . (Join-Path $PSScriptRoot Screenshot-UiHelpers.ps1) -TargetProcessId $first.Id
    foreach($tool in $tools){Select-TestTool $tool;if((Width) -ne 3){throw "既定の太さが3ではありません: $tool"}}
    Select-TestTool Pen;SetWidth 8
    Select-TestTool Eraser;SetWidth 12
    CloseEditor $first
    $second=OpenEditor
    foreach($tool in $tools){Select-TestTool $tool;$expected=if($tool -eq 'Pen'){8}elseif($tool -eq 'Eraser'){12}else{3};if((Width) -ne $expected){throw "ツール別の太さが復元されません: $tool"}}
    $report.IndependentAfterRestart=$true
    CloseEditor $second
    $report.Status='passed'
}catch{$report.Status='failed';$report.Error=$_.Exception.Message}
finally {
    foreach($process in $processes){
        if(-not $process.HasExited){try{$process.Refresh();[Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close();$null=$process.WaitForExit(5000)}catch{}}
    }
    $report.Closed=@($processes|Where-Object {-not $_.HasExited}).Count -eq 0
    $report.ProcessIds=@($processes.Id)
    $report|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $OutputDirectory pen-defaults.json) -Encoding utf8
}
$report|ConvertTo-Json -Depth 4
if($report.Status -ne 'passed' -or -not $report.Closed){throw 'ペン既定値のUIA検証に失敗しました。'}
