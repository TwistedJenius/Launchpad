; Script generated by the Inno Script Studio Wizard.
; SEE THE DOCUMENTATION FOR DETAILS ON CREATING INNO SETUP SCRIPT FILES!

#define MyAppName "TerrorChasm"
#define MyAppVersion "2.1.1.0"
#define MyAppPublisher "Twisted Jenius LLC"
#define MyAppURL "https://github.com/TwistedJenius/Launchpad"
#define MyAppExeName "Launchpad.exe"
#define GtkSharp "gtk-sharp-2.12.45.msi"

;
; Fill this out with the path to your built launchpad binaries.
;
#define LaunchpadReleaseDir "..\..\Launchpad.Launcher\bin\x64\Release\net462"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{810B3C9B-6CF2-45A9-8557-B8AED56A38EA}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={commonpf64}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=C:\Users\Vicious\Desktop
OutputBaseFilename={#MyAppName}-setup
Compression=lzma2/ultra
SolidCompression=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=admin
VersionInfoVersion={#MyAppVersion}
ShowLanguageDialog=auto

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}";

[Files]
Source: "{#LaunchpadReleaseDir}\Launchpad.exe"; DestDir: "{app}";
Source: "{#LaunchpadReleaseDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs
; Extra libraries - GTK# must be included.
Source: "{#LaunchpadReleaseDir}\{#GtkSharp}"; DestDir: "{tmp}";

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName}\Uninstall"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppExeName}"

[Dirs]
Name: "{app}\"; Permissions: everyone-modify

[Run]
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\{#GtkSharp}"" /qn"; Flags: runascurrentuser;
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: runascurrentuser nowait postinstall skipifsilent
