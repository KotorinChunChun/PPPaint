[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$TargetProcessId,
    [ValidateSet('Dump','Invoke','Expand','SetValue','Click','RightClick','Drag','Keys','Screenshot')][string]$Action='Dump',
    [string]$Name='', [string]$AutomationId='', [string]$Value='',
    [int]$X=0,[int]$Y=0,[int]$EndX=0,[int]$EndY=0,
    [string]$OutputPath=''
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms,System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PPPaintUiProbe {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
}
'@
$process=Get-Process -Id $TargetProcessId
if($process.ProcessName -ne 'PPPaint' -or $process.MainWindowHandle -eq 0){throw '対象は起動済みPPPaintに限定します。'}
$root=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if($Action -eq 'Screenshot'){
    $rect=$root.Current.BoundingRectangle
    $bitmap=[System.Drawing.Bitmap]::new([int]$rect.Width,[int]$rect.Height)
    $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen([int]$rect.X,[int]$rect.Y,0,0,$bitmap.Size); $bitmap.Save($OutputPath,[System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    Write-Output $OutputPath
    return
}
if($Action -in @('Click','RightClick','Drag','Keys')){
    [PPPaintUiProbe]::SetForegroundWindow($process.MainWindowHandle)|Out-Null
    if($Action -eq 'Keys'){[System.Windows.Forms.SendKeys]::SendWait($Value)}
    else{
        $rect=$root.Current.BoundingRectangle
        if($X -lt $rect.X -or $Y -lt $rect.Y -or $X -ge $rect.Right -or $Y -ge $rect.Bottom){throw '座標は対象ウィンドウの実測範囲内に限定します。'}
        [PPPaintUiProbe]::SetCursorPos($X,$Y)|Out-Null
        [PPPaintUiProbe]::mouse_event($(if($Action -eq 'RightClick'){8}else{2}),0,0,0,[UIntPtr]::Zero)
        if($Action -eq 'Drag'){
            if($EndX -lt $rect.X -or $EndY -lt $rect.Y -or $EndX -ge $rect.Right -or $EndY -ge $rect.Bottom){throw '終点が対象外です。'}
            for($i=1;$i -le 20;$i++){[PPPaintUiProbe]::SetCursorPos([int]($X+($EndX-$X)*$i/20),[int]($Y+($EndY-$Y)*$i/20))|Out-Null;Start-Sleep -Milliseconds 15}
        }
        [PPPaintUiProbe]::mouse_event($(if($Action -eq 'RightClick'){16}else{4}),0,0,0,[UIntPtr]::Zero)
    }
} elseif($Action -ne 'Dump'){
    $condition=if($AutomationId){[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$AutomationId)}else{[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$Name)}
    $matches=@($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition) | Where-Object {
        $pattern= switch($Action){ 'Invoke' {[System.Windows.Automation.InvokePattern]::Pattern} 'Expand' {[System.Windows.Automation.ExpandCollapsePattern]::Pattern} 'SetValue' {[System.Windows.Automation.ValuePattern]::Pattern} }
        $supported=$null
        -not $_.Current.IsOffscreen -and $_.TryGetCurrentPattern($pattern,[ref]$supported)
    })
    if($matches.Count -ne 1){throw "UI要素が一意でありません: $($matches.Count)"}
    $element=$matches[0]
    switch($Action){
        'Invoke' { ([System.Windows.Automation.InvokePattern]$element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke() }
        'Expand' { ([System.Windows.Automation.ExpandCollapsePattern]$element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand() }
        'SetValue' { ([System.Windows.Automation.ValuePattern]$element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).SetValue($Value) }
    }
}
Start-Sleep -Milliseconds 200
$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) |
    Where-Object { $_.Current.Name -and -not $_.Current.IsOffscreen } |
    ForEach-Object { [pscustomobject]@{Name=$_.Current.Name;Id=$_.Current.AutomationId;Type=$_.Current.ControlType.ProgrammaticName;Enabled=$_.Current.IsEnabled;Rect=$_.Current.BoundingRectangle.ToString()} } |
    ConvertTo-Json -Depth 3
