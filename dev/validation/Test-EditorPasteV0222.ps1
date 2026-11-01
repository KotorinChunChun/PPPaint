[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$SettingsDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)

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
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing.Common
$runId = [guid]::NewGuid().ToString('N')
$resultPath = Join-Path $OutputDirectory ("paste-uia-$runId.json")
$report = [ordered]@{
    AppPath=$AppPath;SettingsDirectory=$SettingsDirectory;Status='running';PhysicalInputUsed=$false
    CtrlVPhysicalKeyVerified=$false;CtrlShiftVPhysicalKeyVerified=$false;ClipboardRestored=$false
    Scenarios=[Collections.Generic.List[object]]::new();Limitations=@('UIAで貼り付け主操作と▼メニューを確認する。Ctrl+V/Ctrl+Shift+Vの実キー入力・画素色の一致はこの試験では検証しない。')
}
$script:TestProcess = $null; $script:MainHandle = [IntPtr]::Zero
. (Join-Path $PSScriptRoot 'Clipboard-Snapshot.ps1')
$previousClipboard = New-EditorClipboardSnapshot
$clipboardChanged = $false; $failure = $null

function New-Fixture([string]$Label,[int]$Width,[int]$Height) {
    $path = Join-Path $OutputDirectory ("paste-$Label-$runId.png")
    $bitmap = [Drawing.Bitmap]::new($Width,$Height,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.FillRectangle([Drawing.Brushes]::Coral,0,0,[Math]::Max(1,$Width-3),[Math]::Max(1,$Height-3))
            $graphics.FillRectangle([Drawing.Brushes]::Navy,0,0,$Width,1)
            $graphics.FillRectangle([Drawing.Brushes]::Teal,$Width-1,0,1,$Height)
        } finally { $graphics.Dispose() }
        $bitmap.Save($path,[Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
    return $path
}
function Set-FixtureClipboard([string]$Path) {
    $bitmap = [Drawing.Bitmap]::new($Path)
    try { [Windows.Forms.Clipboard]::SetImage($bitmap); $script:clipboardChanged=$true }
    finally { $bitmap.Dispose() }
}
function Window-Root {
    $root=[Windows.Automation.AutomationElement]::FromHandle($script:MainHandle)
    if ($root.Current.ProcessId -ne $script:TestProcess.Id) { throw '検証対象PIDが変わりました。' }
    return $root
}
function Find-Control([string]$Id,[switch]$Optional) {
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$Id)
    $processCondition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$script:TestProcess.Id)
    $deadline=[DateTime]::UtcNow.AddSeconds($(if($Optional){.15}else{5}))
    do {
        foreach ($surface in [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$processCondition)) {
            foreach ($element in $surface.FindAll([Windows.Automation.TreeScope]::Descendants,$condition)) {
                if (-not $element.Current.IsOffscreen -and -not $element.Current.BoundingRectangle.IsEmpty) { return $element }
            }
        }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $Optional) { throw "表示要素がありません: $Id" }
}
function Invoke-Id([string]$Id) { Invoke-ElementAction -Element (Find-Control $Id) -Action Invoke; Start-Sleep -Milliseconds 100 }
function Wait-Dimensions([int]$Width,[int]$Height,[bool]$Edited) {
    $deadline=[DateTime]::UtcNow.AddSeconds(12)
    do {
        $name=(Find-Control DimensionsButton).Current.Name
        $undo=(Find-Control UndoButton).Current.IsEnabled
        $matches=$name -match ('^'+$Width+'\s*×\s*'+$Height+'\s*px')
        if ($matches -and $undo -eq $Edited) {
            if (-not $Edited -or (Find-Control OrderButton).Current.IsEnabled) { return }
        }
        Start-Sleep -Milliseconds 80
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "寸法/履歴が一致しません: expected=$Width × $Height, edited=$Edited, actual=$name, undo=$undo"
}
function Assert-NoPasteMenu {
    if (Find-Control PasteModeOverlay -Optional) { throw '通常の貼り付け主操作で方式メニューが表示されています。' }
}
function Read-Button([string]$Id) {
    $button=Find-Control $Id
    $texts=@($button.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Text)) | ForEach-Object { $_.Current.Name })
    return [pscustomobject]@{Id=$Id;Name=$button.Current.Name;Texts=$texts;AcceleratorKey=$button.Current.AcceleratorKey;HelpText=$button.Current.HelpText}
}
function Check-Buttons {
    $buttons=@()
    foreach ($id in @('FileButton','MoreButton','OrderButton','PasteOptionsButton')) {
        $button=Read-Button $id
        if ($button.Name -notmatch '▼' -and -not @($button.Texts | Where-Object {$_ -match '▼'}).Count) { throw "サブメニュー用の▼がUIA表示テキストにありません: $id" }
        $buttons+=$button
    }
    $main=Read-Button PasteButton
    if ($main.Name -ne '貼り付け' -or $main.AcceleratorKey -ne 'Ctrl+V' -or $main.HelpText -notmatch '重ねて') { throw '貼り付け主操作の名前/キー/説明が一致しません。' }
    $options=$buttons | Where-Object Id -eq PasteOptionsButton
    if ($options.AcceleratorKey -ne 'Ctrl+Shift+V' -or $options.HelpText -notmatch '重ねる・上・下・左・右') { throw '貼り付け方法のキー/説明が一致しません。' }
    return ,@($buttons+$main)
}
function Undo-Paste([int]$Width,[int]$Height) {
    Invoke-Id UndoButton; Wait-Dimensions $Width $Height $false
    if ((Window-Root).Current.Name.StartsWith('*')) { throw 'Undo後に未保存変更が残っています。' }
}
function Start-Scenario([string]$Profile,[string]$ImagePath,[int]$Width,[int]$Height) {
    $arguments=@('--settings-dir',('"'+$Profile+'"'))
    if ($ImagePath) { $arguments+=('"'+$ImagePath+'"') }
    $script:TestProcess=Start-Process -FilePath $AppPath -ArgumentList $arguments -PassThru
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$script:TestProcess.Id)
    $deadline=[DateTime]::UtcNow.AddSeconds(20);$windows=@()
    do {
        if ($script:TestProcess.HasExited) { throw '専用プロセスが終了しました。' }
        $windows=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$condition) | Where-Object {$_.Current.Name -like '* - PPPaint - v*' -and $_.Current.BoundingRectangle.Width -gt 100})
        if (-not $windows.Count) { Start-Sleep -Milliseconds 100 }
    } while (-not $windows.Count -and [DateTime]::UtcNow -lt $deadline)
    if ($windows.Count -ne 1) { throw '専用編集ウィンドウを一意に特定できません。' }
    $script:MainHandle=[IntPtr]$windows[0].Current.NativeWindowHandle
    Wait-Dimensions $Width $Height $false
}
function Close-Scenario {
    $cleanup=[ordered]@{ProcessId=$(if($script:TestProcess){$script:TestProcess.Id}else{$null});CancelledDialog=$false;OwnEditsUndone=0;Closed=$false;Error=$null}
    if (-not $script:TestProcess) { $cleanup.Closed=$true;return $cleanup }
    if ($script:TestProcess.HasExited) { $cleanup.Closed=$true;return $cleanup }
    if ($script:MainHandle -eq [IntPtr]::Zero) { $cleanup.Error='専用ウィンドウを特定できていないため終了要求を送りません。';return $cleanup }
    try {
        # Resolving an already-open paste choice is reversible in this disposable document.
        if (Find-Control PasteModeOverlay -Optional) { Invoke-Id PasteModeOverlay; Start-Sleep -Milliseconds 150 }
        if (Find-Control DialogCloseButton -Optional) { Invoke-Id DialogCloseButton;$cleanup.CancelledDialog=$true }
        for ($index=0;$index -lt 85;$index++) {
            if (-not (Find-Control UndoButton).Current.IsEnabled) { break }
            Invoke-Id UndoButton;$cleanup.OwnEditsUndone++
        }
        if ((Find-Control UndoButton).Current.IsEnabled -or (Window-Root).Current.Name.StartsWith('*')) { throw '未保存状態が残るため強制終了しません。' }
        (Window-Root).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
        $cleanup.Closed=$script:TestProcess.WaitForExit(5000)
        if (-not $cleanup.Closed) { throw '専用プロセスの終了を確認できません。' }
    } catch { $cleanup.Error=$_.Exception.Message }
    return $cleanup
}

