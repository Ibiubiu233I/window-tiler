#define AppVersion "0.5.3"
[Setup]
AppId={{30DB49E2-4098-4D88-9D83-087E8709AE99}
AppName=铺窗 Window Tiler
AppVersion={#AppVersion}
AppPublisher=Window Tiler
DefaultDirName={localappdata}\Programs\WindowTiler
DefaultGroupName=铺窗 Window Tiler
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
OutputDir=..\artifacts\installer
OutputBaseFilename=WindowTiler-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\WindowTiler.App\app.ico
UninstallDisplayIcon={app}\WindowTiler.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=铺窗 Window Tiler

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl,ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\铺窗 Window Tiler"; Filename: "{app}\WindowTiler.exe"
Name: "{autodesktop}\铺窗 Window Tiler"; Filename: "{app}\WindowTiler.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\WindowTiler.exe"; Description: "启动铺窗"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "WindowTiler"; Flags: uninsdeletevalue
