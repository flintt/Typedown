; MyAppName, MyAppPublisher, MyAppExeName, MyAppId: the edition's names.
#include "brand.iss"
#ifndef MyArch
  #define MyArch "x64"
#endif
; A trial build passes its own (/DMyAppVersion=1.4.0.13, build-local.ps1 -TrialVersion) with /DMyFileVersion.
#ifndef MyAppVersion
  #define MyAppVersion "1.4.1"
#endif
#ifndef MyFileVersion
  #define MyFileVersion MyAppVersion + ".0"
#endif
#define MyAppAssocName MyAppName + " Markdown"
#define MyAppAssocKey MyAppName + ".Markdown"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename={#MyAppName}-windows-{#MyArch}-v{#MyAppVersion}
SetupIconFile=..\..\Dev\Typedown\Assets\logo.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
#if MyArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
PrivilegesRequired=admin
ChangesAssociations=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
; The app outlives its window when "keep running in the background" is on, and a window is what the
; restart manager sends its close request to — so it finds the app, asks it to close, and nothing
; happens. Checking the mutex says so before a single file is touched, instead of failing partway
; through with a list of files in use and an uninstall that then cannot finish either.
; Config.InstanceName + ".Mutex" (Dev/Typedown/App.cs)
AppMutex={#MyAppName}.App.Mutex
RestartApplications=no
; Windows 10 1903 or later, as the app (Typedown.csproj TargetPlatformMinVersion) and the MSIX require.
MinVersion=10.0.18362
VersionInfoVersion={#MyFileVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.FileAssociations=File associations:
english.AssocMarkdown=Open Markdown files (.md, .markdown, ...) with %1
english.AssocDefaultApps=Windows keeps the app you chose for Markdown files before. To open them with %1, choose it in Settings > Default apps.%n%nOpen Default apps now?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
; Optional (a reader: an installer taking over .md unasked is unwelcome, mdview #75, #283); checked, as every version
; before this one did it. Without it the app is still offered in "Open with" and in Default apps.
Name: "assocmd"; Description: "{cm:AssocMarkdown,{#MyAppName}}"; GroupDescription: "{cm:FileAssociations}"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "..\..\Dev\Typedown\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not IsWebView2RuntimeInstalled

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "Markdown Editor"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; HKA: the install's own scope (HKLM in this administrative install), written there by name. Not HKCR: a write through
; it lands in HKCU\Software\Classes wherever that already has the key (an app installed per user made .md there, as
; Typora does), so the extensions went to the administrator's own HKCU and the file type to HKLM (2026-11-04, W10).
; The app's file type: its name, icon and how it opens a file. Always written: "Open with" and Default apps need it.
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}"; ValueType: string; ValueName: ""; ValueData: "{#MyAppAssocName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
; Offered in "Open with" for each Markdown extension and for .txt / .text (upstream #3), whether or not it is the
; default. A key made here goes again on uninstall when nothing else is left in it.
Root: HKA; Subkey: "Software\Classes\.md"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.markdown"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.mdown"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.mdown\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.mkdn"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.mkdn\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.mkd"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.mkd\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.mdwn"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.mdwn\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.mdtxt"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.mdtxt\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.mdtext"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.mdtext\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.rmd"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.rmd\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.txt"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.txt\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.text"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Classes\.text\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
; Listed in Settings > Default apps, where Windows lets the user make it the default (the installer cannot: since
; Windows 8 the user's own choice, UserChoice, is protected and wins over what is written here).
Root: HKA; Subkey: "Software\{#MyAppName}"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Markdown Editor"
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".md"; ValueData: "{#MyAppAssocKey}"
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".markdown"; ValueData: "{#MyAppAssocKey}"
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdown"; ValueData: "{#MyAppAssocKey}"
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mkdn"; ValueData: "{#MyAppAssocKey}"
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mkd"; ValueData: "{#MyAppAssocKey}"
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdwn"; ValueData: "{#MyAppAssocKey}"
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdtxt"; ValueData: "{#MyAppAssocKey}"
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdtext"; ValueData: "{#MyAppAssocKey}"
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".rmd"; ValueData: "{#MyAppAssocKey}"
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".txt"; ValueData: "{#MyAppAssocKey}"
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".text"; ValueData: "{#MyAppAssocKey}"
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: "Software\{#MyAppName}\Capabilities"; Flags: uninsdeletevalue
; The task: the default for each Markdown extension, where the user has not chosen one of their own. Uninstall takes
; it away only where it still names this app (UninstallAssociations), not one another app has put there since.
Root: HKA; Subkey: "Software\Classes\.md"; ValueType: string; ValueName: ""; ValueData: "{#MyAppAssocKey}"; Tasks: assocmd
Root: HKA; Subkey: "Software\Classes\.markdown"; ValueType: string; ValueName: ""; ValueData: "{#MyAppAssocKey}"; Tasks: assocmd
Root: HKA; Subkey: "Software\Classes\.mdown"; ValueType: string; ValueName: ""; ValueData: "{#MyAppAssocKey}"; Tasks: assocmd
Root: HKA; Subkey: "Software\Classes\.mkdn"; ValueType: string; ValueName: ""; ValueData: "{#MyAppAssocKey}"; Tasks: assocmd
Root: HKA; Subkey: "Software\Classes\.mkd"; ValueType: string; ValueName: ""; ValueData: "{#MyAppAssocKey}"; Tasks: assocmd
Root: HKA; Subkey: "Software\Classes\.mdwn"; ValueType: string; ValueName: ""; ValueData: "{#MyAppAssocKey}"; Tasks: assocmd
Root: HKA; Subkey: "Software\Classes\.mdtxt"; ValueType: string; ValueName: ""; ValueData: "{#MyAppAssocKey}"; Tasks: assocmd
Root: HKA; Subkey: "Software\Classes\.mdtext"; ValueType: string; ValueName: ""; ValueData: "{#MyAppAssocKey}"; Tasks: assocmd
Root: HKA; Subkey: "Software\Classes\.rmd"; ValueType: string; ValueName: ""; ValueData: "{#MyAppAssocKey}"; Tasks: assocmd

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing WebView2 Runtime..."; Flags: waituntilterminated; Check: not IsWebView2RuntimeInstalled
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
function IsWebView2RuntimeInstalled: Boolean;
var
  Version: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version)
     or RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
  begin
    Result := (Version <> '') and (Version <> '0.0.0.0');
  end;
end;

// The previous version's program files go before this one's are copied. Setup only ever added and replaced files, so
// an update kept every file the old version had and the new one does not: after the move to WinUI 3 the old UWP
// resources.pri stayed beside the app, WinUI 3 read its XAML resources from it first, and the updated app could not
// build its window. Only a folder with this app's exe in it is emptied (a custom folder may hold other things); the
// documents, settings and logs are elsewhere (Documents and LocalAppData), never in the program folder.
procedure RemovePreviousVersion;
var
  Dir: String;
  Found: TFindRec;
begin
  Dir := ExpandConstant('{app}');
  if not FileExists(AddBackslash(Dir) + '{#MyAppExeName}') then
    Exit;
  Log('Removing the previous version''s files from ' + Dir);
  if FindFirst(AddBackslash(Dir) + '*', Found) then
  try
    repeat
      if (Found.Name <> '.') and (Found.Name <> '..') then
      begin
        if Found.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
          DelTree(AddBackslash(Dir) + Found.Name, True, True, True)
        else
          DeleteFile(AddBackslash(Dir) + Found.Name);
      end;
    until not FindNext(Found);
  finally
    FindClose(Found);
  end;
end;

const
  MarkdownExtensions = '.md,.markdown,.mdown,.mkdn,.mkd,.mdwn,.mdtxt,.mdtext,.rmd';

// An extension's default, where it names this app.
procedure RemoveDefault(RootKey: Integer; Ext: String);
var
  Value: String;
begin
  if RegQueryStringValue(RootKey, 'Software\Classes\' + Ext, '', Value) and (CompareText(Value, '{#MyAppAssocKey}') = 0) then
  begin
    Log('Removing the default of ' + Ext);
    RegDeleteValue(RootKey, 'Software\Classes\' + Ext, '');
  end;
end;

// Each Markdown extension's default goes where it still names this app: the task left unchecked on an update (the
// versions before it always set it) or the app uninstalled. One another app has set since stays. HKCU too: the
// versions before wrote through HKCR, which put it there where HKCU had the extension already.
procedure UninstallAssociations;
var
  Rest, Ext: String;
  I: Integer;
begin
  Rest := MarkdownExtensions + ',';
  while Rest <> '' do
  begin
    I := Pos(',', Rest);
    Ext := Copy(Rest, 1, I - 1);
    Delete(Rest, 1, I);
    RemoveDefault(HKA, Ext);
    RemoveDefault(HKCU, Ext);
  end;
end;

// The user's own choice for .md other than this app, which what the installer writes does not change: UserChoice, or
// the default in the user's own classes (an app installed per user), which wins over the machine's.
function OtherUserChoice: Boolean;
var
  ProgId: String;
begin
  Result := (RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.md\UserChoice', 'ProgId', ProgId)
      and (ProgId <> '') and (CompareText(ProgId, '{#MyAppAssocKey}') <> 0))
    or (RegQueryStringValue(HKCU, 'Software\Classes\.md', '', ProgId)
      and (ProgId <> '') and (CompareText(ProgId, '{#MyAppAssocKey}') <> 0));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
begin
  if CurStep = ssInstall then
    RemovePreviousVersion;
  if CurStep = ssPostInstall then
  begin
    if not WizardIsTaskSelected('assocmd') then
      UninstallAssociations
    // Settings opened on this app's page where Windows has one (11), else on Default apps.
    else if OtherUserChoice and not WizardSilent and
      (SuppressibleMsgBox(FmtMessage(CustomMessage('AssocDefaultApps'), ['{#MyAppName}']), mbConfirmation, MB_YESNO, IDNO) = IDYES) then
      ShellExecAsOriginalUser('open', 'ms-settings:defaultapps?registeredAppMachine={#MyAppName}', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;

// "Open with" entries the versions before put in HKCU through HKCR (as UninstallAssociations says), which their
// uninstall, deleting through HKCR as well, did not find there (left on W10, 2026-11-04).
procedure UninstallUserOpenWith;
var
  Rest, Ext: String;
  I: Integer;
begin
  Rest := MarkdownExtensions + ',.txt,.text,';
  while Rest <> '' do
  begin
    I := Pos(',', Rest);
    Ext := Copy(Rest, 1, I - 1);
    Delete(Rest, 1, I);
    if RegValueExists(HKCU, 'Software\Classes\' + Ext + '\OpenWithProgids', '{#MyAppAssocKey}') then
    begin
      Log('Removing the Open with entry of ' + Ext + ' in HKCU');
      RegDeleteValue(HKCU, 'Software\Classes\' + Ext + '\OpenWithProgids', '{#MyAppAssocKey}');
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    UninstallAssociations;
    UninstallUserOpenWith;
  end;
end;
