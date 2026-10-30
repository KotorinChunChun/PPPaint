[CmdletBinding()]
param([Parameter(Mandatory)][int]$TargetProcessId,[Parameter(Mandatory)][string]$OutputDirectory,[switch]$CloseWindow)

# Run against a disposable settings profile. UI Automation only; no physical input.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $TargetProcessId
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/'))
if (-not [IO.Path]::GetFullPath($OutputDirectory).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)) { throw '出力先はartifacts配下に限定してください。' }
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$root = [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle)
$transform = $root.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern)
if (-not $transform.Current.CanResize) { throw '隔離ウィンドウをリサイズできません。' }
function Get-Control([string]$Id) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$Id)
    return $root.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
}
function Assert-Visible([string]$Id) {
    $el = Get-Control $Id
    if (-not $el -or $el.Current.IsOffscreen -or $el.Current.BoundingRectangle.IsEmpty -or $el.Current.BoundingRectangle.Width -le 0) { throw "表示されていません: $Id" }
    $r = $el.Current.BoundingRectangle; $w = $root.Current.BoundingRectangle
    if ($r.Left -lt $w.Left -or $r.Right -gt $w.Right -or $r.Top -lt $w.Top -or $r.Bottom -gt $w.Bottom) { throw "ウィンドウからはみ出しています: $Id $r / $w" }
    return $el
}
function Assert-Hidden([string]$Id) {
    $el = Get-Control $Id
    if ($el -and -not $el.Current.IsOffscreen -and -not $el.Current.BoundingRectangle.IsEmpty -and $el.Current.BoundingRectangle.Width -gt 0) { throw "不要な設定が表示されています: $Id" }
}
$commands = @('NewButton','OpenButton','SaveButton','CutButton','CopyButton','PasteButton','UndoButton','RedoButton','CropButton','DeleteButton','OrderButton','MoreButton','SettingsButton')
$tools = @('Select','Region','Pen','Eraser','Arrow','Rectangle','Ellipse','Line','Text','Number','Redact','Highlight','Blur','Callout','Mosaic','Watermark')
$fixed = @($commands) + @($tools | ForEach-Object { "Tool$_" }) + @('EyedropperButton','CustomColorButton') + @(0..7 | ForEach-Object { "PaletteColor$_" })
$options = @('WidthBox','BlurRadiusBox','MosaicSizeBox','OpacityBox','FontBox','TextInput','NumberStartBox')
$visibleOptions = @{
    Select=@('WidthBox'); Region=@('WidthBox'); Pen=@('WidthBox'); Eraser=@('WidthBox'); Arrow=@('WidthBox'); Rectangle=@('WidthBox'); Ellipse=@('WidthBox'); Line=@('WidthBox')
    Text=@('FontBox','TextInput'); Number=@('FontBox','NumberStartBox'); Redact=@(); Highlight=@(); Blur=@('BlurRadiusBox'); Callout=@('WidthBox','FontBox','TextInput'); Mosaic=@('MosaicSizeBox'); Watermark=@('OpacityBox','FontBox','TextInput')
}
$states = @()
foreach ($width in @(1280,920)) {
    $transform.Resize($width,900); Start-Sleep -Milliseconds 350
    foreach ($tool in $tools) {
        Select-TestTool $tool
        $controls = @($fixed | ForEach-Object { Assert-Visible $_ })
        foreach ($id in $options) {
            if ($id -in $visibleOptions[$tool]) { $controls += Assert-Visible $id } else { Assert-Hidden $id }
        }
        $viewport = Assert-Visible Viewport; $viewRect = $viewport.Current.BoundingRectangle
        if ($viewRect.Height -lt 350) { throw "キャンバス領域が不足しています: $width / $tool / $viewRect" }
        $geometry = @($controls | ForEach-Object { $current=$_.Current; [pscustomobject]@{Id=$current.AutomationId;Name=$current.Name;Rect=$current.BoundingRectangle} })
        for ($i=0;$i -lt $geometry.Count;$i++) {
            $a = $geometry[$i].Rect
            if ($a.Bottom -gt $viewRect.Top + 1) { throw "ツールバーが画像領域に重なっています: $($geometry[$i].Id)" }
            for ($j=$i+1;$j -lt $geometry.Count;$j++) {
                $b = $geometry[$j].Rect
                if ([Math]::Min($a.Right,$b.Right)-[Math]::Max($a.Left,$b.Left) -gt 1 -and [Math]::Min($a.Bottom,$b.Bottom)-[Math]::Max($a.Top,$b.Top) -gt 1) { throw "コントロールが重なっています: $($geometry[$i].Id) / $($geometry[$j].Id)" }
            }
        }
        $toolRows = @($tools | ForEach-Object { (Get-Control "Tool$_").Current.BoundingRectangle.Y } | Sort-Object -Unique)
        if ($toolRows.Count -lt 2) { throw '描画ツールの改行が確認できません。' }
        foreach ($id in $commands) {
            $el = Get-Control $id
            if ($el.Current.Name -match '[()]' -or -not $el.Current.AcceleratorKey) { throw "上段のキー表示/アクセシビリティ情報に不整合があります: $id" }
        }
        $states += [pscustomobject]@{Width=$width;Tool=$tool;ToolRows=$toolRows.Count;CanvasHeight=$viewRect.Height;VisibleControls=@($geometry | ForEach-Object { [pscustomobject]@{Id=$_.Id;Name=$_.Name;Bounds=$_.Rect.ToString()} })}
    }
}
foreach ($spec in @(@('Select','選択・移動','V','図形・画像を選択して移動'),@('Region','矩形範囲','M','画像上の矩形を指定'))) {
    $el = Get-Control "Tool$($spec[0])"
    if ($el.Current.Name -ne $spec[1] -or $el.Current.AcceleratorKey -ne $spec[2] -or -not $el.Current.HelpText.Contains($spec[3])) { throw "用途名/キー/説明が不一致です: $($spec[0])" }
}
Select-TestTool Pen
$widthBox = Get-Control WidthBox
$widthBox.GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(7)
Select-TestTool Text; Select-TestTool Pen
if ((Get-Control WidthBox).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value -ne 7) { throw '非表示中に太さの値が失われました。' }
$scrolls = @($root.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition) | Where-Object {
    $pattern=$null
    $_.TryGetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern,[ref]$pattern) -and $_.Current.BoundingRectangle.Bottom -le (Get-Control Viewport).Current.BoundingRectangle.Top -and $pattern.Current.HorizontallyScrollable
})
if ($scrolls.Count) { throw 'ツールバーに横スクロールが残っています。' }
Select-TestTool Select
$result = [pscustomobject]@{ProcessId=$TargetProcessId;Title=$root.Current.Name;States=$states;Widths=@(1280,920);ToolsChecked=$tools.Count;TopCommandKeyCaptionsHidden=$true;OptionValueRetained=$true;NoToolbarHorizontalScroll=$true;PhysicalInputVerified=$false;ForegroundHandle=[WUA.Win]::GetForegroundWindow().ToInt64()}
$result | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $OutputDirectory 'toolbar-validation.json') -Encoding utf8
if ($CloseWindow) { $root.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close() }
[pscustomobject]@{Widths=$result.Widths;States=$states.Count;ToolsChecked=$tools.Count;MinimumCanvasHeight=($states.CanvasHeight | Measure-Object -Minimum).Minimum;Output=(Join-Path $OutputDirectory 'toolbar-validation.json')} | ConvertTo-Json
