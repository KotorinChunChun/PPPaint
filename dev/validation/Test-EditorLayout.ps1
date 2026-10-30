[CmdletBinding()]
param([Parameter(Mandatory)][int]$TargetProcessId)

# Run only against a disposable window launched with --settings-dir. No real mouse/keyboard is used.
. (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $TargetProcessId
$rootElement = [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle)
$tools = @($rootElement.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.AutomationId -like 'Tool*' -and $_.Current.ControlType -eq [Windows.Automation.ControlType]::Button })
if ($tools.Count -lt 13) { throw '編集ツールが不足しています。' }
$keys = @($tools | ForEach-Object { $_.Current.AcceleratorKey })
if (@($keys | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0 -or @($keys | Select-Object -Unique).Count -ne $keys.Count) { throw 'ツールキーが未設定または重複しています。' }
foreach ($tool in $tools) {
    if (-not $tool.Current.HelpText.Contains($tool.Current.AcceleratorKey)) { throw 'ツールチップ用のキー情報がありません。' }
}
$canvas = (Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId PaintCanvas).Current.BoundingRectangle
$edges = @()
foreach ($id in @('CanvasLeftGrip', 'CanvasTopGrip', 'CanvasRightGrip', 'CanvasBottomGrip')) {
    $element = Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $id
    $rect = $element.Current.BoundingRectangle
    if ($rect.IsEmpty -or $rect.Width -le 0 -or $rect.Height -le 0 -or $element.Current.IsOffscreen) { throw "キャンバス境界が表示されていません: $id" }
    if ($id -in @('CanvasLeftGrip', 'CanvasRightGrip') -and [Math]::Abs($rect.Height - $canvas.Height) -gt 2) { throw "縦の境界が画像の高さに合いません: $id" }
    if ($id -in @('CanvasTopGrip', 'CanvasBottomGrip') -and [Math]::Abs($rect.Width - $canvas.Width) -gt 2) { throw "横の境界が画像の幅に合いません: $id" }
    $edges += [pscustomobject]@{ Id = $id; Bounds = $rect.ToString() }
}
Select-TestTool Blur
$strength = Find-InWindow -WindowHandle $script:EditorTestHandle -Name 'ぼかしの強さ 最小1 最大64'
if ($strength.Current.IsOffscreen -or -not $strength.Current.IsEnabled) { throw 'ぼかしの強さを操作できません。' }
[pscustomobject]@{ ToolCount = $tools.Count; UniqueToolKeys = $keys.Count; CanvasEdges = $edges; BlurOptionsVisible = $true; PhysicalInputTested = $false } | ConvertTo-Json -Depth 4
