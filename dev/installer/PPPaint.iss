[Setup]
AppId=KotorinToys.PPPaint
AppName=PPPaint
AppVersion={#ProductVersion}
AppPublisher=KotorinChunChun
AppPublisherURL=https://github.com/KotorinChunChun/PPPaint
DefaultDirName={localappdata}\Programs\PPPaint
DefaultGroupName=PPPaint
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
DisableProgramGroupPage=yes
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\PPPaint.exe
SetupIconFile={#Payload}\Assets\PPPaintIcon.ico
OutputDir={#OutputDirectory}
OutputBaseFilename=PPPaint-{#ProductVersion}-setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
#ifdef DeveloperSigned
SignTool=PPPaintDeveloperSigning
SignedUninstaller=yes
#endif

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{userprograms}\PPPaint"; Filename: "{app}\PPPaint.exe"; WorkingDir: "{app}"

[Code]
function CreateFileW(FileName: String; Access, ShareMode: Cardinal; SecurityAttributes: NativeUInt;
  CreationDisposition, FlagsAndAttributes: Cardinal; TemplateFile: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function CanReplaceInstalledApp(): Boolean;
var
  AppPath: String;
  AppHandle: THandle;
begin
  AppPath := ExpandConstant('{app}\PPPaint.exe');
  Result := not FileExists(AppPath);
  if Result then Exit;
  { 書き込み用に開き、実行中または他プロセスが使っているEXEの置換を避ける。 }
  AppHandle := CreateFileW(AppPath, $40000000, 0, 0, 3, $80, 0);
  Result := AppHandle <> THandle(-1);
  if Result then CloseHandle(AppHandle);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not CanReplaceInstalledApp() then
    Result := 'PPPaintの実行ファイルを更新できません。作業を保存してアプリを終了し、再試行してください。';
end;

function InitializeUninstall(): Boolean;
begin
  Result := CanReplaceInstalledApp();
  if not Result then
  begin
    Log('PPPaintの実行ファイルを削除できません。アプリの終了を確認してください。');
    if not UninstallSilent then
      MsgBox('PPPaintを削除できません。作業を保存してアプリを終了してからやり直してください。', mbError, MB_OK);
  end;
end;
