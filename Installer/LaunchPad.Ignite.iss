#ifndef AppVersion
	#define AppVersion "1.0.9"
#endif
#ifndef SourceDir
  #define SourceDir "..\LaunchPad.Ignite.App\bin\Release\net8.0\win-x64\publish"
#endif
#ifndef OutputDir
  #define OutputDir "artifacts"
#endif
#ifndef OutputName
  #define OutputName "LaunchPad.Ignite-Setup"
#endif

#define AppName "LaunchPad.Ignite"
#define AppPublisher "LaunchPad"
#define AppExeName "LaunchPad.Ignite.App.exe"

[Setup]
AppId={{EFD53A3A-6DA9-4C30-A3D9-2021A2CF3B37}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}.0
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\rocket.ico
SetupIconFile=..\LaunchPad.Ignite.App\Assets\rocket.ico
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible arm64
ArchitecturesInstallIn64BitMode=x64compatible arm64
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LaunchPad.Ignite.App\Assets\rocket.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\rocket.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\rocket.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
