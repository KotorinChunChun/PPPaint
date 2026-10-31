[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$SettingsDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$ConfirmationOnly,
    [switch]$CaptureWindows
)

# Disposable profile only. Captures require the caller to announce exclusive PC use.
$ErrorActionPreference = 'Stop'
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
    if (Test-Path -LiteralPath $path) {
        $item = Get-Item -LiteralPath $path
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'リンク先の設定/出力フォルダーは使いません。' }
    }
}
if ((Test-Path -LiteralPath $SettingsDirectory) -and @(Get-ChildItem -LiteralPath $SettingsDirectory -Force).Count) { throw '空または未作成の専用設定フォルダーを指定してください。' }
New-Item -ItemType Directory -Path $SettingsDirectory,$OutputDirectory -Force | Out-Null
$resultPath = Join-Path $OutputDirectory ('dialog-uia-'+[guid]::NewGuid().ToString('N')+'.json')
. "$env:USERPROFILE/.codex/skills/windows-ui-automation/scripts/ui-toolkit.ps1"
$report = [ordered]@{ AppPath=$AppPath;SettingsDirectory=$SettingsDirectory;ProcessId=$null;Status='running';Layouts=[Collections.Generic.List[object]]::new();Flows=[Collections.Generic.List[object]]::new();MenuShortcuts=[Collections.Generic.List[object]]::new();Limitations=[Collections.Generic.List[string]]::new();PhysicalKeysVerified=$false;ScreenshotsTaken=$false;Cleanup=$null }
$process = $null; $script:EditorTestHandle = [IntPtr]::Zero; $ownUndoCount = 0; $failure = $null
function Window-Root {
    $root = [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle)
    if ($root.Current.ProcessId -ne $process.Id) { throw '検証対象PIDが変わりました。' }
    return $root
}
function Find-Control([string]$Id,[switch]$Optional) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$Id)
    $root = Window-Root
    $element = $root.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
    if ($element -and ($element.Current.IsOffscreen -or $element.Current.BoundingRectangle.IsEmpty)) { $element = $null }
    if (-not $element -and -not $Optional) { throw "表示要素がありません: $Id" }
    return $element
}
function Wait-Dialog([bool]$Visible = $true) {
    $deadline = [DateTime]::UtcNow.AddSeconds(12)
    do {
        $dialog = Find-Control EditorDialog -Optional
        if (($null -ne $dialog) -eq $Visible) { return $dialog }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "ダイアログの表示状態が切り替わりません: $Visible"
}
function Invoke-Id([string]$Id) { Invoke-ElementAction -Element (Find-Control $Id) -Action Invoke; Start-Sleep -Milliseconds 150 }
function Set-Number([string]$Id,[double]$Value) { (Find-Control $Id).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($Value); Start-Sleep -Milliseconds 150 }
function Rect-Data($Rect) { return [ordered]@{X=$Rect.X;Y=$Rect.Y;Width=$Rect.Width;Height=$Rect.Height} }
function Inside($Outer,$Inner) { return -not $Inner.IsEmpty -and $Inner.Width -gt 0 -and $Inner.Height -gt 0 -and $Inner.Left -ge $Outer.Left-1 -and $Inner.Top -ge $Outer.Top-1 -and $Inner.Right -le $Outer.Right+1 -and $Inner.Bottom -le $Outer.Bottom+1 }
function Compact([string]$Text) { return [regex]::Replace($Text,'\s','') }
function Check-Button([string]$Id,[string]$Caption) {
    $button = Find-Control $Id; $bounds = $button.Current.BoundingRectangle
    if (-not (Inside (Window-Root).Current.BoundingRectangle $bounds)) { throw "ボタンがウィンドウからはみ出しています: $Id" }
    $nodes = @($button) + @($button.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition))
    $candidates = [Collections.Generic.List[object]]::new(); $patternEvidence = $null
    foreach ($node in $nodes) {
        $name = $node.Current.Name
        if ($name) { $candidates.Add([pscustomobject]@{Text=$name;Element=$node}) }
        $pattern = $null
        try {
            if (-not $node.TryGetCurrentPattern([Windows.Automation.TextPattern]::Pattern,[ref]$pattern)) { continue }
            $text = $pattern.DocumentRange.GetText(-1)
        } catch { continue }
        if ((Compact $text) -ne (Compact $Caption)) { continue }
        $candidates.Add([pscustomobject]@{Text=$text;Element=$node})
        $last = $null; $method = 'TextPatternFindLastParenthesis'; $rangeProblem = ''
        try { $last = $pattern.DocumentRange.FindText(')',$true,$false) }
        catch { $rangeProblem = 'FindText: ' + $_.Exception.Message }
        if (-not $last) {
            try {
                $last = $pattern.DocumentRange.Clone()
                $offset = $text.LastIndexOf(')')
                $null = $last.MoveEndpointByUnit([Windows.Automation.TextPatternRangeEndpoint]::Start,[Windows.Automation.TextUnit]::Character,$offset)
                $null = $last.MoveEndpointByUnit([Windows.Automation.TextPatternRangeEndpoint]::End,[Windows.Automation.TextUnit]::Character,-($text.Length-$offset-1))
                if ($last.GetText(-1) -ne ')') { $last=$null; $rangeProblem += '; Clone range did not isolate the closing character.' }
                else { $method = 'TextPatternCloneAndCharacterEndpoints' }
            } catch { $last=$null; $rangeProblem += '; Clone/MoveEndpointByUnit: ' + $_.Exception.Message }
        }
        $coordinates = @()
        if ($last) {
            try { $coordinates = @($last.GetBoundingRectangles()) }
            catch { $rangeProblem += '; GetBoundingRectangles: ' + $_.Exception.Message }
        }
        if (-not $coordinates.Count -or $coordinates.Count % 4) {
            $report.Limitations.Add("$Id : 末尾グリフのUIA矩形を取得できません。$rangeProblem")
            continue
        }
        $glyphs = @()
        for ($index=0;$index -lt $coordinates.Count;$index+=4) {
            $glyph = [Windows.Rect]::new($coordinates[$index],$coordinates[$index+1],$coordinates[$index+2],$coordinates[$index+3])
            if (-not (Inside $bounds $glyph)) { throw "閉じ括弧がボタン内に収まりません: $Id" }
            $glyphs += Rect-Data $glyph
        }
        $patternEvidence = [pscustomobject]@{Method=$method;Text=$text;ClosingGlyphRectangles=$glyphs;ClosingGlyphVerified=$true}
        break
    }
    if (-not $patternEvidence) {
        $full = @($candidates | Where-Object { (Compact $_.Text) -eq (Compact $Caption) })
        if (-not $full.Count) { throw "全文のUIAラベルが見つかりません: $Id / $Caption" }
        $contentBounds = $full[0].Element.Current.BoundingRectangle
        if (-not (Inside $bounds $contentBounds)) { throw "全文ラベルの要素がボタンからはみ出しています: $Id" }
        $patternEvidence = [pscustomobject]@{Method='AutomationTextAndElementBoundsOnly';Text=$full[0].Text;TextElementBounds=(Rect-Data $contentBounds);ClosingGlyphVerified=$null}
        $report.Limitations.Add("$Id : TextPatternで末尾グリフを取得できず、全文のUIAラベルと要素矩形だけを確認。")
    }
    return [pscustomobject]@{Id=$Id;Name=$button.Current.Name;ExpectedCaption=$Caption;Bounds=(Rect-Data $bounds);AcceleratorKey=$button.Current.AcceleratorKey;IsEnabled=$button.Current.IsEnabled;TextEvidence=$patternEvidence}
}
function Check-Dialog([string]$Kind,[int]$Width,[hashtable]$Captions) {
    $dialog = Wait-Dialog; $title = $dialog.Current.Name
    if ($title -match 'PPPaint|v\d+\.\d+\.\d+') { throw "見出しにアプリ名/版が重複しています: $title" }
    $buttons = @()
    foreach ($id in @('DialogPrimaryButton','DialogSecondaryButton','DialogCloseButton')) {
        if ($Captions.ContainsKey($id)) { $buttons += Check-Button $id $Captions[$id] }
        elseif (Find-Control $id -Optional) { throw "不要なボタンが表示されています: $Kind / $id" }
    }
    $report.Layouts.Add([pscustomobject]@{Width=$Width;Kind=$Kind;Title=$title;Bounds=(Rect-Data $dialog.Current.BoundingRectangle);Buttons=$buttons})
    if ($CaptureWindows) {
        $capturePath=Join-Path $OutputDirectory ("$Kind-$Width.png")
        if(-not(Save-WindowScreenshot -WindowHandle $script:EditorTestHandle -Path $capturePath)){throw '対象ウィンドウの前面撮影を確認できません。'}
        $report.ScreenshotsTaken=$true
    }
    return $buttons
}
function Cancel-Dialog { Invoke-Id DialogCloseButton; $null = Wait-Dialog $false }
function Read-FileMenuShortcuts([int]$Width) {
    foreach ($spec in @(@('NewMenuItem','Ctrl+N'),@('OpenMenuItem','Ctrl+O'),@('SaveAsMenuItem','Ctrl+Shift+S'))) {
        $item = Find-Control $spec[0]
        $texts = @($item.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Text)) | ForEach-Object {$_.Current.Name})
        $report.MenuShortcuts.Add([pscustomobject]@{Width=$Width;Id=$spec[0];AcceleratorKey=$item.Current.AcceleratorKey;Texts=$texts;ShortcutTextFound=($texts -contains $spec[1])})
    }
}
try {
    $process = Start-Process -FilePath $AppPath -ArgumentList @('--settings-dir',('"'+$SettingsDirectory+'"')) -PassThru
    $report.ProcessId = $process.Id
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
    $deadline = [DateTime]::UtcNow.AddSeconds(15); $windows = @()
    do {
        if ($process.HasExited) { throw '専用プロセスが終了しました。既存のプロファイルへ転送された可能性があります。' }
        $windows = @([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$condition) | Where-Object { $_.Current.Name -like '* - PPPaint - v*' -and $_.Current.BoundingRectangle.Width -gt 100 })
        if (-not $windows.Count) { Start-Sleep -Milliseconds 100 }
    } while (-not $windows.Count -and [DateTime]::UtcNow -lt $deadline)
    if ($windows.Count -ne 1) { throw '専用編集ウィンドウを一意に特定できません。' }
    . (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $process.Id
    $initialDimensions = (Find-Control DimensionsButton).Current.Name
    $initialTitle = (Window-Root).Current.Name
    if ($initialTitle.StartsWith('*')) { throw '新しい専用ウィンドウが既に未保存です。' }
    $transform = $null; $canResize = (Window-Root).TryGetCurrentPattern([Windows.Automation.TransformPattern]::Pattern,[ref]$transform) -and $transform.Current.CanResize
    $widths = if ($canResize) { @(1280,920) } else { @([int](Window-Root).Current.BoundingRectangle.Width) }
    if (-not $canResize) { $report.Limitations.Add('TransformPatternによる幅1280/920の比較は未実施。現在幅だけを確認。') }
    foreach ($width in $widths) {
        if ($canResize) { $transform.Resize($width,900); Start-Sleep -Milliseconds 200 }
        # Test the reported three-button confirmation first; -ConfirmationOnly is a short preflight.
        Invoke-Id DimensionsButton; $null = Wait-Dialog; Set-Number DialogImageWidth 1010
        Invoke-Id DialogPrimaryButton; $null = Wait-Dialog $false; $ownUndoCount++
        if (-not (Window-Root).Current.Name.StartsWith('*')) { throw 'キャンバス変更で未保存になりません。' }
        (Window-Root).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
        $buttons = @(Check-Dialog 'UnsavedConfirmation' $width @{DialogPrimaryButton='保存 (S)';DialogSecondaryButton='保存せずに閉じる (N)';DialogCloseButton='キャンセル (C)'})
        $secondary = Find-Control DialogSecondaryButton; $close = Find-Control DialogCloseButton
        $keys = @($secondary.Current.AcceleratorKey -split ',\s*')
        if ('N' -notin $keys -or 'Alt+N' -notin $keys) { throw '保存せずに閉じるのN/Alt+Nが公開されていません。' }
        if (@($close.Current.AcceleratorKey -split ',\s*') -notcontains 'Enter') { throw '既定操作がキャンセルではありません。' }
        $focused = [Windows.Automation.AutomationElement]::FocusedElement; $focusVerified = $null
        if ($close.Current.HasKeyboardFocus) { $focusVerified = $true }
        elseif ($focused -and $focused.Current.ProcessId -eq $process.Id) { throw '既定フォーカスがキャンセルにありません。' }
        else { $report.Limitations.Add('対象プロセスの実フォーカスをUIAで確認できず、CancelのEnter既定メタデータだけを確認。') }
        Cancel-Dialog; Invoke-Id UndoButton; $ownUndoCount--
        if ((Find-Control DimensionsButton).Current.Name -ne $initialDimensions -or (Window-Root).Current.Name -ne $initialTitle) { throw '取消/Undoで元の画像状態へ戻りません。' }
        $report.Flows.Add([pscustomobject]@{Width=$width;UnsavedDialogOpened=$true;NAndAltNMetadata=$true;CancelIsEnterDefault=$true;CancelFocused=$focusVerified;CancelledAndUndoRestored=$true;PhysicalNKeyVerified=$false})
        if ($ConfirmationOnly) { continue }
        foreach ($spec in @(@('CanvasResize','DimensionsButton','適用 (S)','キャンセル (C)'),@('Background','CanvasBackgroundButton','適用 (S)','キャンセル (C)'),@('Color','CustomColorButton','適用 (S)','キャンセル (C)'),@('Settings','SettingsButton','','閉じる (C)'))) {
            Invoke-Id $spec[1]; $captions = @{DialogCloseButton=$spec[3]}; if ($spec[2]) { $captions.DialogPrimaryButton=$spec[2] }
            $null = Check-Dialog $spec[0] $width $captions; Cancel-Dialog
        }
        Invoke-Id FileButton; Read-FileMenuShortcuts $width; Invoke-Id OpenMenuItem
        $null = Check-Dialog 'FileOpen' $width @{DialogPrimaryButton='開く (S)';DialogCloseButton='キャンセル (C)'}; Cancel-Dialog
        Invoke-Id DimensionsButton; $null = Wait-Dialog; Set-Number DialogImageWidth 1.5
        if ((Find-Control DialogPrimaryButton).Current.IsEnabled) { throw '小数幅の適用が無効になっていません。' }
        Cancel-Dialog
        if ((Find-Control DimensionsButton).Current.Name -ne $initialDimensions -or (Window-Root).Current.Name -ne $initialTitle) { throw '不正寸法の取消で画像が変わりました。' }
        $report.Flows.Add([pscustomobject]@{Width=$width;FractionalWidthRejected=$true;CancelPreservedDocument=$true})
    }
    $report.Status = if ($report.Limitations.Count) { 'completed-with-limitations' } else { 'verified-uia-layout-and-flow' }
} catch { $failure = $_; $report.Status='failed'; $report.Error=$_.Exception.Message }
finally {
    $cleanup = [ordered]@{CancelledDialog=$false;OwnEditsUndone=0;Closed=$false;Error=$null}
    if ($process -and -not $process.HasExited -and $script:EditorTestHandle -ne [IntPtr]::Zero) {
        try {
            if (Find-Control EditorDialog -Optional) { Cancel-Dialog; $cleanup.CancelledDialog=$true }
            while ($ownUndoCount -gt 0) {
                if (-not (Find-Control UndoButton).Current.IsEnabled) { throw '検証用変更をUndoできません。' }
                Invoke-Id UndoButton; $ownUndoCount--; $cleanup.OwnEditsUndone++
            }
            if ((Window-Root).Current.Name.StartsWith('*')) { throw '未保存状態が残るため強制終了しません。' }
            (Window-Root).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
            $cleanup.Closed=$process.WaitForExit(3000)
        } catch { $cleanup.Error=$_.Exception.Message }
    } elseif ($process -and $process.HasExited) { $cleanup.Closed=$true }
    if ($process -and -not $cleanup.Closed -and -not $failure) {
        $failure = [InvalidOperationException]::new('専用検証ウィンドウの安全な終了を確認できません。')
        $report.Status='failed-cleanup'; $report.Error=$failure.Message
    }
    $report.Cleanup=$cleanup
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $resultPath -Encoding utf8
}
if ($failure) { throw $failure }
[pscustomobject]@{Status=$report.Status;ProcessId=$report.ProcessId;DialogLayouts=$report.Layouts.Count;Limitations=$report.Limitations.Count;Result=$resultPath;PhysicalKeysVerified=$false;Cleanup=$report.Cleanup} | ConvertTo-Json -Depth 4
