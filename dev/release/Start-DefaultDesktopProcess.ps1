param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$Arguments)
$ErrorActionPreference='Stop'
if(-not ('PPPaintDefaultDesktopProcess' -as [type])) {
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class PPPaintDefaultDesktopProcess {
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct Startup {
  public int cb; public string reserved; public string desktop; public string title;
  public int x,y,xSize,ySize,xChars,yChars,fill,flags; public short show,reservedSize;
  public IntPtr reservedData,input,output,error;
 }
 [StructLayout(LayoutKind.Sequential)] public struct ProcessInfo { public IntPtr process,thread; public int pid,tid; }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
 public static extern bool CreateProcess(string application,StringBuilder command,IntPtr processAttrs,IntPtr threadAttrs,bool inherit,int flags,IntPtr environment,string directory,ref Startup startup,out ProcessInfo info);
 [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
}
'@
}
$exe=(Resolve-Path -LiteralPath $Executable).Path
$startup=[PPPaintDefaultDesktopProcess+Startup]::new()
$startup.cb=[Runtime.InteropServices.Marshal]::SizeOf($startup)
$startup.desktop='winsta0\default';$startup.flags=1;$startup.show=0
$info=[PPPaintDefaultDesktopProcess+ProcessInfo]::new()
$command=[Text.StringBuilder]::new(('"'+$exe+'" '+$Arguments))
if(-not [PPPaintDefaultDesktopProcess]::CreateProcess($exe,$command,[IntPtr]::Zero,[IntPtr]::Zero,$false,0x08000000,[IntPtr]::Zero,$PWD.Path,[ref]$startup,[ref]$info)){throw [ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error())}
[PPPaintDefaultDesktopProcess]::CloseHandle($info.thread)|Out-Null
[PPPaintDefaultDesktopProcess]::CloseHandle($info.process)|Out-Null
$info.pid
