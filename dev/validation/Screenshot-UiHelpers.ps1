param([Parameter(Mandatory)][int]$TargetProcessId)

$ErrorActionPreference = 'Stop'
. "$env:USERPROFILE/.codex/skills/windows-ui-automation/scripts/ui-toolkit.ps1"
$script:EditorTestHandle = (Get-Process -Id $TargetProcessId -ErrorAction Stop).MainWindowHandle
if ($script:EditorTestHandle -eq 0) { throw '検証対象のウィンドウがありません。' }

function Invoke-TestAction([string]$Id) {
    $element = Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id
    Invoke-ElementAction -Element $element -Action Invoke
    Start-Sleep -Milliseconds 120
}
function Select-TestTool([string]$Tool) {
    $element = Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId "Tool$Tool"
    $pattern = $element.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    if ($pattern.Current.ToggleState -ne [Windows.Automation.ToggleState]::On) { Invoke-ElementAction -Element $element -Action Toggle }
    Start-Sleep -Milliseconds 100
}
function Get-TestImagePoint([double]$X, [double]$Y) {
    $canvas = Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId PaintCanvas
    $dimensions = Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId DimensionsButton
    $numbers = [regex]::Matches($dimensions.Current.Name, '\d+')
    $rect = $canvas.Current.BoundingRectangle
    return ,@(($rect.X + $X * $rect.Width / [double]$numbers[0].Value), ($rect.Y + $Y * $rect.Height / [double]$numbers[1].Value))
}
function Invoke-TestImageClick([double]$X, [double]$Y) {
    $point = Get-TestImagePoint $X $Y
    Invoke-Click -WindowHandle $script:EditorTestHandle -X $point[0] -Y $point[1]
}
function Invoke-TestImageDrag([double]$X1, [double]$Y1, [double]$X2, [double]$Y2) {
    $a = Get-TestImagePoint $X1 $Y1; $b = Get-TestImagePoint $X2 $Y2
    if (-not (Set-ForegroundForced -WindowHandle $script:EditorTestHandle)) { throw '対象を前面化できません。' }
    Assert-PointInWindow -WindowHandle $script:EditorTestHandle -X $a[0] -Y $a[1]
    Assert-PointInWindow -WindowHandle $script:EditorTestHandle -X $b[0] -Y $b[1]
    [WUA.Win]::SetCursorPos([int]$a[0], [int]$a[1])
    [WUA.Win]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    try {
        for ($step = 1; $step -le 15; $step++) {
            $x = $a[0] + ($b[0] - $a[0]) * $step / 15
            $y = $a[1] + ($b[1] - $a[1]) * $step / 15
            Assert-ForegroundIs -WindowHandle $script:EditorTestHandle
            Assert-PointInWindow -WindowHandle $script:EditorTestHandle -X $x -Y $y
            [WUA.Win]::SetCursorPos([int]$x, [int]$y)
            Start-Sleep -Milliseconds 25
        }
    } finally { [WUA.Win]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 180
}
function Save-TestWindow([string]$Path) {
    if (-not (Save-WindowScreenshot -WindowHandle $script:EditorTestHandle -Path $Path)) { throw '対象ウィンドウを撮影できませんでした。' }
}
