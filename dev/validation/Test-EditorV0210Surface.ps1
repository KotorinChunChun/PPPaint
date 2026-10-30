[CmdletBinding()]
param([Parameter(Mandatory)][int]$TargetProcessId,[Parameter(Mandatory)][string]$SourcePath,[Parameter(Mandatory)][string]$OutputDirectory,[switch]$KeepOpen)

# Synthetic input and isolated disposable editor. Geometry/UIA only, no physical pointer input.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $TargetProcessId
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/'))
foreach($path in @($SourcePath,$OutputDirectory)) {if(-not [IO.Path]::GetFullPath($path).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'artifacts配下に限定してください。'}}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
function Find-Control([string]$Id) {Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id}
$checks=[Collections.Generic.List[object]]::new()
$dpiScale=$null
foreach($zoom in @(25,50,100,200)) {
    (Find-Control ZoomSlider).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($zoom)
    Start-Sleep -Milliseconds 150
    $view=Find-Control Viewport
    $scroll=$view.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
    $positions=[Collections.Generic.List[double[]]]::new(); $positions.Add([double[]]@(0,0)); if($scroll.Current.HorizontallyScrollable -or $scroll.Current.VerticallyScrollable){$positions.Add([double[]]@(100,0));$positions.Add([double[]]@(0,100));$positions.Add([double[]]@(100,100))}
    $covered=[Collections.Generic.HashSet[int]]::new()
    foreach($position in $positions){
        $hx=if($scroll.Current.HorizontallyScrollable){$position[0]}else{-1}
        $vy=if($scroll.Current.VerticallyScrollable){$position[1]}else{-1}
        $scroll.SetScrollPercent($hx,$vy); Start-Sleep -Milliseconds 100
        $image=(Find-Control PaintCanvas).Current.BoundingRectangle
        $surface=(Find-Control InteractionCanvas).Current.BoundingRectangle
        if($null -eq $dpiScale){$dpiScale=$image.Width/(900*$zoom/100)}
        $margins=@(($image.Left-$surface.Left),($image.Top-$surface.Top),($surface.Right-$image.Right),($surface.Bottom-$image.Bottom))
        foreach($margin in $margins){
            if($margin -lt -1.5 -or $margin -gt 10*$dpiScale+1.5){throw "画像と操作面の余白がずれています: $zoom/$margin"}
        }
        $visible=@($margins | ForEach-Object {[Math]::Abs($_-10*$dpiScale) -le 1.5})
        $corners=@(@($image.Left,$image.Top,-1,-1,0,1),@($image.Right,$image.Top,1,-1,2,1),@($image.Right,$image.Bottom,1,1,2,3),@($image.Left,$image.Bottom,-1,1,0,3))
        for($i=0;$i -lt 4;$i++){
            $corner=$corners[$i]
            if(-not($visible[$corner[4]] -and $visible[$corner[5]])){continue}
            $x=$corner[0]+4.5*$dpiScale*$corner[2];$y=$corner[1]+4.5*$dpiScale*$corner[3]
            if($x -lt $surface.Left -or $x -gt $surface.Right -or $y -lt $surface.Top -or $y -gt $surface.Bottom){throw '外側のハンドルが操作面からはみ出ています。'}
            $null=$covered.Add($i)
        }
        if(-not (Find-Control InteractionCanvas).Current.IsKeyboardFocusable){throw '操作面へフォーカスできません。'}
        $checks.Add([pscustomobject]@{Zoom=$zoom;Scroll=@($hx,$vy);Image=$image.ToString();Surface=$surface.ToString();Margins=$margins})
    }
    if($covered.Count -ne 4){throw "四隅の外側ハンドルを表示して確認できません: $zoom/$($covered.Count)"}
}
(Find-Control ZoomSlider).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(100)
Invoke-TestAction MoreButton; Invoke-TestAction SelectAllMenuItem
if(-not (Find-Control CropButton).Current.IsEnabled){throw 'キャンバス全体の範囲選択ができません。'}
Invoke-TestAction MoreButton; Invoke-TestAction SaveAsMenuItem
$saved=Join-Path $OutputDirectory ('surface-'+[guid]::NewGuid().ToString('N')+'.png')
Set-ElementValue -Element (Find-Control DialogFolder) -Value $OutputDirectory
Set-ElementValue -Element (Find-Control DialogFileName) -Value ([IO.Path]::GetFileName($saved))
Invoke-TestAction DialogPrimaryButton
if(-not(Test-Path $saved)){throw 'PNG保存に失敗しました。'}
$source=[Drawing.Bitmap]::new($SourcePath);$bitmap=[Drawing.Bitmap]::new($saved)
try {
    if($bitmap.Width -ne $source.Width -or $bitmap.Height -ne $source.Height){throw '元の画像寸法が変わりました。'}
    foreach($x in @(0,9,100,300,899)){foreach($y in @(0,20,100,250,549)){
        if($bitmap.GetPixel($x,$y).ToArgb() -ne $source.GetPixel($x,$y).ToArgb()){throw '保存画像へ選択枠が混入しました。'}
    }}
} finally {$source.Dispose();$bitmap.Dispose()}
if(-not $KeepOpen){[Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()}
[pscustomobject]@{Geometry=$checks;FullCanvasRegion=$true;PngHasNoSelectionOverlay=$true;SavedPng=$saved;PhysicalInputVerified=$false} | ConvertTo-Json -Depth 4
