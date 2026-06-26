; WallFlow Installer - Inno Setup Script
#define MyAppName "WallFlow"
#define MyAppVersion "1.0"
#define MyAppPublisher "WallFlow"
#define MyAppURL ""
#define MyAppExeName "WallFlow.exe"

[Setup]
AppId={{B4F5C9E1-3D2A-4B6C-8F7E-9A1B2C3D4E5F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
SetupIconFile=WallFlow\logofondo.ico
OutputDir=installer
OutputBaseFilename=WallFlow-Setup
Compression=lzma
SolidCompression=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableWelcomePage=no
DisableFinishedPage=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "&Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Ejecutar {#MyAppName}"; Flags: postinstall nowait skipifsilent shellexec
