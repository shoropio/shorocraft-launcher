; Inno Setup Script
; Compila con Inno Setup (https://jrsoftware.org/isinfo.php)

[Setup]
AppName=ShoroCraft Launcher
AppVersion=1.6.9
AppPublisher=Shoropio Corporation
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
CloseApplications=yes
CloseApplicationsFilter=ShoroCraftLauncher.exe
RestartApplications=yes
; Herramienta de firma definida al compilar con /Ssigntool=...
; ej: ISCC.exe setup.iss "/Ssigntool=C:\...\signtool.exe sign /n $qShoroCraft Launcher$q /s my /fd sha256 /tr http://timestamp.digicert.com /td sha256 $f"
; SignTool=signtool
; SignedUninstaller=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\ShoroCraft Launcher"; Filename: "{app}\ShoroCraftLauncher.exe"
Name: "{userdesktop}\ShoroCraft Launcher"; Filename: "{app}\ShoroCraftLauncher.exe"

[Run]
Filename: "{app}\ShoroCraftLauncher.exe"; Description: "Iniciar ShoroCraft Launcher"; Flags: postinstall nowait skipifsilent
