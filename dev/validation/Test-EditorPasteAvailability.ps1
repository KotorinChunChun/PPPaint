[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath,[Parameter(Mandatory)][string]$SettingsDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA'){throw 'pwsh -Staで実行してください。'}
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts'))+[IO.Path]::DirectorySeparatorChar
foreach($path in @($AppPath,$SettingsDirectory,$OutputDirectory)){
    if(-not [IO.Path]::IsPathFullyQualified($path) -or -not [IO.Path]::GetFullPath($path).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'アプリ・専用設定・出力はartifacts配下の絶対パスで指定してください。'}
}
if((Test-Path -LiteralPath $SettingsDirectory) -and @(Get-ChildItem -LiteralPath $SettingsDirectory -Force).Count){throw '未使用の専用設定を指定してください。'}
New-Item -ItemType Directory -Path $SettingsDirectory,$OutputDirectory -Force|Out-Null
. "$env:USERPROFILE/.codex/skills/windows-ui-automation/scripts/ui-toolkit.ps1"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing.Common
Add-Type @'
using System.Runtime.InteropServices;
public static class PasteTestClipboard { [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber(); }
'@
. (Join-Path $PSScriptRoot 'Clipboard-Snapshot.ps1')
# All formats must be materialized before the first clipboard mutation.
$snapshot=New-EditorClipboardSnapshot
$script:expectedSequence=[PasteTestClipboard]::GetClipboardSequenceNumber()
$report=[ordered]@{Status='running';PhysicalInputUsed=$false;Cases=[Collections.Generic.List[string]]::new();ClipboardRestored=$false;Closed=$false;ProcessId=$null}
$process=$null;$script:testHandle=[IntPtr]::Zero;$changed=$false;$failure=$null
function Root { [Windows.Automation.AutomationElement]::FromHandle($script:testHandle) }
function Element([string]$id,[switch]$Optional){
    $node=(Root).FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
    if(-not $node -and -not $Optional){throw "要素がありません: $id"};return $node
}
function WaitUntil([scriptblock]$probe,[string]$label){
    $deadline=[DateTime]::UtcNow.AddSeconds(12)
    while(-not (&$probe)){if([DateTime]::UtcNow -gt $deadline){throw "待機期限: $label"};Start-Sleep -Milliseconds 100}
}
function AssertSequence {
    if([PasteTestClipboard]::GetClipboardSequenceNumber() -ne $script:expectedSequence){throw '利用者または他アプリがクリップボードを変更したため、上書きせず検証を中止します。'}
}
function ChangeClipboard([scriptblock]$action){
    AssertSequence
    $script:changed=$true
    &$action
    $script:expectedSequence=[PasteTestClipboard]::GetClipboardSequenceNumber()
}
function CheckButtons([bool]$enabled,[string]$label){
    WaitUntil { $paste=Element PasteButton -Optional;$options=Element PasteOptionsButton -Optional;$paste -and $options -and $paste.Current.IsEnabled -eq $enabled -and $options.Current.IsEnabled -eq $enabled } $label
    $report.Cases.Add($label)
}
function InvokeId([string]$id){Invoke-ElementAction -Element (Element $id) -Action Invoke}
try{
    $process=Start-Process -FilePath $AppPath -ArgumentList @('--settings-dir',('"'+$SettingsDirectory+'"')) -WindowStyle Hidden -PassThru
    $report.ProcessId=$process.Id
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
    WaitUntil {$windows=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$condition)|Where-Object {$_.Current.Name -like '* - PPPaint - v*'});if($windows.Count -eq 1){$script:testHandle=[IntPtr]$windows[0].Current.NativeWindowHandle};$script:testHandle -ne [IntPtr]::Zero} '専用窓起動'
    ChangeClipboard { [Windows.Forms.Clipboard]::Clear() };CheckButtons $false 'empty-disabled'
    ChangeClipboard { [Windows.Forms.Clipboard]::SetText('paste availability fixture') };CheckButtons $false 'text-disabled'
    $bitmap=[Drawing.Bitmap]::new(16,12)
    try{
        $graphics=[Drawing.Graphics]::FromImage($bitmap);try{$graphics.Clear([Drawing.Color]::Coral)}finally{$graphics.Dispose()}
        ChangeClipboard { [Windows.Forms.Clipboard]::SetImage($bitmap) }
        CheckButtons $true 'external-image-enabled'
        InvokeId PasteButton
        WaitUntil { (Root).Current.Name.StartsWith('*') -and (Element CopyButton).Current.IsEnabled } '重ね貼り'
        CheckButtons $true 'overlay-completed'
        AssertSequence;InvokeId CopyButton
        WaitUntil {[PasteTestClipboard]::GetClipboardSequenceNumber() -ne $script:expectedSequence} 'アプリ内コピー'
        $script:expectedSequence=[PasteTestClipboard]::GetClipboardSequenceNumber()
        CheckButtons $true 'internal-copy-enabled'
        ChangeClipboard { [Windows.Forms.Clipboard]::SetText('invalidates copied objects') };CheckButtons $false 'stale-internal-copy-disabled'
        ChangeClipboard { [Windows.Forms.Clipboard]::SetImage($bitmap) };CheckButtons $true 'replacement-image-enabled'
        InvokeId PasteOptionsButton
        WaitUntil {Element PasteModeOverlay -Optional} '貼り付け方法メニュー'
        InvokeId PasteModeOverlay
        WaitUntil {-not (Element PasteModeOverlay -Optional)} 'メニューの貼り付け完了'
        CheckButtons $true 'paste-options-completed'
    }finally{$bitmap.Dispose()}
    ChangeClipboard { [Windows.Forms.Clipboard]::Clear() };CheckButtons $false 'cleared-disabled'
    $report.Status='passed'
}catch{
    $failure=$_;$report.Status='failed';$report.Error=$_.Exception.Message;$report.Stack=$_.ScriptStackTrace
    if($script:testHandle -ne [IntPtr]::Zero){
        try{$report.UiAtFailure=@((Root).FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|ForEach-Object {if($_.Current.AutomationId -like 'Dialog*' -or $_.Current.AutomationId -eq 'EditorDialog' -or $_.Current.ControlType -eq [Windows.Automation.ControlType]::Text){[pscustomobject]@{Id=$_.Current.AutomationId;Name=$_.Current.Name}}})}catch{}
    }
}
finally{
    try{
        if($process -and -not $process.HasExited -and $script:testHandle -eq [IntPtr]::Zero){$process.Refresh();$script:testHandle=$process.MainWindowHandle}
        if($process -and -not $process.HasExited -and $script:testHandle -ne [IntPtr]::Zero){
            if(Element EditorDialog -Optional){InvokeId DialogCloseButton;Start-Sleep -Milliseconds 200}
            (Root).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
            if(-not $process.WaitForExit(700)){WaitUntil {Element DialogSecondaryButton -Optional} '専用画像の破棄確認';InvokeId DialogSecondaryButton}
            $report.Closed=$process.WaitForExit(5000)
        }elseif($process){$report.Closed=$process.HasExited}
    }catch{$report.CleanupError=$_.Exception.Message}
    try{
        if($changed){AssertSequence;if($snapshot){[Windows.Forms.Clipboard]::SetDataObject($snapshot,$true)}else{[Windows.Forms.Clipboard]::Clear()}}
        $report.ClipboardRestored=$true
    }catch{$report.ClipboardRestoreError=$_.Exception.Message;$report.Status='failed';if(-not $failure){$failure=$_}}
    if(-not $report.Closed){$report.Status='failed'}
    $report|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $OutputDirectory paste-availability.json) -Encoding utf8
}
$report|ConvertTo-Json -Depth 5
if($failure -or $report.Status -ne 'passed'){throw '貼り付け可否のUIA検証に失敗しました。結果JSONを確認してください。'}
