[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath, [Parameter(Mandatory)][string]$SettingsDirectory,
    [Parameter(Mandatory)][string]$ImagePath, [Parameter(Mandatory)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'pwsh -Sta -Fileで実行してください。' }
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/'))
foreach ($path in @($AppPath,$SettingsDirectory,$ImagePath,$OutputDirectory)) {
    if (-not [IO.Path]::GetFullPath($path).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)) { throw '専用検証パスはartifacts配下に限定してください。' }
}
New-Item -ItemType Directory -Path $SettingsDirectory,$OutputDirectory -Force | Out-Null
. "$env:USERPROFILE/.codex/skills/windows-ui-automation/scripts/ui-toolkit.ps1"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing.Common
$previousClipboard = [Windows.Forms.Clipboard]::GetDataObject()
$clipboardChanged = $false
$process = Start-Process -FilePath $AppPath -ArgumentList @('--settings-dir',"`"$SettingsDirectory`"","`"$ImagePath`"") -PassThru
$processCondition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
function Find-TestElement([string]$Id, [switch]$Optional) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$Id)
    $deadline = [DateTime]::UtcNow.AddSeconds($(if($Optional){.2}else{5}))
    do {
        foreach ($surface in [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$processCondition)) {
            foreach ($element in $surface.FindAll([Windows.Automation.TreeScope]::Descendants,$condition)) {
                if (-not $element.Current.IsOffscreen) { return $element }
            }
        }
        Start-Sleep -Milliseconds 80
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $Optional) { throw "対象要素がありません: $Id" }
}
function Invoke-TestElement([string]$Id) {
    Invoke-ElementAction -Element (Find-TestElement $Id) -Action Invoke
    Start-Sleep -Milliseconds 180
}
function Assert-Dimensions([int]$Width,[int]$Height) {
    $name = (Find-TestElement DimensionsButton).Current.Name
    if ($name -notmatch ('^' + $Width + ' × ' + $Height + ' px')) { throw "寸法が一致しません: $name" }
}
$joined = @()
try {
    $fileButton = Find-TestElement FileButton
    if ($fileButton.Current.Name -ne 'ファイル▼' -or $fileButton.Current.AcceleratorKey -ne 'Alt+F') { throw 'ファイルメニューの表示/キーが異なります。' }
    if ((Find-TestElement MoreButton).Current.Name -ne 'その他▼') { throw 'その他の表示が異なります。' }
    Invoke-TestElement FileButton
    foreach ($id in @('NewMenuItem','OpenMenuItem','SaveAsMenuItem')) { $null = Find-TestElement $id }
    Invoke-TestElement OpenMenuItem; Invoke-TestElement DialogCloseButton
    Invoke-TestElement FileButton; Invoke-TestElement SaveAsMenuItem; Invoke-TestElement DialogCloseButton
    Invoke-TestElement MoreButton
    if (Find-TestElement SaveAsMenuItem -Optional) { throw 'その他に別名保存が残っています。' }
    Invoke-TestElement SelectAllMenuItem
    $sourceSize = [regex]::Matches((Find-TestElement DimensionsButton).Current.Name,'\d+')
    $width = [int]$sourceSize[0].Value; $height = [int]$sourceSize[1].Value
    $clipboardChanged = $true
    $externalBitmap = [Drawing.Bitmap]::new($ImagePath)
    try { [Windows.Forms.Clipboard]::SetImage($externalBitmap) } finally { $externalBitmap.Dispose() }
    Invoke-TestElement PasteButton; Invoke-TestElement PasteModeLeft
    Assert-Dimensions ($width*2) $height
    Invoke-TestElement UndoButton; Assert-Dimensions $width $height
    $clipboardChanged = $true; Invoke-TestElement CopyButton
    foreach ($mode in @('Overlay','Top','Bottom','Left','Right')) {
        Invoke-TestElement PasteButton
        foreach ($choice in @('Overlay','Top','Bottom','Left','Right')) { $null = Find-TestElement ('PasteMode' + $choice) }
        Invoke-TestElement ('PasteMode' + $mode)
        $expectedWidth = if($mode -in @('Left','Right')){$width*2}else{$width}
        $expectedHeight = if($mode -in @('Top','Bottom')){$height*2}else{$height}
        Assert-Dimensions $expectedWidth $expectedHeight
        if (-not (Find-TestElement OrderButton).Current.IsEnabled) { throw '貼り付けたオブジェクトが選択されていません。' }
        Invoke-TestElement UndoButton; Assert-Dimensions $width $height
        if ((Find-TestElement UndoButton).Current.IsEnabled) { throw '1回のUndoで戻っていません。' }
        $joined += $mode
    }
    # Opening another flyout dismisses the pending choice without applying a paste.
    Invoke-TestElement PasteButton; $null = Find-TestElement PasteModeOverlay
    Invoke-TestElement FileButton; $null = Find-TestElement OpenMenuItem
    if ((Find-TestElement UndoButton).Current.IsEnabled) { throw '貼り付けを取り消した後に変更が残っています。' }
    Invoke-TestElement OpenMenuItem; Invoke-TestElement DialogCloseButton
    Assert-Dimensions $width $height
    $result = [pscustomobject]@{FileMenu=$true;MoreCaption=$true;SaveAsRemovedFromMore=$true;OpenAndSaveAsCancel=$true;
        Modes=$joined;ExternalImage=$true;SingleUndo=$true;DismissWithoutEdit=$true;PhysicalInputUsed=$false;ProcessId=$process.Id}
} finally {
    try {
    # Cleanup is restricted to this isolated process and its known test edits.
    $pending = Find-TestElement PasteModeOverlay -Optional
    if ($pending) { Invoke-ElementAction -Element $pending -Action Invoke; Start-Sleep -Milliseconds 180 }
    $dialogClose = Find-TestElement DialogCloseButton -Optional
    if ($dialogClose) { Invoke-ElementAction -Element $dialogClose -Action Invoke; Start-Sleep -Milliseconds 180 }
    for ($i=0; $i -lt 8; $i++) {
        $undo = Find-TestElement UndoButton -Optional
        if (-not $undo -or -not $undo.Current.IsEnabled) { break }
        Invoke-ElementAction -Element $undo -Action Invoke; Start-Sleep -Milliseconds 180
    }
    $windows = @([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$processCondition) | Where-Object {$_.Current.Name -like '* - PPPaint - v*'})
    foreach ($window in $windows) { $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close() }
    if (-not $process.WaitForExit(5000)) { throw '専用検証窓が終了していません。' }
    } finally {
        if ($clipboardChanged) {
            if ($previousClipboard) { [Windows.Forms.Clipboard]::SetDataObject($previousClipboard,$true) } else { [Windows.Forms.Clipboard]::Clear() }
        }
    }
}
$result | Add-Member -NotePropertyName ClipboardRestored -NotePropertyValue $true
$result | ConvertTo-Json | Tee-Object -FilePath (Join-Path $OutputDirectory menus-paste-validation.json)
