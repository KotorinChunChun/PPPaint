[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$SettingsDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [double]$Zoom = 67
)

# v0.2.30: moving an image at a non-100% zoom must keep it on whole pixels.
# Exclusive mouse test with a disposable profile and a synthetic checkerboard only.
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'pwsh -Sta -Fileで実行してください。' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
foreach ($path in @($AppPath, $SettingsDirectory, $OutputDirectory)) { if (-not [IO.Path]::IsPathFullyQualified($path)) { throw 'すべて絶対パスで指定してください。' } }
$AppPath = [IO.Path]::GetFullPath($AppPath); $SettingsDirectory = [IO.Path]::GetFullPath($SettingsDirectory); $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $AppPath.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $AppPath -PathType Leaf)) { throw 'リポジトリ配下のアプリ実行ファイルを指定してください。' }
foreach ($path in @($SettingsDirectory, $OutputDirectory)) { if (-not $path.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw '設定と出力はartifacts配下に限定してください。' } }
if ((Test-Path -LiteralPath $SettingsDirectory) -and @(Get-ChildItem -LiteralPath $SettingsDirectory -Force).Count) { throw '空または未作成の専用設定フォルダーを指定してください。' }
New-Item -ItemType Directory -Path $SettingsDirectory, $OutputDirectory -Force | Out-Null
. "$env:USERPROFILE/.codex/skills/windows-ui-automation/scripts/ui-toolkit.ps1"
Add-Type -AssemblyName System.Drawing.Common

$runId = [guid]::NewGuid().ToString('N')
$fixturePath = Join-Path $OutputDirectory "move-fixture-$runId.png"
$resultPath = Join-Path $OutputDirectory "move-validation-$runId.json"
$width = 120; $height = 80
$bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
try {
    for ($y = 0; $y -lt $height; $y++) { for ($x = 0; $x -lt $width; $x++) { $bitmap.SetPixel($x, $y, $(if (($x + $y) % 2) { [Drawing.Color]::White } else { [Drawing.Color]::Black })) } }
    $bitmap.Save($fixturePath, [Drawing.Imaging.ImageFormat]::Png)
} finally { $bitmap.Dispose() }

