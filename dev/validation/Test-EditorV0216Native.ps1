[CmdletBinding()]
param([Parameter(Mandatory)][int]$TargetProcessId,[Parameter(Mandatory)][string]$OutputDirectory)

# Dedicated blank test document only. Announce exclusive mouse use before running.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $TargetProcessId
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/'))
if(-not [IO.Path]::GetFullPath($OutputDirectory).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw '出力はartifacts配下に限定してください。'}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class ToolCursorInspection {
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
 [StructLayout(LayoutKind.Sequential)] public struct CursorInfo { public int Size,Flags; public IntPtr Handle; public Point Position; }
 [StructLayout(LayoutKind.Sequential)] public struct IconInfo { [MarshalAs(UnmanagedType.Bool)] public bool Icon; public uint X,Y; public IntPtr Mask,Color; }
 [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CursorInfo info);
 [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr cursor,out IconInfo info);
 [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr handle);
}
'@
function Control([string]$Id){Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id}
function Set-Number([string]$Id,[double]$Value){(Control $Id).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($Value);Start-Sleep -Milliseconds 120}
function Move-ImagePointer([double]$X,[double]$Y){
    $point=Get-TestImagePoint $X $Y
    if(-not(Set-ForegroundForced -WindowHandle $script:EditorTestHandle)){throw '対象を前面化できません。'}
    Assert-PointInWindow -WindowHandle $script:EditorTestHandle -X $point[0] -Y $point[1]
    $null=[WUA.Win]::SetCursorPos([int]$point[0],[int]$point[1]);Start-Sleep -Milliseconds 200
    Assert-ForegroundIs -WindowHandle $script:EditorTestHandle
}
function Cursor-Details([string]$Expected){
    if((Control CanvasCursorHost).Current.ItemStatus -ne $Expected){throw "カーソル用途が不一致です: $Expected"}
    $cursor=[ToolCursorInspection+CursorInfo]::new();$cursor.Size=[Runtime.InteropServices.Marshal]::SizeOf($cursor)
    if(-not[ToolCursorInspection]::GetCursorInfo([ref]$cursor)){throw '実カーソルを確認できません。'}
    $icon=[ToolCursorInspection+IconInfo]::new()
    if(-not[ToolCursorInspection]::GetIconInfo($cursor.Handle,[ref]$icon)){throw '実カーソルの先端を確認できません。'}
    try{return [pscustomobject]@{Role=$Expected;HotspotX=$icon.X;HotspotY=$icon.Y;Handle=$cursor.Handle.ToInt64()}}
    finally{if($icon.Mask -ne [IntPtr]::Zero){[void][ToolCursorInspection]::DeleteObject($icon.Mask)};if($icon.Color -ne [IntPtr]::Zero){[void][ToolCursorInspection]::DeleteObject($icon.Color)}}
}
function Save-Png([string]$Prefix){
    Invoke-TestAction MoreButton;Invoke-TestAction SaveAsMenuItem
    $path=Join-Path $OutputDirectory ($Prefix+'-'+[guid]::NewGuid().ToString('N')+'.png')
    Set-ElementValue -Element (Control DialogFolder) -Value $OutputDirectory
    Set-ElementValue -Element (Control DialogFileName) -Value ([IO.Path]::GetFileName($path))
    Invoke-TestAction DialogPrimaryButton
    $deadline=[DateTime]::UtcNow.AddSeconds(5)
    while(-not(Test-Path $path) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 100}
    if(-not(Test-Path $path)){throw 'PNG保存に失敗しました。'}
    return $path
}
Invoke-TestAction DimensionsButton
$ratio=Control DialogImageRatio
if($ratio.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [Windows.Automation.ToggleState]::On){Invoke-ElementAction -Element $ratio -Action Toggle}
Set-Number DialogImageWidth 240;Set-Number DialogImageHeight 160;Invoke-TestAction DialogPrimaryButton
Set-Number ZoomSlider 100
Select-TestTool Pen;Set-Number WidthBox 1
Move-ImagePointer 60.35 60.35
$pencil=Cursor-Details '鉛筆'
if($pencil.HotspotY -le $pencil.HotspotX*3){throw '鉛筆の先端位置が不適切です。'}
Invoke-TestImageClick 60.35 60.35
$penPng=Save-Png 'pen-one-pixel'
$image=[Drawing.Bitmap]::new($penPng)
try{
    $ink=0;for($y=0;$y -lt $image.Height;$y++){for($x=0;$x -lt $image.Width;$x++){if($image.GetPixel($x,$y).ToArgb() -ne [Drawing.Color]::White.ToArgb()){$ink++}}}
    if($ink -ne 1 -or $image.GetPixel(60,60).R -ne 220){throw "実クリックで1画素を描けません: $ink"}
}finally{$image.Dispose()}
Select-TestTool Eraser;Set-Number WidthBox 1
Move-ImagePointer 60.35 60.35
$eraser=Cursor-Details '消しゴム（四角）'
if($eraser.HotspotX -ne 0 -or $eraser.HotspotY -ne 0){throw '消しゴムのプラスが残っています。'}
Invoke-TestImageClick 60.35 60.35
$erasedPng=Save-Png 'erased-one-pixel'
$image=[Drawing.Bitmap]::new($erasedPng)
try{for($y=0;$y -lt $image.Height;$y++){for($x=0;$x -lt $image.Width;$x++){if($image.GetPixel($x,$y).ToArgb() -ne [Drawing.Color]::White.ToArgb()){throw "実消去に残りがあります: $x,$y"}}}}finally{$image.Dispose()}
Set-Number WidthBox 16;Move-ImagePointer 95.2 60.2
$screenshot=Join-Path $OutputDirectory 'square-eraser-window.png';Save-TestWindow $screenshot
Invoke-ElementAction -Element (Control EyedropperButton) -Action Toggle;Move-ImagePointer 80.2 70.2
$picker=Cursor-Details 'スポイト'
if($picker.HotspotY -le $picker.HotspotX*3){throw 'スポイトの先端位置が不適切です。'}
Invoke-TestImageClick 80.2 70.2
if((Control PaletteColor7).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [Windows.Automation.ToggleState]::On){throw 'スポイトで白を取得できません。'}
if((Control EyedropperButton).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [Windows.Automation.ToggleState]::Off){throw 'スポイト採取後に元のツールへ戻りません。'}
$result=[pscustomobject]@{ProcessId=$TargetProcessId;RealPointerAndClicks=$true;PenAndEraserOnePixelPng=$true;PickerColorAndReturn=$true;Cursors=@($pencil,$eraser,$picker);Pngs=@($penPng,$erasedPng);WindowScreenshot=$screenshot}
$result|ConvertTo-Json -Depth 5|Set-Content (Join-Path $OutputDirectory 'native-validation.json')
[Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
$result|ConvertTo-Json -Depth 5
