; SXA-RTX Analytics - Instalador dual IIS / Windows Service
; Uso: iscc /DMyAppVersion=1.0.0 installer\Analytics.iss  (o via publish-analytics.ps1)
;
; IMPORTANTE: las rutas relativas de este archivo se resuelven desde la carpeta
; installer\, por eso se sube un nivel con "..\" para llegar a la raiz del repo.
; {src} NO sirve: es una constante de runtime y el compilador no la expande.

#define MyAppName "SXA-RTX Analytics"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "ECCSA Automation"
#define MyAppURL "https://github.com/hector1516/SXA-RTX-Analytics"
#define MyAppExeName "SXA.RTX.Analytics.Web.exe"
#define MyPublishDir "..\artifacts\publish"
#define MyOutputDir "..\artifacts\pkg"
#define MyIconFile "..\src\SXA.RTX.Analytics.Web\wwwroot\favicon.ico"

[Setup]
AppId={{4E1D2C3B-5A6F-4B7C-9D8E-0F1A2B3C4D5E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={pf}\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#MyOutputDir}
OutputBaseFilename=Setup_SXA_RTX_Analytics_v{#MyAppVersion}
SetupIconFile={#MyIconFile}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
DisableDirPage=no
DisableProgramGroupPage=yes
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "install.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\tools\update-analytics.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "http://localhost:5000"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[Run]
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\install.ps1"" -InstallPath ""{app}"" -Mode install"; Flags: runhidden waituntilterminated
Filename: "http://localhost:5000"; Description: "Abrir SXA-RTX Analytics"; Flags: postinstall nowait skipifsilent shellexec

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\install.ps1"" -InstallPath ""{app}"" -Mode uninstall"; Flags: runhidden waituntilterminated

[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  Msg: String;
begin
  if (CurStep = ssPostInstall) and (not WizardSilent) then
  begin
    Msg := 'SXA-RTX Analytics se instaló correctamente.' + #13#10#13#10 +
           'Abre: http://localhost:5000' + #13#10 +
           'Usuario inicial: ECCSA' + #13#10 +
           'Contraseña inicial: Qwe123456' + #13#10#13#10 +
           'La conexión SQL quedó precargada por defecto.' + #13#10 +
           'Guarda estos datos y cambia la contraseña al entrar.' + #13#10 +
           'También quedó una copia en PRIMER-INGRESO.txt.';
    MsgBox(Msg, mbInformation, MB_OK);
  end;
end;