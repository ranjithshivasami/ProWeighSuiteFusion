; ProWeigh Fusion Installer Script for Inno Setup 6
; Per-brand overrides via ISCC /DMyAppName= /DMyAppVersion= /DMyAppId=.
; Defaults = Fusion brand, version mirrors the csproj <Version>.

#ifndef MyAppName
  #define MyAppName "ProWeigh Fusion"
#endif
#ifndef MyAppVersion
  #define MyAppVersion "1.1.6"
#endif
#ifndef MyAppId
  #define MyAppId "{D4E5F6A7-4444-4555-B666-777788889999}"
#endif
#ifndef Brand
  #define Brand "Fusion"
#endif
#ifndef SetupIcon
  #define SetupIcon "..\branding\fusion\Images\app-icon.ico"
#endif
#ifndef MyAppExeName
  #define MyAppExeName "ProWeighSuitePlatforms.exe"
#endif
#ifndef PublishDir
  #define PublishDir "Publish"
#endif
#ifndef InstallerOutputDir
  #define InstallerOutputDir "installer"
#endif
#define MyAppPublisher "Tamil Automation"
#define MyAppURL "https://github.com/ranjithshivasami/ProWeighSuitePlatforms"
#define MyAppDescription "ProWeigh Fusion Weighing Management System"

[Setup]
; Application information
AppId={{#MyAppId}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir={#InstallerOutputDir}
OutputBaseFilename={#MyAppName}_Setup_v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin

; UI Settings
SetupIconFile={#SetupIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}

; Version Information
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppDescription}
VersionInfoCopyright=Copyright (C) 2024 {#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
; Main application file (single-file publish output)
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; Database will be created in %AppData%\ProWeighSuitePlatforms on first run.

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:ProgramOnTheWeb,{#MyAppName}}"; Filename: "{#MyAppURL}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DataDirPage: TInputDirWizardPage;

procedure InitializeWizard;
begin
  { Create custom page for data directory }
  DataDirPage := CreateInputDirPage(wpSelectDir,
    'Select Data Directory', 'Where should application data be stored?',
    'Select the folder where ProWeigh Fusion will store its database and configuration files, then click Next.' + #13#10 + #13#10 +
    'Default location: %AppData%\ProWeighSuitePlatforms',
    False, '');
  DataDirPage.Add('');
  DataDirPage.Values[0] := ExpandConstant('{userappdata}\ProWeighSuitePlatforms');
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  { Application is self-contained - no .NET runtime check needed }
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  DataDir: String;
begin
  if CurStep = ssPostInstall then
  begin
    { Create data directory }
    DataDir := DataDirPage.Values[0];
    if not DirExists(DataDir) then
    begin
      if not CreateDir(DataDir) then
      begin
        MsgBox('Failed to create data directory: ' + DataDir, mbError, MB_OK);
      end;
    end;

    { Database will be initialized automatically on first application run }
    { The DatabaseInitializationService will create and seed the database }
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
  Response: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\ProWeighSuitePlatforms');
    if DirExists(DataDir) then
    begin
      Response := MsgBox('Do you want to remove application data and database?' + #13#10 + #13#10 +
                         'Location: ' + DataDir + #13#10 + #13#10 +
                         'If you plan to reinstall, select NO to keep your data.',
                         mbConfirmation, MB_YESNO);
      if Response = IDYES then
      begin
        if not DelTree(DataDir, True, True, True) then
        begin
          MsgBox('Could not remove all data files. You may need to manually delete: ' + DataDir,
                 mbInformation, MB_OK);
        end;
      end;
    end;
  end;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nProWeigh Fusion is a professional weighing and batch management system with the following features:%n%n* Real-time weight monitoring%n* Batch management%n* User authentication and role-based access%n* Material and recipe management%n* Comprehensive reporting%n* Label printing support%n%nDefault users will be created on first run:%n  - administrator / administrator (Super Admin)%n  - admin / admin (Admin)%n  - operator / operator (Operator)%n%nIt is recommended that you close all other applications before continuing.
