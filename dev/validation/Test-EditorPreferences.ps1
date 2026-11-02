[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath,[Parameter(Mandatory)][string]$SettingsDirectory,[Parameter(Mandatory)][string]$OutputDirectory)

$ErrorActionPreference='Stop'
$fileVersion = [version](Get-Item -LiteralPath $AppPath).VersionInfo.FileVersion
$expectedVersion = '{0}.{1}.{2}' -f $fileVersion.Major, $fileVersion.Minor, $fileVersion.Build

$artifactRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/'))
foreach($path in @($AppPath,$SettingsDirectory,$OutputDirectory)){if(-not [IO.Path]::GetFullPath($path).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw '配布版/専用設定/出力はartifacts配下に限定してください。'}}
New-Item -ItemType Directory -Force $SettingsDirectory,$OutputDirectory | Out-Null
$settingsFile=Join-Path $SettingsDirectory 'screenshot-preferences.json'
if(-not(Test-Path $settingsFile)){'{"Width":7,"FontSize":22,"BlurRadius":9,"MosaicSize":14,"Opacity":35}'|Set-Content $settingsFile}
. "$env:USERPROFILE/.codex/skills/windows-ui-automation/scripts/ui-toolkit.ps1"
function Open-TestEditor {
    $p=Start-Process -FilePath $AppPath -ArgumentList @('--settings-dir',"`"$SettingsDirectory`"") -PassThru
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$p.Id)
    $deadline=[DateTime]::UtcNow.AddSeconds(12)
    do {
        $windows=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$condition)|Where-Object{$_.Current.Name -like ('* - PPPaint - v' + $expectedVersion)})
        if($windows.Count -ne 1){Start-Sleep -Milliseconds 150}
    }while($windows.Count -ne 1 -and [DateTime]::UtcNow -lt $deadline)
    if($windows.Count -ne 1){throw '専用編集ウィンドウを起動できません。'}
    $script:EditorTestHandle=[IntPtr]$windows[0].Current.NativeWindowHandle
    return $p
}
function Get-Number([string]$Id){(Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value}
function Set-Number([string]$Id,[double]$Value){(Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($Value);Start-Sleep -Milliseconds 100}
function Close-TestEditor($Process){
    [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    if(-not $Process.WaitForExit(5000)){throw '設定だけの編集ウィンドウが終了しません。'}
}
$specs=@(@('Pen','WidthBox',1),@('Eraser','WidthBox',16),@('Arrow','WidthBox',5),@('Text','FontBox',40),@('Callout','FontBox',18),@('Number','FontBox',32),@('Blur','BlurRadiusBox',19),@('Mosaic','MosaicSizeBox',22),@('Text','OpacityBox',65))
$first=Open-TestEditor
. (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $first.Id
$removedTool=[Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'ToolWatermark'))
if($removedTool){throw '削除した透かしボタンが残っています。'}
Select-TestTool Text
if((Get-Number OpacityBox) -ne 100){throw '旧設定移行後の文字の不透明度が100%ではありません。'}
Select-TestTool Line
if((Get-Number WidthBox) -ne 7){throw '旧共有設定からの移行ができません。'}
Select-TestTool Eraser;Set-Number WidthBox 1.5
if((Get-Number WidthBox) -ne 2){throw '消しゴムの表示幅が実際の整数幅と一致しません。'}
foreach($spec in $specs){Select-TestTool $spec[0];Set-Number $spec[1] $spec[2]}
foreach($spec in $specs){Select-TestTool $spec[0];if((Get-Number $spec[1]) -ne $spec[2]){throw "切替で数値が変わりました: $($spec[0])"}}
Select-TestTool Number;Set-Number NumberStartBox 7
Close-TestEditor $first
$saved=Get-Content $settingsFile -Raw|ConvertFrom-Json
if($saved.Version -ne 3 -or $saved.Tools.Pen.Width -ne 1 -or $saved.Tools.Eraser.Width -ne 16 -or ($saved.PSObject.Properties.Name -contains 'NextNumber')){throw '保存JSONが不正です。'}
$second=Open-TestEditor
foreach($spec in $specs){Select-TestTool $spec[0];if((Get-Number $spec[1]) -ne $spec[2]){throw "再起動で数値が変わりました: $($spec[0])"}}
Select-TestTool Number
if((Get-Number NumberStartBox) -ne 1){throw '番号が新しい画像で1へ戻りません。'}
Close-TestEditor $second
[pscustomobject]@{Version=$expectedVersion;LegacyMigration=$true;IndependentTools=@($specs|ForEach-Object{$_[0]}|Select-Object -Unique).Count;NumericSettings=$specs.Count;WatermarkButtonRemoved=$true;MigratedTextOpacity=100;RestoredAfterProcessRestart=$true;EraserIntegerWidth=$true;NumberRestartsAtOne=$true;SettingsFile=$settingsFile;Processes=@($first.Id,$second.Id);PhysicalInputUsed=$false}|ConvertTo-Json|Tee-Object -FilePath (Join-Path $OutputDirectory 'preferences-validation.json')