$report = [ordered]@{ AppPath = $AppPath; ProcessId = $null; Zoom = $Zoom; Status = 'running'; PhysicalInputUsed = $false; Moved = $null; Saved = $false; OpaquePixels = 0; InterpolatedPixels = 0; ImageOrigin = $null; Cleanup = $null }
$process = $null; $script:Handle = [IntPtr]::Zero; $failure = $null
function Root { $root = [Windows.Automation.AutomationElement]::FromHandle($script:Handle); if ($root.Current.ProcessId -ne $process.Id) { throw '検証対象PIDが変わりました。' }; $root }
function Control([string]$Id, [switch]$Optional) {
    $element = (Root).FindFirst([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id))
    if ($element -and ($element.Current.IsOffscreen -or $element.Current.BoundingRectangle.IsEmpty)) { $element = $null }
    if (-not $element -and -not $Optional) { throw "表示要素がありません: $Id" }
    $element
}
function Wait-Until([scriptblock]$Condition, [string]$Message) {
    $deadline = [DateTime]::UtcNow.AddSeconds(12)
    while (-not (& $Condition)) { if ([DateTime]::UtcNow -gt $deadline) { throw $Message }; Start-Sleep -Milliseconds 80 }
}
function Image-Point([double]$X, [double]$Y) {
    $rect = (Control PaintCanvas).Current.BoundingRectangle
    $numbers = [regex]::Matches((Control DimensionsButton).Current.Name, '\d+')
    ,@(($rect.X + $X * $rect.Width / [double]$numbers[0].Value), ($rect.Y + $Y * $rect.Height / [double]$numbers[1].Value))
}
function Drag-Image([double]$X1, [double]$Y1, [double]$X2, [double]$Y2) {
    $a = Image-Point $X1 $Y1; $b = Image-Point $X2 $Y2
    if (-not (Set-ForegroundForced -WindowHandle $script:Handle)) { throw 'INPUT_UNAVAILABLE: 対象を前面化できません。' }
    Assert-PointInWindow -WindowHandle $script:Handle -X $a[0] -Y $a[1]; Assert-PointInWindow -WindowHandle $script:Handle -X $b[0] -Y $b[1]
    Assert-ForegroundIs -WindowHandle $script:Handle
    [WUA.Win]::SetCursorPos([int]$a[0], [int]$a[1]); Start-Sleep -Milliseconds 60
    [WUA.Win]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); $report.PhysicalInputUsed = $true
    try {
        for ($step = 1; $step -le 12; $step++) {
            Assert-ForegroundIs -WindowHandle $script:Handle
            [WUA.Win]::SetCursorPos([int]($a[0] + ($b[0] - $a[0]) * $step / 12), [int]($a[1] + ($b[1] - $a[1]) * $step / 12)); Start-Sleep -Milliseconds 30
        }
    } finally { [WUA.Win]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 250
}
try {
    $process = Start-Process -FilePath $AppPath -ArgumentList @('--settings-dir', ('"' + $SettingsDirectory + '"'), ('"' + $fixturePath + '"')) -PassThru
    $report.ProcessId = $process.Id
    $processFilter = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    Wait-Until { if ($process.HasExited) { throw '専用プロセスが終了しました。' }; $script:windows = @([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $processFilter) | Where-Object { $_.Current.Name -like '* - PPPaint - v*' -and $_.Current.BoundingRectangle.Width -gt 100 }); $script:windows.Count -eq 1 } '専用編集ウィンドウを特定できません。'
    $script:Handle = [IntPtr]$script:windows[0].Current.NativeWindowHandle
    Wait-Until { (Control DimensionsButton).Current.Name -match "^$width × $height px" -and -not (Root).Current.Name.StartsWith('*') } '専用PNGが読み込まれていません。'
    (Control ZoomSlider).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($Zoom); Start-Sleep -Milliseconds 400
    # A fractional image-space distance at this zoom; the editor must round it.
    Drag-Image 60 40 77.3 51.6
    $report.Moved = (Root).Current.Name.StartsWith('*')
    if (-not $report.Moved) { throw 'ドラッグで画像が移動していません。' }
    Invoke-ElementAction -Element (Control SaveButton) -Action Invoke
    Wait-Until { Control EditorDialog -Optional } '上書き確認が表示されません。'
    Invoke-ElementAction -Element (Control DialogPrimaryButton) -Action Invoke
    Wait-Until { -not (Root).Current.Name.StartsWith('*') -and -not (Control EditorDialog -Optional) } '保存が完了しません。'
    $report.Saved = $true

    $saved = [Drawing.Bitmap]::new($fixturePath)
    try {
        $left = $width; $top = $height
        for ($y = 0; $y -lt $saved.Height; $y++) {
            for ($x = 0; $x -lt $saved.Width; $x++) {
                $pixel = $saved.GetPixel($x, $y)
                if ($pixel.A -eq 0) { continue }
                # The background is white; the first black pixel marks where the moved checkerboard starts.
                if ($pixel.A -eq 255 -and $pixel.R -eq 0) { $left = [Math]::Min($left, $x); $top = [Math]::Min($top, $y) }
                if ($pixel.A -eq 255) { $report.OpaquePixels++ }
                if ($pixel.A -ne 255 -or ($pixel.R -ne 0 -and $pixel.R -ne 255)) { $report.InterpolatedPixels++ }
            }
        }
        $report.ImageOrigin = "$left, $top"
    } finally { $saved.Dispose() }
    if ($left -eq 0 -and $top -eq 0) { throw '保存画像で画像の移動を確認できません。' }
    $report.Status = if ($report.InterpolatedPixels -eq 0) { 'whole-pixel' } else { 'interpolated' }
} catch { $failure = $_; $report.Status = 'failed'; $report.Error = $_.Exception.Message }
finally {
    $cleanup = [ordered]@{ Closed = $false; Error = $null }
    if ($process -and -not $process.HasExited -and $script:Handle -eq [IntPtr]::Zero) { $process.Refresh(); $script:Handle = $process.MainWindowHandle }
    if ($process -and -not $process.HasExited -and $script:Handle -ne [IntPtr]::Zero) {
        try {
            if ((Root).Current.Name.StartsWith('*')) { throw '未保存状態が残るため閉じません。' }
            (Root).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
            $cleanup.Closed = $process.WaitForExit(5000)
        } catch { $cleanup.Error = $_.Exception.Message }
    } elseif ($process -and $process.HasExited) { $cleanup.Closed = $true }
    $report.Cleanup = $cleanup
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultPath -Encoding utf8
}
if ($failure) { throw $failure }
[pscustomobject]@{ Status = $report.Status; ProcessId = $report.ProcessId; Origin = $report.ImageOrigin; Opaque = $report.OpaquePixels; Interpolated = $report.InterpolatedPixels; Result = $resultPath; Cleanup = $report.Cleanup } | ConvertTo-Json -Depth 4
