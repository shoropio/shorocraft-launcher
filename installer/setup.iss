; Inno Setup Script
; Compila con Inno Setup (https://jrsoftware.org/isinfo.php)

[Setup]
AppName=ShoroCraft Launcher
AppVersion={#GetVersionNumbersString('..\src\ShoroCraftLauncher.App\bin\Release\net8.0-windows\win-x64\publish\ShoroCraftLauncher.exe')}
AppPublisher=ShoroCraft
DefaultDirName={localappdata}\ShoroCraftLauncher
DefaultGroupName=ShoroCraft Launcher
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=ShoroCraftLauncher_Setup
SetupIconFile=..\assets\icon.ico
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\src\ShoroCraftLauncher.App\bin\Release\net8.0-windows\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\ShoroCraft Launcher"; Filename: "{app}\ShoroCraftLauncher.exe"
Name: "{userdesktop}\ShoroCraft Launcher"; Filename: "{app}\ShoroCraftLauncher.exe"

[Run]
Filename: "{app}\ShoroCraftLauncher.exe"; Description: "Iniciar ShoroCraft Launcher"; Flags: postinstall nowait skipifsilent
