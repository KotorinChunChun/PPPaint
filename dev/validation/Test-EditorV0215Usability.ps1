[CmdletBinding()]
param([Parameter(Mandatory)][int]$TargetProcessId,[Parameter(Mandatory)][string]$SourcePath,[Parameter(Mandatory)][string]$OutputDirectory)

# Disposable profile, synthetic input, UIA only. No physical input or clipboard.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $TargetProcessId
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/'))
foreach($path in @($SourcePath,$OutputDirectory)){if(-not [IO.Path]::GetFullPath($path).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'artifacts配下に限定してください。'}}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$inputFolder=Split-Path $SourcePath
New-Item -ItemType Directory -Force (Join-Path $inputFolder Child) | Out-Null
$window=[Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle)
$transform=$window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern)
Add-Type 'using System; using System.Runtime.InteropServices; public static class PPPaintUsabilityDpi { [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd); }'
$scale=[PPPaintUsabilityDpi]::GetDpiForWindow($script:EditorTestHandle)/96.0
function Control([string]$Id){Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id}
function Value([string]$Id){(Control $Id).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value}
function Number([string]$Id){(Control $Id).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value}
function Set-Number([string]$Id,[double]$Value){(Control $Id).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($Value);Start-Sleep -Milliseconds 100}
function Fit-Image {
    $pattern=(Control FitButton).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    if($pattern.Current.ToggleState -ne [Windows.Automation.ToggleState]::On){$pattern.Toggle();Start-Sleep -Milliseconds 150}
}
function Select-Folder {
    $list=Control DialogFileList
    $items=$list.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition)
    $item=@($items|Where-Object{$_.Current.Name -eq '▸ Child'})[0]
    if(-not $item){throw 'Childフォルダーが一覧にありません。'}
    $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Assert-Fit {
    $zoom=Number ZoomSlider
    $view=(Control Viewport).Current.BoundingRectangle
    $dimensions=[regex]::Matches((Control DimensionsButton).Current.Name,'\d+')
    # UIA canvas rectangles are clipped to the viewport; use pixel size, DPI and zoom instead.
    if([double]$dimensions[0].Value*$zoom/100*$scale -gt $view.Width+2 -or [double]$dimensions[1].Value*$zoom/100*$scale -gt $view.Height+2){throw "画像が全体表示に収まりません: $zoom / $view"}
    if((Control FitButton).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [Windows.Automation.ToggleState]::On){throw '全体表示の状態が表示されていません。'}
}
function Save-Png([string]$Name){
    Invoke-TestAction MoreButton;Invoke-TestAction SaveAsMenuItem
    Set-ElementValue -Element (Control DialogFolder) -Value $OutputDirectory
    Set-ElementValue -Element (Control DialogFileName) -Value $Name
    Invoke-TestAction DialogPrimaryButton
    $path=Join-Path $OutputDirectory $Name
    $deadline=[DateTime]::UtcNow.AddSeconds(5)
    while(-not(Test-Path $path) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 100}
    if(-not(Test-Path $path)){throw 'PNG保存に失敗しました。'}
    return $path
}
function Pixels([string]$Path){
    $bitmap=[Drawing.Bitmap]::new($Path)
    try{
        $data=$bitmap.LockBits([Drawing.Rectangle]::new(0,0,$bitmap.Width,$bitmap.Height),[Drawing.Imaging.ImageLockMode]::ReadOnly,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try{$bytes=[byte[]]::new([Math]::Abs($data.Stride)*$bitmap.Height);[Runtime.InteropServices.Marshal]::Copy($data.Scan0,$bytes,0,$bytes.Length);return ,$bytes}finally{$bitmap.UnlockBits($data)}
    }finally{$bitmap.Dispose()}
}
Select-TestTool Select
Fit-Image
$transform.Resize(1200,800);Start-Sleep -Milliseconds 200;Assert-Fit
$smallZoom=Number ZoomSlider
$transform.Resize(1500,1000);Start-Sleep -Milliseconds 200;Assert-Fit
if((Number ZoomSlider) -le $smallZoom){throw '全体表示がウィンドウ拡大に追従していません。'}
Set-Number ZoomSlider 150
$transform.Resize(1200,800);Start-Sleep -Milliseconds 200
if((Number ZoomSlider) -ne 150 -or (Control FitButton).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [Windows.Automation.ToggleState]::Off){throw '手動倍率がサイズ変更で変わりました。'}
Invoke-TestAction DimensionsButton;Set-Number DialogImageWidth 450;Invoke-TestAction DialogCloseButton
if((Number ZoomSlider) -ne 150){throw 'キャンバス変更取消で手動倍率が失われました。'}
Fit-Image;Invoke-TestAction DimensionsButton;Set-Number DialogImageWidth 450;Invoke-TestAction DialogCloseButton;Assert-Fit
Invoke-TestAction DimensionsButton;Set-Number DialogImageWidth 450;Invoke-TestAction DialogPrimaryButton;Assert-Fit
Invoke-TestAction UndoButton;Assert-Fit
if((Control DimensionsButton).Current.Name -notlike '900 × 550 px*'){throw '寸法変更Undoが戻りません。'}
Select-TestTool Pen;Set-Number WidthBox 12
Invoke-TestAction SettingsButton;Invoke-TestAction ResetDrawingPreferencesButton;Invoke-TestAction DialogCloseButton
if((Number WidthBox) -ne 3){throw '描画設定を初期化できません。'}
Invoke-TestAction OpenButton
$startingFolder=Value DialogFolder
Select-Folder
if((Value DialogFolder) -ne $startingFolder){throw '選択だけでフォルダーへ移動しました。'}
Invoke-TestAction DialogPrimaryButton
if((Value DialogFolder) -ne (Join-Path $inputFolder Child)){throw '選択フォルダーを確定できません。'}
Set-ElementValue -Element (Control DialogFileName) -Value ''
Invoke-TestAction DialogPrimaryButton
if((Control DialogValidationError).Current.Name -notlike '*画像ファイル*'){throw '空の名前を検証できません。'}
Invoke-TestAction DialogCloseButton
# Return to the synthetic input directory using an explicitly typed path.
Invoke-TestAction OpenButton
Set-ElementValue -Element (Control DialogFolder) -Value $inputFolder
Set-ElementValue -Element (Control DialogFileName) -Value '.'
Invoke-TestAction DialogPrimaryButton;Select-Folder
Set-ElementValue -Element (Control DialogFileName) -Value 'missing-image.png'
Invoke-TestAction DialogPrimaryButton
if((Value DialogFolder) -ne $inputFolder -or (Control DialogValidationError).Current.Name -notlike '*画像ファイル*'){throw '手入力した名前が旧フォルダー選択に奪われました。'}
Invoke-TestAction DialogCloseButton
Invoke-TestAction MoreButton;Invoke-TestAction SaveAsMenuItem
Set-ElementValue -Element (Control DialogFileName) -Value ('preserved-'+[guid]::NewGuid().ToString('N')+'.png')
$savedName=Value DialogFileName
Select-Folder;Invoke-TestAction DialogPrimaryButton
if((Value DialogFolder) -ne (Join-Path $inputFolder Child) -or (Value DialogFileName) -ne $savedName){throw 'フォルダー移動で保存名を保持できません。'}
Set-ElementValue -Element (Control DialogFolder) -Value $OutputDirectory
Invoke-TestAction DialogPrimaryButton
$saved=Join-Path $OutputDirectory $savedName
if(-not(Test-Path $saved)){throw '手入力先へPNG保存できません。'}
if(-not [Linq.Enumerable]::SequenceEqual[byte]((Pixels $SourcePath),(Pixels $saved))){throw '設定/寸法プレビューの取消/Undoで元画像が変わりました。'}
Invoke-TestAction MoreButton;Invoke-TestAction SelectAllMenuItem;Invoke-TestAction DeleteButton
if((Control WidthBox).Current.IsOffscreen){throw '選択削除後に設定欄が同期されていません。'}
Invoke-TestAction UndoButton
$restored=Save-Png ('undo-'+[guid]::NewGuid().ToString('N')+'.png')
if(-not [Linq.Enumerable]::SequenceEqual[byte]((Pixels $SourcePath),(Pixels $restored))){throw '削除Undo後のPNGが元画像と一致しません。'}
$result=[pscustomobject]@{AutoFitFollowsViewport=$true;AutoFitFollowsCanvasUndo=$true;ManualZoomRetained=$true;ResizeCancelRestoresMode=$true;FolderSelectAndConfirm=$true;TypedNameOverridesSelection=$true;EmptyNameValidation=$true;SaveNamePreserved=$true;ResetPreferences=$true;DeleteOptionsSynchronized=$true;PngAllBgraPreserved=$true;PhysicalKeyboardOrDoubleClickVerified=$false;Foreground=[WUA.Win]::GetForegroundWindow().ToInt64();DpiScale=$scale;SavedFiles=@($saved,$restored)}
$result|ConvertTo-Json -Depth 4|Set-Content (Join-Path $OutputDirectory 'usability-validation.json')
$window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
$result|ConvertTo-Json -Depth 4
