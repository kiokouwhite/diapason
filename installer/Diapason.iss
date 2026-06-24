; ==========================================================================
;  Diapason - installeur tournoi (Inno Setup). ASCII only (pas d'accents).
;  Compile : ISCC.exe Diapason.iss  ->  ..\Setup-Tournoi-Diapason.exe
; ==========================================================================
#define AppName "Diapason"

[Setup]
AppName={#AppName}
AppVersion=1.2
AppPublisher=Association FGC
DefaultDirName={autopf}\Diapason
DefaultGroupName=Diapason
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=..
OutputBaseFilename=Setup-Tournoi-Diapason
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\logo.ico
; Pilotes noyau (ViGEmBus/HidHide) -> proposer un redemarrage en fin d'install.
AlwaysRestart=yes
UninstallDisplayIcon={app}\Diapason.exe

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "autostart"; Description: "Lancer Diapason automatiquement a chaque ouverture de session (recommande)"

[Files]
Source: "..\dist\Diapason.exe";                  DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\SDL2.dll";                      DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\gamecontrollerdb.txt";          DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\2-Lancer-Diapason.bat";         DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\1-Installer-pilotes.ps1";       DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\3-Activer-demarrage-auto.ps1";  DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\Desactiver-demarrage-auto.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\LISEZMOI.txt";                  DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\DEBLOQUER-MANETTES.bat";        DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\Diagnostic-Diapason.bat";       DestDir: "{app}"; Flags: ignoreversion
Source: "_install-drivers.ps1";                  DestDir: "{app}"; Flags: ignoreversion
Source: "_register-autostart.ps1";               DestDir: "{app}"; Flags: ignoreversion
; Pilotes embarques (installeur 100% autonome, sans internet ni winget).
Source: "redist\vc_redist.x64.exe";              DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "redist\ViGEmBusSetup.exe";              DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "redist\HidHideSetup.exe";               DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\Diapason";              Filename: "{app}\Diapason.exe"
Name: "{group}\Desinstaller Diapason"; Filename: "{uninstallexe}"

[Run]
; 1) Pilotes embarques, en silencieux (l'installeur tourne deja en admin -> /quiet marche).
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installation de Visual C++ Runtime..."; Flags: runhidden waituntilterminated
Filename: "{tmp}\ViGEmBusSetup.exe"; Parameters: "/quiet /norestart"; StatusMsg: "Installation du pilote ViGEmBus..."; Flags: runhidden waituntilterminated
Filename: "{tmp}\HidHideSetup.exe"; Parameters: "/quiet /norestart"; StatusMsg: "Installation du pilote HidHide..."; Flags: runhidden waituntilterminated
; 2) Verification (+ repli winget si un pilote manque encore, + alerte si echec).
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\_install-drivers.ps1"""; StatusMsg: "Verification des pilotes..."; Flags: runhidden waituntilterminated
; 3) Demarrage automatique (option cochee).
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\_register-autostart.ps1"" -Exe ""{app}\Diapason.exe"" -UserName ""{username}"""; StatusMsg: "Configuration du demarrage automatique..."; Flags: runhidden waituntilterminated; Tasks: autostart

[UninstallRun]
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""Diapason"" /F"; Flags: runhidden; RunOnceId: "DelDiapasonTask"
