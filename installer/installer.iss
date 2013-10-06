#define AppExeName "Ec2Manager.exe"
#define AppSrc "..\bin\Release"
#define AppExe AppSrc + "\Ec2Manager.exe"
#define AppName GetStringFileInfo(AppExe, "ProductName")
#define AppVersion GetFileVersion(AppExe)
#define AppPublisher "Ec2Manager"
#define AppURL "https://github.com/canton7/ec2manager"


[Setup]
AppId={{0512E32C-CF12-4633-B836-DA29E0BAB2F5}
AppName={#AppName}
AppVersion={#AppVersion}
;AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={pf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
LicenseFile={#AppSrc}\LICENSE.txt
OutputBaseFilename={#AppName}Setup
SetupIconFile={#AppSrc}\icon.ico
Compression=lzma
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AppSrc}\Ec2Manager.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSrc}\Ec2Manager.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSrc}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSrc}\*.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSrc}\*.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSrc}\*.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "dotNetFx45_Full_setup.exe"; DestDir: {tmp}; Flags: deleteafterinstall; Check: FrameworkIsNotInstalled

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\dotNetFx45_Full_setup.exe"; Parameters: "/passive /promptrestart"; Check: FrameworkIsNotInstalled; StatusMsg: Microsoft Framework 4.5 is being installed. Please wait...
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[code]
function FrameworkIsNotInstalled: Boolean;
var 
  exists: boolean;
  release: cardinal;
begin
  exists := RegQueryDWordValue(HKEY_LOCAL_MACHINE, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', release);
  result := not exists or (release < 378389);
end;

[UninstallDelete]
Type: files; Name: "{userappdata}\{#AppName}\config.xml"
Type: filesandordirs; Name: "{userappdata}\{#AppName}\keys"
Type: dirifempty; Name: "{userappdata}\{#AppName}"