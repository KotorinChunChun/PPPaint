[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath,[Parameter(Mandatory)][string]$SettingsDirectory,[Parameter(Mandatory)][string]$OutputDirectory,[switch]$CaptureWindows)
# Exclusive mouse/keyboard test. Announce before running; all files/profiles are synthetic.
$ErrorActionPreference='Stop'
if([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA'){throw 'pwsh -Staで実行してください。'}
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $repoRoot artifacts))+[IO.Path]::DirectorySeparatorChar
foreach($path in @($SettingsDirectory,$OutputDirectory)){
    if(-not [IO.Path]::IsPathFullyQualified($path) -or -not [IO.Path]::GetFullPath($path).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw '専用設定と出力はartifacts配下の絶対パスにしてください。'}
}
if((Test-Path -LiteralPath $SettingsDirectory) -and @(Get-ChildItem -LiteralPath $SettingsDirectory -Force).Count){throw '設定は新規または空の専用フォルダーにしてください。'}
New-Item -ItemType Directory -Path $SettingsDirectory,$OutputDirectory -Force | Out-Null
. "$env:USERPROFILE/.codex/skills/windows-ui-automation/scripts/ui-toolkit.ps1"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing.Common
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class EditorV22Keys {
 [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
}
"@
$runId=[guid]::NewGuid().ToString('N')
$fixturePath=Join-Path $OutputDirectory ("native-fixture-$runId.png")
$fixture=[Drawing.Bitmap]::new(240,160,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
try{$fixture.SetPixel(15,15,[Drawing.Color]::Blue);$fixture.SetPixel(225,145,[Drawing.Color]::Blue);$fixture.Save($fixturePath,[Drawing.Imaging.ImageFormat]::Png)}finally{$fixture.Dispose()}
. (Join-Path $PSScriptRoot 'Clipboard-Snapshot.ps1')
$clipboardBefore=New-EditorClipboardSnapshot;$clipboardChanged=$false
$report=[ordered]@{Status='running';AppPath=$AppPath;ProcessId=$null;PhysicalInputUsed=$false;Checks=[Collections.Generic.List[object]]::new();Pngs=[Collections.Generic.List[string]]::new();ClipboardRestored=$false;Cleanup=$null}
$process=$null;$failure=$null;$script:EditorTestHandle=[IntPtr]::Zero
function Control([string]$Id,[switch]$Optional){Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id -AllowMissing:$Optional}
function Invoke-Id([string]$Id){Invoke-ElementAction -Element (Control $Id) -Action Invoke;Start-Sleep -Milliseconds 130}
function Set-Number([string]$Id,[double]$Value){(Control $Id).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($Value);Start-Sleep -Milliseconds 130}
function Dimensions {
    $matches=[regex]::Matches((Control DimensionsButton).Current.Name,'\d+')
    return ,@([int]$matches[0].Value,[int]$matches[1].Value)
}
function Assert-Dimensions([int]$Width,[int]$Height){$actual=Dimensions;if($actual[0] -ne $Width -or $actual[1] -ne $Height){throw "寸法不一致: $($actual -join 'x') != ${Width}x${Height}"}}
function Selected-Count { $status=(Control InteractionCanvas).Current.ItemStatus;if($status -match '^選択: (\d+)個$'){return [int]$Matches[1]};return 0 }
function Assert-Selected([int]$Count){$actual=Selected-Count;if($actual -ne $Count){throw "選択数不一致: $actual != $Count / $((Control InteractionCanvas).Current.ItemStatus)"}}
function Checked-Key([string]$Keys){
    if(-not(Set-ForegroundForced -WindowHandle $script:EditorTestHandle)){throw 'INPUT_UNAVAILABLE: 対象を前面化できません。'}
    Assert-ForegroundIs -WindowHandle $script:EditorTestHandle
    Send-TextInput -WindowHandle $script:EditorTestHandle -Text $Keys -Raw
    $report.PhysicalInputUsed=$true;Start-Sleep -Milliseconds 180
}
function Modifier([byte]$Key,[bool]$Down){
    if($Down){Assert-ForegroundIs -WindowHandle $script:EditorTestHandle}
    [EditorV22Keys]::keybd_event($Key,0,$(if($Down){0}else{2}),[UIntPtr]::Zero)
}
function Ctrl-Click([double]$X,[double]$Y){
    if(-not(Set-ForegroundForced -WindowHandle $script:EditorTestHandle)){throw 'INPUT_UNAVAILABLE: 対象を前面化できません。'}
    Modifier 0x11 $true
    try{Invoke-TestImageClick $X $Y}finally{Modifier 0x11 $false}
    $report.PhysicalInputUsed=$true;Start-Sleep -Milliseconds 140
}
function Drag-Image([double]$X1,[double]$Y1,[double]$X2,[double]$Y2,[switch]$Control,[switch]$Cancel){
    if(-not(Set-ForegroundForced -WindowHandle $script:EditorTestHandle)){throw 'INPUT_UNAVAILABLE: 対象を前面化できません。'}
    if($Control){Modifier 0x11 $true}
    try{
        if($Cancel){
            $a=Get-TestImagePoint $X1 $Y1;$b=Get-TestImagePoint $X2 $Y2
            Assert-PointInWindow -WindowHandle $script:EditorTestHandle -X $a[0] -Y $a[1]
            Assert-PointInWindow -WindowHandle $script:EditorTestHandle -X $b[0] -Y $b[1]
            Assert-ForegroundIs -WindowHandle $script:EditorTestHandle
            [void][WUA.Win]::SetCursorPos([int]$a[0],[int]$a[1]);[WUA.Win]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
            try{Start-Sleep -Milliseconds 80;Assert-ForegroundIs -WindowHandle $script:EditorTestHandle;[void][WUA.Win]::SetCursorPos([int]$b[0],[int]$b[1]);Start-Sleep -Milliseconds 120;Checked-Key '{ESC}'}finally{[WUA.Win]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
        }else{Invoke-TestImageDrag $X1 $Y1 $X2 $Y2}
    }finally{if($Control){Modifier 0x11 $false}}
    $report.PhysicalInputUsed=$true;Start-Sleep -Milliseconds 160
}
function Save-OwnPng([string]$Label){
    Invoke-Id FileButton;Invoke-Id SaveAsMenuItem
    $path=Join-Path $OutputDirectory ("$Label-$runId.png")
    Set-ElementValue -Element (Control DialogFolder) -Value $OutputDirectory
    Set-ElementValue -Element (Control DialogFileName) -Value ([IO.Path]::GetFileName($path))
    Invoke-Id DialogPrimaryButton
    $deadline=[DateTime]::UtcNow.AddSeconds(8)
    while(-not(Test-Path -LiteralPath $path) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 100}
    if(-not(Test-Path -LiteralPath $path)){throw '検証PNGが保存されません。'}
    $report.Pngs.Add($path);return $path
}
function Compare-Png([string]$Left,[string]$Right){
    $a=[Drawing.Bitmap]::new($Left);$b=[Drawing.Bitmap]::new($Right);$max=0;$different=0
    try{
        if($a.Width -ne $b.Width -or $a.Height -ne $b.Height){throw '統合前後のPNG寸法が違います。'}
        for($y=0;$y -lt $a.Height;$y++){for($x=0;$x -lt $a.Width;$x++){
            $p=$a.GetPixel($x,$y);$q=$b.GetPixel($x,$y)
            if($p.A -ne $q.A){throw "統合前後のalphaが違います: $x,$y"}
            $delta=[Math]::Max([Math]::Abs([int]$p.R-$q.R),[Math]::Max([Math]::Abs([int]$p.G-$q.G),[Math]::Abs([int]$p.B-$q.B)))
            if($delta -gt 0){$different++};$max=[Math]::Max($max,$delta)
        }}
        if($max -gt 1){throw "統合前後の色が大きく変化しました: $max"}
        return [pscustomobject]@{MaximumRgbDelta=$max;ChangedRgbPixels=$different;AlphaExact=$true}
    }finally{$a.Dispose();$b.Dispose()}
}
function Select-TwoShapes {
    Invoke-TestImageClick 110 65;Assert-Selected 0
    Invoke-TestImageClick 30 50;Ctrl-Click 160 80;Assert-Selected 2
}
try{
    $process=Start-Process -FilePath $AppPath -ArgumentList @('--settings-dir',('"'+$SettingsDirectory+'"'),('"'+$fixturePath+'"')) -PassThru
    $report.ProcessId=$process.Id
    $deadline=[DateTime]::UtcNow.AddSeconds(20)
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
    do{
        $windows=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$condition)|Where-Object{$_.Current.Name -like '* - PPPaint - v*'})
        if($windows.Count -ne 1){Start-Sleep -Milliseconds 100}
    }while($windows.Count -ne 1 -and [DateTime]::UtcNow -lt $deadline)
    if($windows.Count -ne 1){throw '専用窓を一意に確認できません。'}
    . (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $process.Id
    do{$size=Dimensions;if($size[0] -eq 240 -and $size[1] -eq 160){break};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $deadline)
    Assert-Dimensions 240 160;Set-Number ZoomSlider 100
    Invoke-TestImageClick 100 60
    $tiny=[Drawing.Bitmap]::new(40,30)
    try{$g=[Drawing.Graphics]::FromImage($tiny);try{$g.Clear([Drawing.Color]::OrangeRed)}finally{$g.Dispose()};$clipboardChanged=$true;[Windows.Forms.Clipboard]::SetImage($tiny)}finally{$tiny.Dispose()}
    Checked-Key '^v';Assert-Dimensions 240 160
    if(Control PasteModeOverlay -Optional){throw 'Ctrl+Vでメニューが開きました。'}
    Assert-Selected 1;Invoke-Id UndoButton
    Checked-Key '^+v';$null=Control PasteModeOverlay
    foreach($mode in @('Overlay','Top','Bottom','Left','Right')){$null=Control ('PasteMode'+$mode)}
    Checked-Key '{ESC}';Assert-Dimensions 240 160
    $report.Checks.Add([pscustomobject]@{Check='CtrlVOverlayAndCtrlShiftVMenu';Passed=$true})
    Checked-Key '^c';Checked-Key '^v';Assert-Dimensions 240 160
    if(Control PasteModeOverlay -Optional){throw '内部コピーのCtrl+Vでメニューが開きました。'}
    Invoke-Id UndoButton
    $report.Checks.Add([pscustomobject]@{Check='InAppClipboardOverlay';Passed=$true})
    foreach($case in @(@('TopLeft',-5,-5,-1,-1),@('Top',120,-5,0,-1),@('TopRight',245,-5,1,-1),@('Left',-5,80,-1,0),@('Right',245,80,1,0),@('BottomLeft',-5,165,-1,1),@('Bottom',120,165,0,1),@('BottomRight',245,165,1,1))){
        foreach($mode in @('Grow','Shrink','CenterGrow')){
            $sign=if($mode -eq 'Shrink'){-1}else{1};$ctrl=$mode -eq 'CenterGrow'
            $dx=[double]$case[3]*2*$sign;$dy=[double]$case[4]*2*$sign
            Drag-Image $case[1] $case[2] ($case[1]+$dx) ($case[2]+$dy) -Control:$ctrl
            $increment=if($ctrl){4}else{2*$sign}
            Assert-Dimensions (240+$(if($case[3] -ne 0){$increment}else{0})) (160+$(if($case[4] -ne 0){$increment}else{0}))
            Invoke-Id UndoButton;Assert-Dimensions 240 160
            $report.Checks.Add([pscustomobject]@{Check='CanvasDrag';Handle=$case[0];Mode=$mode;Passed=$true})
        }
    }
    Drag-Image -5 -5 -6 -7;Assert-Dimensions 241 162;Invoke-Id UndoButton
    Drag-Image 245 165 255 175 -Cancel;Assert-Dimensions 240 160
    $report.Checks.Add([pscustomobject]@{Check='OneTwoPixelDragAndEscapeCancel';Passed=$true})
    Select-TestTool Rectangle;Set-Number WidthBox 2;Drag-Image 30 30 90 70
    Select-TestTool Ellipse;Set-Number WidthBox 2;Drag-Image 130 80 190 120
    Ctrl-Click 30 50;Assert-Selected 2
    Invoke-TestImageClick 110 65;Assert-Selected 0
    Invoke-TestImageClick 30 50;Assert-Selected 1
    $empty=Get-TestImagePoint -45 50;Invoke-Click -WindowHandle $script:EditorTestHandle -X $empty[0] -Y $empty[1];Assert-Selected 0
    Checked-Key '^a';Assert-Selected 3;Invoke-TestImageClick 110 65;Assert-Selected 0
    if((Control InteractionCanvas).Current.ItemStatus -ne '選択なし'){throw '範囲内の空白クリックで範囲選択が解除されません。'}
    Select-TestTool Select
    Select-TwoShapes
    Drag-Image 110 65 120 65;Assert-Selected 2
    Invoke-Id UndoButton
    Select-TwoShapes
    $report.Checks.Add([pscustomobject]@{Check='EmptyGroupAndWorkspaceDeselectAndDragGroup';Passed=$true})
    $before=Save-OwnPng 'before-merge'
    Select-TwoShapes
    Checked-Key '^e';Assert-Selected 1
    $merged=Save-OwnPng 'merged';$comparison=Compare-Png $before $merged
    Invoke-Id UndoButton
    Select-TwoShapes
    Invoke-Id MoreButton;Invoke-Id MergeSelectionMenuItem;Assert-Selected 1
    $menuMerged=Save-OwnPng 'menu-merged';$menuComparison=Compare-Png $before $menuMerged
    Invoke-Id UndoButton
    Select-TwoShapes
    $report.Checks.Add([pscustomobject]@{Check='RasterMergeCtrlEAndMenuAndUndo';Passed=$true;PngComparison=$comparison;MenuPngComparison=$menuComparison})
    if($CaptureWindows){Save-TestWindow (Join-Path $OutputDirectory ('editor-v22-'+$runId+'.png'))}
    $report.Status='verified-native-input'
}catch{$failure=$_;$report.Status='failed';$report.Error=$_.Exception.Message}
finally{
    $cleanup=[ordered]@{Closed=$false;Error=$null}
    try{
        if($process -and -not $process.HasExited -and $script:EditorTestHandle -ne [IntPtr]::Zero){
            $cancel=Control DialogCloseButton -Optional;if($cancel){Invoke-ElementAction -Element $cancel -Action Invoke;Start-Sleep -Milliseconds 120}
            [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close();Start-Sleep -Milliseconds 200
            if(-not $process.HasExited){$discard=Control DialogSecondaryButton -Optional;if($discard){Invoke-ElementAction -Element $discard -Action Invoke}}
            $cleanup.Closed=$process.WaitForExit(5000)
        }elseif($process -and $process.HasExited){$cleanup.Closed=$true}
    }catch{$cleanup.Error=$_.Exception.Message}
    finally{
        if($clipboardChanged){if($clipboardBefore){[Windows.Forms.Clipboard]::SetDataObject($clipboardBefore,$true)}else{[Windows.Forms.Clipboard]::Clear()}}
        $report.ClipboardRestored=$true
    }
    $report.Cleanup=$cleanup
    $report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $OutputDirectory ('native-editing-'+$runId+'.json')) -Encoding utf8
}
$report|ConvertTo-Json -Depth 8
if($failure){throw $failure}
if(-not $report.Cleanup.Closed){throw '専用窓の安全終了を確認できません。'}
