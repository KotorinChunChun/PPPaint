[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$SettingsDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$CaptureWindows
)

# Disposable profile and synthetic image only. Announce exclusive PC use before captures.
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'pwsh -Sta -Fileで実行してください。' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
foreach ($path in @($AppPath,$SettingsDirectory,$OutputDirectory)) {
    if (-not [IO.Path]::IsPathFullyQualified($path)) { throw 'すべて絶対パスで指定してください。' }
}
$AppPath = [IO.Path]::GetFullPath($AppPath)
$SettingsDirectory = [IO.Path]::GetFullPath($SettingsDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $AppPath.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $AppPath -PathType Leaf)) { throw 'リポジトリ配下のアプリ実行ファイルを指定してください。' }
foreach ($path in @($SettingsDirectory,$OutputDirectory)) {
    if (-not $path.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)) { throw '設定と出力はartifacts配下に限定してください。' }
    # A reparse point anywhere below artifacts would escape the isolated test area.
    $current = $path
    while ($current.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'リンク先の設定/出力フォルダーは使いません。' }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
}
if ((Test-Path -LiteralPath $SettingsDirectory) -and @(Get-ChildItem -LiteralPath $SettingsDirectory -Force).Count) { throw '空または未作成の専用設定フォルダーを指定してください。' }
New-Item -ItemType Directory -Path $SettingsDirectory,$OutputDirectory -Force | Out-Null
. "$env:USERPROFILE/.codex/skills/windows-ui-automation/scripts/ui-toolkit.ps1"
Add-Type -AssemblyName System.Drawing.Common
$runId = [guid]::NewGuid().ToString('N')
$resultPath = Join-Path $OutputDirectory ("canvas-resize-uia-$runId.json")
$fixturePath = Join-Path $OutputDirectory ("canvas-resize-fixture-$runId.png")
$fixture = [Drawing.Bitmap]::new(501,333,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
try {
    $graphics = [Drawing.Graphics]::FromImage($fixture)
    try {
        $graphics.Clear([Drawing.Color]::White)
        $graphics.FillRectangle([Drawing.Brushes]::LightGoldenrodYellow,1,1,499,331)
        $graphics.FillRectangle([Drawing.Brushes]::Red,0,0,501,1)
        $graphics.FillRectangle([Drawing.Brushes]::Blue,0,332,501,1)
        $graphics.FillRectangle([Drawing.Brushes]::Green,0,1,1,331)
        $graphics.FillRectangle([Drawing.Brushes]::Purple,500,1,1,331)
        foreach ($x in @(1,2,249,250,251,498,499)) { $graphics.FillRectangle([Drawing.Brushes]::Black,$x,1,1,4); $graphics.FillRectangle([Drawing.Brushes]::Black,$x,328,1,4) }
        foreach ($y in @(1,2,165,166,167,330,331)) { $graphics.FillRectangle([Drawing.Brushes]::Black,1,$y,4,1); $graphics.FillRectangle([Drawing.Brushes]::Black,496,$y,4,1) }
        $graphics.FillRectangle([Drawing.Brushes]::OrangeRed,240,156,21,21)
    } finally { $graphics.Dispose() }
    $fixture.Save($fixturePath,[Drawing.Imaging.ImageFormat]::Png)
} finally { $fixture.Dispose() }

$viewIds = @('CanvasResizeThumbnail') + @('TopLeft','Top','TopRight','Left','Right','BottomLeft','Bottom','BottomRight' | ForEach-Object { 'CanvasResizeDetail' + $_ })
$report = [ordered]@{
    AppPath=$AppPath; SettingsDirectory=$SettingsDirectory; Fixture=$fixturePath; ProcessId=$null; Status='running'
    Layouts=[Collections.Generic.List[object]]::new(); Updates=[Collections.Generic.List[object]]::new(); Flows=[Collections.Generic.List[object]]::new(); TextInputs=[Collections.Generic.List[object]]::new(); Diagnostics=[Collections.Generic.List[object]]::new()
    Limitations=[Collections.Generic.List[string]]::new(); ScreenshotsTaken=$false; PhysicalInputUsed=$false; PixelRenderingVerified=$false; Cleanup=$null
}
$report.Limitations.Add('UIAで確認するのはビュー配置、Draw後の状態、元画像の表示状態、取消/適用/Undo。画素色・最近傍補間・1pxの目視確認は別の描画試験と対象ウィンドウ画像の確認が必要。')
$process = $null; $script:EditorTestHandle = [IntPtr]::Zero; $ownUndoCount = 0; $failure = $null
function Window-Root {
    $root = [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle)
    if ($root.Current.ProcessId -ne $process.Id) { throw '検証対象PIDが変わりました。' }
    return $root
}
function Find-Control([string]$Id,[switch]$Optional) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$Id)
    $element = (Window-Root).FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
    if ($element -and ($element.Current.IsOffscreen -or $element.Current.BoundingRectangle.IsEmpty)) { $element=$null }
    if (-not $element -and -not $Optional) { throw "表示要素がありません: $Id" }
    return $element
}
function Wait-Dialog([bool]$Visible=$true) {
    $deadline=[DateTime]::UtcNow.AddSeconds(12)
    do {
        $dialog=Find-Control EditorDialog -Optional
        if (($null -ne $dialog) -eq $Visible) { return $dialog }
        Start-Sleep -Milliseconds 80
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "ダイアログの表示状態が切り替わりません: $Visible"
}
function Invoke-Id([string]$Id) { Invoke-ElementAction -Element (Find-Control $Id) -Action Invoke; Start-Sleep -Milliseconds 120 }
function Set-Number([string]$Id,[double]$Value) { (Find-Control $Id).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($Value); Start-Sleep -Milliseconds 120 }
function Read-OwnFocus {
    $focused=[Windows.Automation.AutomationElement]::FocusedElement
    if (-not $focused -or $focused.Current.ProcessId -ne $process.Id) { return $null }
    return [pscustomobject]@{Id=$focused.Current.AutomationId;RuntimeId=($focused.GetRuntimeId() -join ',')}
}
function Set-InputText([string]$Id,[string]$Value) {
    # Focus the intended editor through UIA first; SetValue can itself move focus.
    # No Tab/Enter is sent, so preview changes are observed before a commit/blur.
    $input=Find-Control $Id
    $targetRuntimeId=$input.GetRuntimeId() -join ','
    $input.SetFocus()
    Start-Sleep -Milliseconds 80
    $before=Read-OwnFocus
    (Find-Control $Id).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
    Start-Sleep -Milliseconds 120
    $actual=(Find-Control $Id).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($actual -cne $Value) { throw "入力途中の値が書き換えられました: $Id / '$Value' -> '$actual'" }
    $after=Read-OwnFocus
    $focusVerified=$null
    if ($before -and $before.RuntimeId -eq $targetRuntimeId -and $after) {
        $focusVerified=$before.RuntimeId -eq $after.RuntimeId
        if (-not $focusVerified) { throw "内部TextBoxのSetValueだけでフォーカスが変わりました: $Id" }
    } elseif (-not $report.Limitations.Contains('対象入力欄の実フォーカスがUIAで取得できない場合、SetValue後のフォーカス維持は未確認。入力前にUIA SetFocusを要求するが、Tab/Enterは送っていない。')) {
        $report.Limitations.Add('対象入力欄の実フォーカスがUIAで取得できない場合、SetValue後のフォーカス維持は未確認。入力前にUIA SetFocusを要求するが、Tab/Enterは送っていない。')
    }
    $report.TextInputs.Add([pscustomobject]@{Id=$Id;Text=$Value;Retained=$true;OwnFocusUnchanged=$focusVerified;InputFocusRequestedBeforeEdit=$true;FocusMoveAfterEditRequested=$false;TabOrEnterSent=$false})
}
function Set-Check([string]$Id,[bool]$On) {
    $toggle=(Find-Control $Id).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    if (($toggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::On) -ne $On) { Invoke-ElementAction -Element (Find-Control $Id) -Action Toggle; Start-Sleep -Milliseconds 120 }
}
function Rect-Data($Rect) { return [ordered]@{ X=$Rect.X;Y=$Rect.Y;Width=$Rect.Width;Height=$Rect.Height } }
function Inside($Outer,$Inner) { return -not $Inner.IsEmpty -and $Inner.Width -gt 0 -and $Inner.Height -gt 0 -and $Inner.Left -ge $Outer.Left-1 -and $Inner.Top -ge $Outer.Top-1 -and $Inner.Right -le $Outer.Right+1 -and $Inner.Bottom -le $Outer.Bottom+1 }
function Read-MainState {
    $dimensions=Find-Control DimensionsButton
    $canvas=Find-Control PaintCanvas
    $zoom=(Find-Control ZoomSlider).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value
    $viewport=Find-Control Viewport -Optional
    $scroll=$null; $scrollData=$null
    if ($viewport -and $viewport.TryGetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern,[ref]$scroll)) {
        $scrollData=[ordered]@{Horizontal=$scroll.Current.HorizontalScrollPercent;Vertical=$scroll.Current.VerticalScrollPercent}
    }
    return [pscustomobject]@{Dimensions=$dimensions.Current.Name;DimensionsBounds=(Rect-Data $dimensions.Current.BoundingRectangle);CanvasBounds=(Rect-Data $canvas.Current.BoundingRectangle);Zoom=$zoom;Scroll=$scrollData;Title=(Window-Root).Current.Name;Undo=(Find-Control UndoButton).Current.IsEnabled}
}
function Assert-MainUnchanged($Before,[string]$Phase) {
    $after=Read-MainState
    if (($Before | ConvertTo-Json -Depth 5 -Compress) -ne ($after | ConvertTo-Json -Depth 5 -Compress)) { throw "サイズ変更の候補入力だけで本体の表示/文書状態が変わりました: $Phase" }
}
function Read-PreviewDiagnostic([string]$Phase,[string]$ExpectedPrefix) {
    $diagnostic=[ordered]@{Phase=$Phase;ExpectedStatusPrefix=$ExpectedPrefix;Views=@();Inputs=@();Error=$null;ApplyEnabled=$null}
    foreach($id in $viewIds) {
        try {
            $view=Find-Control $id -Optional
            $diagnostic.Views += if($view){[pscustomobject]@{Id=$id;Name=$view.Current.Name;Status=$view.Current.ItemStatus}}else{[pscustomobject]@{Id=$id;Missing=$true}}
        } catch { $diagnostic.Views += [pscustomobject]@{Id=$id;ReadError=$_.Exception.Message} }
    }
    foreach($id in @('DialogImageWidth','DialogImageHeight','CanvasResizeWidthInput','CanvasResizeHeightInput')) {
        $entry=[ordered]@{Id=$id;Name=$null;ClassName=$null;ControlType=$null;RangeValue=$null;TextValue=$null;RuntimeId=$null}
        try {
            $input=Find-Control $id -Optional
            if(-not $input){$entry.Missing=$true}else{
                $entry.Name=$input.Current.Name;$entry.ClassName=$input.Current.ClassName;$entry.ControlType=$input.Current.ControlType.ProgrammaticName;$entry.RuntimeId=$input.GetRuntimeId() -join ','
                $range=$null;$value=$null
                if($input.TryGetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern,[ref]$range)){$entry.RangeValue=$range.Current.Value}
                if($input.TryGetCurrentPattern([Windows.Automation.ValuePattern]::Pattern,[ref]$value)){$entry.TextValue=$value.Current.Value}
            }
        } catch { $entry.ReadError=$_.Exception.Message }
        $diagnostic.Inputs += [pscustomobject]$entry
    }
    try {
        $errorElement=Find-Control CanvasResizeError -Optional
        if($errorElement){
            $text=$null;$errorText=$null
            if($errorElement.TryGetCurrentPattern([Windows.Automation.TextPattern]::Pattern,[ref]$text)){$errorText=$text.DocumentRange.GetText(-1)}
            $diagnostic.Error=[pscustomobject]@{Name=$errorElement.Current.Name;Text=$errorText}
        }
    } catch { $diagnostic.Error=[pscustomobject]@{ReadError=$_.Exception.Message} }
    try { $diagnostic.ApplyEnabled=(Find-Control DialogPrimaryButton).Current.IsEnabled } catch { $diagnostic.ApplyReadError=$_.Exception.Message }
    $report.Diagnostics.Add([pscustomobject]$diagnostic)
}
function Read-Views([int]$Width,[int]$Height,[string]$Phase,[bool]$Centered=$true) {
    $offsetX=if($Centered){[int][Math]::Truncate(($Width-501)/2)}else{0}
    $offsetY=if($Centered){[int][Math]::Truncate(($Height-333)/2)}else{0}
    $prefix="候補: $Width × $Height px、画像の移動 $offsetX, $offsetY px"
    $deadline=[DateTime]::UtcNow.AddSeconds(10)
    do {
        $views=@();$ready=$true
        foreach($id in $viewIds) {
            $view=Find-Control $id -Optional
            if (-not $view -or -not $view.Current.ItemStatus.StartsWith($prefix,[StringComparison]::Ordinal)) { $ready=$false;break }
            $views += [pscustomobject]@{Id=$id;Name=$view.Current.Name;Status=$view.Current.ItemStatus;Bounds=(Rect-Data $view.Current.BoundingRectangle)}
        }
        if ($ready) { $report.Updates.Add([pscustomobject]@{Phase=$Phase;Width=$Width;Height=$Height;Views=$views}); return ,$views }
        Start-Sleep -Milliseconds 80
    } while ([DateTime]::UtcNow -lt $deadline)
    Read-PreviewDiagnostic $Phase $prefix
    throw "９ビューのDraw後の状態が候補寸法にそろいません: $Phase / $Width × $Height"
}
function Assert-Layout([int]$WindowWidth,[int]$WindowHeight,$Views) {
    $dialog=(Wait-Dialog).Current.BoundingRectangle
    $outer=(Window-Root).Current.BoundingRectangle
    $rects=@{}
    foreach ($view in $Views) {
        $bounds=(Find-Control $view.Id).Current.BoundingRectangle
        if (-not (Inside $dialog $bounds) -or -not (Inside $outer $bounds)) { throw "プレビューが画面からはみ出しています: $($view.Id)" }
        $rects[$view.Id]=$bounds
    }
    foreach($id in @('DialogImageWidth','DialogImageHeight','DialogImageRatio','DialogCanvasCentered','DialogPrimaryButton','DialogCloseButton')) {
        $bounds=(Find-Control $id).Current.BoundingRectangle
        if (-not (Inside $dialog $bounds) -or -not (Inside $outer $bounds)) { throw "操作要素が画面からはみ出しています: $id" }
    }
    $center=$rects.CanvasResizeThumbnail
    foreach ($side in @('TopLeft','Top','TopRight')) { if ($rects['CanvasResizeDetail'+$side].Bottom -gt $center.Top+1) { throw "上方向の配置が中央図より上ではありません: $side" } }
    foreach ($side in @('BottomLeft','Bottom','BottomRight')) { if ($rects['CanvasResizeDetail'+$side].Top -lt $center.Bottom-1) { throw "下方向の配置が中央図より下ではありません: $side" } }
    foreach ($side in @('TopLeft','Left','BottomLeft')) { if ($rects['CanvasResizeDetail'+$side].Right -gt $center.Left+1) { throw "左方向の配置が中央図より左ではありません: $side" } }
    foreach ($side in @('TopRight','Right','BottomRight')) { if ($rects['CanvasResizeDetail'+$side].Left -lt $center.Right-1) { throw "右方向の配置が中央図より右ではありません: $side" } }
    $report.Layouts.Add([pscustomobject]@{Width=$WindowWidth;Height=$WindowHeight;DialogBounds=(Rect-Data $dialog);ViewCount=$Views.Count;Views=$Views;AllControlsVisible=$true;EightDirections=$true})
}
function Capture-Window([string]$Label) {
    if (-not $CaptureWindows) { return }
    if (-not (Save-WindowScreenshot -WindowHandle $script:EditorTestHandle -Path (Join-Path $OutputDirectory "$Label-$runId.png"))) { throw '対象ウィンドウの前面撮影を確認できません。' }
    $report.ScreenshotsTaken=$true
}
function Cancel-Dialog { Invoke-Id DialogCloseButton; $null=Wait-Dialog $false }
try {
    $process=Start-Process -FilePath $AppPath -ArgumentList @('--settings-dir',('"'+$SettingsDirectory+'"'),('"'+$fixturePath+'"')) -PassThru
    $report.ProcessId=$process.Id
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
    $deadline=[DateTime]::UtcNow.AddSeconds(20);$windows=@()
    do {
        if ($process.HasExited) { throw '専用プロセスが終了しました。' }
        $windows=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$condition) | Where-Object {$_.Current.Name -like '* - PPPaint - v*' -and $_.Current.BoundingRectangle.Width -gt 100})
        if (-not $windows.Count) { Start-Sleep -Milliseconds 100 }
    } while (-not $windows.Count -and [DateTime]::UtcNow -lt $deadline)
    if ($windows.Count -ne 1) { throw '専用編集ウィンドウを一意に特定できません。' }
    $script:EditorTestHandle=[IntPtr]$windows[0].Current.NativeWindowHandle
    do {
        $initial=Read-MainState
        if ($initial.Dimensions -match '^501 × 333 px' -and -not $initial.Title.StartsWith('*')) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($initial.Dimensions -notmatch '^501 × 333 px' -or $initial.Title.StartsWith('*') -or $initial.Undo) { throw '専用PNGがクリーンな文書として読み込まれていません。' }
    $transform=$null
    if (-not (Window-Root).TryGetCurrentPattern([Windows.Automation.TransformPattern]::Pattern,[ref]$transform) -or -not $transform.Current.CanResize) { throw '1280/920幅の配置を検証するTransformPatternがありません。' }
    foreach($layout in @(@(1280,900),@(920,760))) {
        $transform.Resize($layout[0],$layout[1]);Start-Sleep -Milliseconds 250
        $baseline=Read-MainState
        Invoke-Id DimensionsButton;$null=Wait-Dialog
        $views=Read-Views 501 333 ("initial-"+$layout[0]);Assert-Layout $layout[0] $layout[1] $views
        Set-Check DialogImageRatio $false
        Set-InputText CanvasResizeWidthInput '500'
        $null=Read-Views 500 333 ("typed-width-without-commit-"+$layout[0]);Assert-MainUnchanged $baseline 'typed-width-without-commit'
        Set-InputText CanvasResizeHeightInput '332'
        $null=Read-Views 500 332 ("typed-height-without-commit-"+$layout[0]);Assert-MainUnchanged $baseline 'typed-height-without-commit'
        foreach($field in @(@('CanvasResizeWidthInput','500'),@('CanvasResizeHeightInput','332'))) {
            foreach($invalid in @('','abc','499.5')) {
                Set-InputText $field[0] $invalid
                if ((Find-Control DialogPrimaryButton).Current.IsEnabled) { throw "入力途中の不正な値で適用が有効です: $($field[0]) / '$invalid'" }
                Assert-MainUnchanged $baseline 'invalid-typed-input'
                Set-InputText $field[0] $field[1]
                $null=Read-Views 500 332 ("typed-integer-recovery-"+$field[0]+'-'+$layout[0])
                if (-not (Find-Control DialogPrimaryButton).Current.IsEnabled) { throw "整数へ訂正しても適用が再有効になりません: $($field[0])" }
                Assert-MainUnchanged $baseline 'valid-typed-input-recovery'
            }
        }
        Set-InputText CanvasResizeHeightInput '333'
        $null=Read-Views 500 333 ("typed-height-restored-"+$layout[0])
        Set-Number DialogImageWidth 500;$null=Read-Views 500 333 ("width-minus-one-"+$layout[0]);Assert-MainUnchanged $baseline 'width-minus-one'
        Set-Number DialogImageWidth 499;$null=Read-Views 499 333 ("width-minus-two-"+$layout[0]);Assert-MainUnchanged $baseline 'width-minus-two'
        Set-Number DialogImageHeight 332;$centered=Read-Views 499 332 ("height-minus-one-"+$layout[0]);Assert-MainUnchanged $baseline 'height-minus-one'
        Capture-Window ("centered-"+$layout[0])
        Set-Check DialogCanvasCentered $false
        $anchored=Read-Views 499 332 ("left-top-anchor-"+$layout[0]) $false;Assert-MainUnchanged $baseline 'left-top-anchor'
        for ($index=0;$index -lt $viewIds.Count;$index++) { if ($centered[$index].Status -eq $anchored[$index].Status) { throw "中心基準切替後に描画状態が更新されていません: $($viewIds[$index])" } }
        Assert-Layout $layout[0] $layout[1] $anchored;Capture-Window ("left-top-anchor-"+$layout[0])
        Set-Number DialogImageWidth 499.5
        if ((Find-Control DialogPrimaryButton).Current.IsEnabled) { throw '小数幅の適用が無効になっていません。' }
        Assert-MainUnchanged $baseline 'invalid-width'
        Cancel-Dialog;Assert-MainUnchanged $baseline 'cancel'
        $report.Flows.Add([pscustomobject]@{Width=$layout[0];Height=$layout[1];NineViews=$true;OnePixelChanges=$true;TextBoxUpdatesWithoutCommit=$true;InputFocusRequestedBeforeEdit=$true;FocusMoveAfterEditRequested=$false;TabOrEnterSent=$false;IncompleteTextRetained=$true;BothInputsRejectEmptyTextFraction=$true;IntegerCorrectionReenablesApply=$true;CenterAndAnchorUpdated=$true;MainStatePreserved=$true;InvalidFractionRejected=$true;CancelPreservedDocument=$true})
        Invoke-Id DimensionsButton;$null=Wait-Dialog;Set-Check DialogImageRatio $false;Set-Number DialogImageWidth 499;Set-Number DialogImageHeight 332
        $null=Read-Views 499 332 ("before-apply-"+$layout[0]);Assert-MainUnchanged $baseline 'before-apply'
        Invoke-Id DialogPrimaryButton;$null=Wait-Dialog $false;$ownUndoCount++
        if ((Find-Control DimensionsButton).Current.Name -notmatch '^499 × 332 px' -or -not (Find-Control UndoButton).Current.IsEnabled) { throw '適用後の寸法と履歴が一致しません。' }
        Invoke-Id UndoButton;$ownUndoCount--
        $after=Read-MainState
        if ($after.Dimensions -ne $baseline.Dimensions -or $after.Title -ne $baseline.Title -or $after.Undo) { throw '１回のUndoで元の文書へ戻りません。' }
        $report.Flows.Add([pscustomobject]@{Width=$layout[0];ApplyChangedDimensions=$true;OneUndoRestored=$true})
    }
    $report.Status='verified-uia-layout-and-flow'
} catch { $failure=$_;$report.Status='failed';$report.Error=$_.Exception.Message }
finally {
    $cleanup=[ordered]@{CancelledDialog=$false;OwnEditsUndone=0;Closed=$false;Error=$null}
    if ($process -and -not $process.HasExited -and $script:EditorTestHandle -ne [IntPtr]::Zero) {
        try {
            if (Find-Control EditorDialog -Optional) { Cancel-Dialog;$cleanup.CancelledDialog=$true }
            while ($ownUndoCount -gt 0) {
                if (-not (Find-Control UndoButton).Current.IsEnabled) { throw '専用の検証変更をUndoできません。' }
                Invoke-Id UndoButton;$ownUndoCount--;$cleanup.OwnEditsUndone++
            }
            if ((Window-Root).Current.Name.StartsWith('*')) { throw '未保存状態が残るため強制終了しません。' }
            (Window-Root).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
            $cleanup.Closed=$process.WaitForExit(5000)
        } catch { $cleanup.Error=$_.Exception.Message }
    } elseif ($process -and $process.HasExited) { $cleanup.Closed=$true }
    if ($process -and -not $cleanup.Closed -and -not $failure) { $failure=[InvalidOperationException]::new('専用ウィンドウの安全な終了を確認できません。');$report.Status='failed-cleanup';$report.Error=$failure.Message }
    $report.Cleanup=$cleanup
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $resultPath -Encoding utf8
}
if ($failure) { throw $failure }
[pscustomobject]@{Status=$report.Status;ProcessId=$report.ProcessId;Layouts=$report.Layouts.Count;Updates=$report.Updates.Count;Flows=$report.Flows.Count;Result=$resultPath;ScreenshotsTaken=$report.ScreenshotsTaken;PhysicalInputUsed=$false;PixelRenderingVerified=$false;Cleanup=$report.Cleanup} | ConvertTo-Json -Depth 5
