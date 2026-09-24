#define AppVersion "1.0.0"
[Setup]
#ifdef TestBuild
AppId=ST4RR-Installer-QA
#else
AppId={{C337D204-3F04-426E-8A72-718B87DB1D8B}
#endif
AppName=ST4RR
AppVersion={#AppVersion}
AppPublisher=fly1ngangel
AppComments=Brawl Stars player finder and battle archive
DefaultDirName={localappdata}\Programs\ST4RR
DefaultGroupName=ST4RR
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=6.3
OutputDir=dist
#ifdef TestBuild
OutputBaseFilename=ST4RR-QA-Setup-1.0.0
#else
OutputBaseFilename=ST4RR-Setup-1.0.0
#endif
SetupIconFile=src\app.ico
UninstallDisplayIcon={app}\ST4RR.exe
WizardStyle=modern dark includetitlebar
WizardSizePercent=110
WizardImageFile=src\wizard.bmp
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
#ifndef TestBuild
AppMutex=Local\ST4RRF1ND-{username}
#endif
UsePreviousLanguage=yes
LanguageDetectionMethod=uilanguage
DisableWelcomePage=no
ShowLanguageDialog=auto
VersionInfoVersion=1.0.0.0
VersionInfoDescription=ST4RR Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.DesktopShortcut=Create a desktop shortcut
russian.DesktopShortcut=Создать ярлык на рабочем столе
english.LaunchApp=Launch ST4RR
russian.LaunchApp=Запустить ST4RR
english.NetInstalling=Installing Microsoft .NET Framework 4.8. This prerequisite may require administrator approval.
russian.NetInstalling=Установка Microsoft .NET Framework 4.8. Для этого компонента может потребоваться разрешение администратора.
english.NetFailed=Microsoft .NET Framework 4.8 could not be installed. Install it and run this setup again. Error code:
russian.NetFailed=Не удалось установить Microsoft .NET Framework 4.8. Установите его и повторите запуск установщика. Код ошибки:
english.NetRestart=Restart Windows to complete .NET Framework installation, then run this installer again.
russian.NetRestart=Перезагрузите Windows для завершения установки .NET Framework, затем запустите этот установщик снова.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[Files]
Source: "tools\net48.exe"; Flags: dontcopy nocompression
Source: "build\ST4RR.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "build\ST4RR.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "build\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

#ifndef TestBuild
[InstallDelete]
Type: files; Name: "{app}\ST4RRF1ND.exe"
Type: files; Name: "{app}\ST4RRF1ND.exe.config"
Type: files; Name: "{autoprograms}\ST4RRF1ND.lnk"
Type: files; Name: "{autodesktop}\ST4RRF1ND.lnk"
#endif

#ifndef TestBuild
[Icons]
Name: "{autoprograms}\ST4RR"; Filename: "{app}\ST4RR.exe"
Name: "{autodesktop}\ST4RR"; Filename: "{app}\ST4RR.exe"; Tasks: desktopicon
#endif

[Run]
Filename: "{app}\ST4RR.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent; Check: CanLaunch

[Code]
var
  NetRestartRequired: Boolean;

function HasFramework: Boolean;
var Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
end;

function CanLaunch: Boolean;
begin
  Result := HasFramework and (not NetRestartRequired);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExitCode: Integer;
begin
  Result := '';
  if not HasFramework then begin
    WizardForm.StatusLabel.Caption := CustomMessage('NetInstalling');
    ExtractTemporaryFile('net48.exe');
    if not ShellExec('runas', ExpandConstant('{tmp}\net48.exe'), '/passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ExitCode) then begin
      Result := CustomMessage('NetFailed') + ' ' + IntToStr(ExitCode);
      exit;
    end;
    if (ExitCode = 3010) or (ExitCode = 1641) then begin
      NetRestartRequired := True;
      NeedsRestart := True;
    end else if ExitCode <> 0 then begin
      Result := CustomMessage('NetFailed') + ' ' + IntToStr(ExitCode);
      exit;
    end;
    if (not HasFramework) and (not NetRestartRequired) then
      Result := CustomMessage('NetFailed') + ' ' + IntToStr(ExitCode);
  end;
end;

function NeedRestart: Boolean;
begin
  Result := NetRestartRequired;
end;
