[CmdletBinding()]
param([Parameter(Mandatory)][int]$TargetProcessId,[Parameter(Mandatory)][string]$SourcePath,[Parameter(Mandatory)][string]$OutputDirectory)

# Synthetic one-pixel PNG, disposable profile, UI Automation only.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $TargetProcessId
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/'))
foreach($path in @($SourcePath,$OutputDirectory)){if(-not [IO.Path]::GetFullPath($path).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'artifacts配下に限定してください。'}}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
function Find-Control([string]$Id) {Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id}
function Set-Number([string]$Id,[double]$Value) {(Find-Control $Id).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($Value);Start-Sleep -Milliseconds 100}
function Save-Png([string]$Prefix){
    $saved=Join-Path $OutputDirectory ($Prefix+'-'+[guid]::NewGuid().ToString('N')+'.png')
    Invoke-TestAction MoreButton;Invoke-TestAction SaveAsMenuItem
    Set-ElementValue -Element (Find-Control DialogFolder) -Value $OutputDirectory
    Set-ElementValue -Element (Find-Control DialogFileName) -Value ([IO.Path]::GetFileName($saved))
    Invoke-TestAction DialogPrimaryButton
    $deadline=[DateTime]::UtcNow.AddSeconds(8)
    while(-not(Test-Path $saved) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 100}
    if(-not(Test-Path $saved)){throw 'PNG保存に失敗しました。'}
    return $saved
}
function Assert-Pixels([string]$Path){
    $source=[Drawing.Bitmap]::new($SourcePath);$result=[Drawing.Bitmap]::new($Path)
    try{
        if($source.Width -ne 32 -or $source.Height -ne 32 -or $source.Size -ne $result.Size){throw '寸法が一致しません。'}
        $ink=0
        for($y=0;$y -lt 32;$y++){for($x=0;$x -lt 32;$x++){
            $pixel=$source.GetPixel($x,$y)
            if($pixel.ToArgb() -ne $result.GetPixel($x,$y).ToArgb()){throw "RGBAが変化しました: $x,$y"}
            if($pixel.ToArgb() -ne [Drawing.Color]::White.ToArgb()){$ink++}
        }}
        if($ink -ne 1 -or $source.GetPixel(8,9).ToArgb() -ne [Drawing.Color]::Red.ToArgb()){throw '1画素の検証用入力ではありません。'}
    }finally{$source.Dispose();$result.Dispose()}
}
$initialHandle=$script:EditorTestHandle
Select-TestTool Pen;Set-Number WidthBox 1
foreach($zoom in @(50,100,200,400)){
    Set-Number ZoomSlider $zoom
    $canvas=Find-Control PaintCanvas
    if($canvas.Current.IsOffscreen -or $canvas.Current.BoundingRectangle.Width -le 0){throw '画像が表示されていません。'}
    if((Find-Control DimensionsButton).Current.Name -notlike '32 × 32 px*'){throw '倍率変更で画像の寸法が変わりました。'}
}
$first=Save-Png 'one-pixel-saved';Assert-Pixels $first
Invoke-TestAction OpenButton
Set-ElementValue -Element (Find-Control DialogFolder) -Value $OutputDirectory
Set-ElementValue -Element (Find-Control DialogFileName) -Value ([IO.Path]::GetFileName($first))
Invoke-TestAction DialogPrimaryButton
$condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$TargetProcessId)
$deadline=[DateTime]::UtcNow.AddSeconds(8);$reopened=@()
do {
    $reopened=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$condition) | Where-Object {
        $_.Current.NativeWindowHandle -ne $initialHandle.ToInt64() -and $_.Current.Name -like "*$(Split-Path $first -Leaf)* - PPPaint - v0.2.14"
    })
    if($reopened.Count -eq 0){Start-Sleep -Milliseconds 100}
}while($reopened.Count -eq 0 -and [DateTime]::UtcNow -lt $deadline)
if($reopened.Count -ne 1){throw '再読込した編集ウィンドウを特定できません。'}
$script:EditorTestHandle=[IntPtr]$reopened[0].Current.NativeWindowHandle
Select-TestTool Pen;Set-Number WidthBox 1;Set-Number ZoomSlider 100
$second=Save-Png 'one-pixel-reopened';Assert-Pixels $second
$evidence=[pscustomobject]@{ProcessId=$TargetProcessId;Source=$SourcePath;FirstSaved=$first;ReopenedSaved=$second;ExactOnePixelAndAllRgba=$true;Zooms=@(50,100,200,400);ImageDimensionsUnchanged=$true;PenWidth=1;PhysicalPenGestureVerified=$false;ForegroundHandle=[WUA.Win]::GetForegroundWindow().ToInt64()}
$evidence | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'pixel-validation.json')
foreach($handle in @($script:EditorTestHandle,$initialHandle)){[Windows.Automation.AutomationElement]::FromHandle($handle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()}
$evidence | ConvertTo-Json