try {
    $smallFixture=New-Fixture small 80 50
    $largeFixture=New-Fixture large 1200 700
    $loadedFixture=New-Fixture loaded 240 160
    foreach ($spec in @(@('new','',1000,650),@('loaded',$loadedFixture,240,160))) {
        $scenario=[ordered]@{Name=$spec[0];ProcessId=$null;BaseWidth=$spec[2];BaseHeight=$spec[3];Buttons=@();Checks=[Collections.Generic.List[object]]::new();Cleanup=$null}
        $report.Scenarios.Add($scenario)
        try {
            Start-Scenario (Join-Path $SettingsDirectory $spec[0]) $spec[1] $spec[2] $spec[3]
            $scenario.ProcessId=$script:TestProcess.Id
            $scenario.Buttons=Check-Buttons
            Set-FixtureClipboard $smallFixture
            Invoke-Id PasteButton;Wait-Dimensions $spec[2] $spec[3] $true;Assert-NoPasteMenu
            $scenario.Checks.Add([pscustomobject]@{Command='PasteButton';Source='small';Width=$spec[2];Height=$spec[3];NoMenu=$true;NoShrink=$true})
            Undo-Paste $spec[2] $spec[3]
            Set-FixtureClipboard $largeFixture
            Invoke-Id PasteButton;Wait-Dimensions 1200 700 $true;Assert-NoPasteMenu
            $scenario.Checks.Add([pscustomobject]@{Command='PasteButton';Source='large';Width=1200;Height=700;NoMenu=$true;Expanded=$true})
            Undo-Paste $spec[2] $spec[3]
            Set-FixtureClipboard $smallFixture
            foreach ($mode in @('Overlay','Top','Bottom','Left','Right')) {
                Invoke-Id PasteOptionsButton
                foreach ($choice in @('Overlay','Top','Bottom','Left','Right')) { $null=Find-Control ('PasteMode'+$choice) }
                Invoke-Id ('PasteMode'+$mode)
                $width=if($mode -in @('Left','Right')){$spec[2]+80}else{$spec[2]}
                $height=if($mode -in @('Top','Bottom')){$spec[3]+50}else{$spec[3]}
                Wait-Dimensions $width $height $true;Assert-NoPasteMenu
                $scenario.Checks.Add([pscustomobject]@{Command='PasteOptionsButton';Mode=$mode;AllFiveChoices=$true;Width=$width;Height=$height})
                Undo-Paste $spec[2] $spec[3]
            }
        } finally {
            $scenario.Cleanup=Close-Scenario
            if (-not $scenario.Cleanup.Closed) { throw ('専用ウィンドウの後片付けに失敗: '+$scenario.Cleanup.Error) }
            $script:TestProcess=$null;$script:MainHandle=[IntPtr]::Zero
        }
    }
    $report.Status='verified-uia-paste-buttons-and-menus'
} catch { $failure=$_;$report.Status='failed';$report.Error=$_.Exception.Message }
finally {
    try {
        if ($clipboardChanged) {
            if ($previousClipboard) { [Windows.Forms.Clipboard]::SetDataObject($previousClipboard,$true) }
            else { [Windows.Forms.Clipboard]::Clear() }
        }
        $report.ClipboardRestored=$true
    } catch {
        $report.ClipboardRestoreError=$_.Exception.Message
        $report.Status='failed-clipboard-restore'
        if (-not $failure) { $failure=$_ }
    }
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $resultPath -Encoding utf8
}
if ($failure) { throw $failure }
[pscustomobject]@{Status=$report.Status;Scenarios=$report.Scenarios.Count;Checks=($report.Scenarios.Checks.Count | Measure-Object -Sum).Sum;ClipboardRestored=$report.ClipboardRestored;PhysicalInputUsed=$false;Result=$resultPath} | ConvertTo-Json
