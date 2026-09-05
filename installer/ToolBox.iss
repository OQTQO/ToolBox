#ifndef MyAppVersion
  #define MyAppVersion "0.6.0"
#endif
#ifndef HostPublishDir
  #define HostPublishDir "..\artifacts\installer\host"
#endif
#ifndef WorkerPublishDir
  #define WorkerPublishDir "..\artifacts\installer\worker"
#endif

[Setup]
AppId={{B8E2B2E0-6E7E-4A72-9B1F-3D6DB5D57B44}
AppName=ToolBox
AppVersion={#MyAppVersion}
AppVerName=ToolBox {#MyAppVersion}
AppPublisher=ToolBox
AppPublisherURL=https://github.com/OQTQO/ToolBox
DefaultDirName={code:GetDefaultInstallDir}
DefaultGroupName=ToolBox
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
OutputBaseFilename=ToolBox-Setup-v{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\ToolBox.Host\Assets\ToolBox.ico
UninstallDisplayIcon={app}\ToolBox.Host.exe
VersionInfoDescription=ToolBox Host
VersionInfoProductName=ToolBox

[Files]
Source: "{#HostPublishDir}\ToolBox.Host.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#WorkerPublishDir}\ToolBox.PluginWorker.exe"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{app}\Data"; Flags: uninsneveruninstall
Name: "{app}\Data\Plugins"; Flags: uninsneveruninstall
Name: "{app}\Data\PluginData"; Flags: uninsneveruninstall
Name: "{app}\Data\Logs"; Flags: uninsneveruninstall

[Icons]
Name: "{code:GetStartMenuDir}\ToolBox"; Filename: "{app}\ToolBox.Host.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\ToolBox.Host.exe"; Description: "启动 ToolBox"; WorkingDir: "{app}"; Flags: postinstall nowait skipifsilent

[Code]
function GetDefaultInstallDir(Param: String): String;
var
  BasePath: String;
begin
  BasePath := GetEnv('LOCALAPPDATA');
  if BasePath = '' then
    BasePath := AddBackslash(GetEnv('USERPROFILE')) + 'AppData\Local';
  if BasePath = '' then
    BasePath := ExpandConstant('{tmp}');
  Result := AddBackslash(BasePath) + 'Programs\ToolBox';
end;

function GetStartMenuDir(Param: String): String;
var
  BasePath: String;
begin
  BasePath := GetEnv('APPDATA');
  if BasePath = '' then
    BasePath := GetDefaultInstallDir('');
  Result := AddBackslash(BasePath) + 'Microsoft\Windows\Start Menu\Programs';
end;
