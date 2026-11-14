; =====================================================================
;  CP Bootable USB — Windows Installer (Inno Setup 6)
;  COMPUTER PLUS · Developed by MD AMZAD HOSSAIN RONI
;
;  Built automatically by GitHub Actions:
;    ISCC.exe /DMyAppVersion=1.2.0 /DPublishDir=..\publish installer\CPBootableUSB.iss
;
;  Installs to:  C:\Program Files\COMPUTER PLUS\CP Bootable USB\
;    CPBootableUSB.exe        the software
;    Payload\                 WinPE boot files (added automatically once Phase 2 + 3 are built)
;    Docs\                    README + architecture
;  Shortcuts: one Desktop icon + Start Menu entry for the software only.
; =====================================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\src\UsbMaker\Payload"
#endif

#define MyAppName      "CP Bootable USB"
#define MyAppPublisher "COMPUTER PLUS"
#define MyAppExeName   "CPBootableUSB.exe"

[Setup]
; Fixed AppId — never change it, Windows uses it to upgrade / uninstall.
AppId={{6C1E8F2A-4B7D-4E0A-9C3B-5C0B00DAB1E5}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=© COMPUTER PLUS, Bera, Pabna — Developed by MD AMZAD HOSSAIN RONI
AppContact=+8801754 614 614 | +8801913 354 321
AppComments=Fast & Reliable Windows Deployment - We Always Ready To Help You.
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup

DefaultDirName={autopf}\COMPUTER PLUS\CP Bootable USB
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

OutputDir=..\dist
OutputBaseFilename=CP-Bootable-USB-Setup-{#MyAppVersion}
SetupIconFile=..\src\UsbMaker\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
WizardImageFile=wizard-large-164.bmp,wizard-large.bmp
WizardSmallImageFile=wizard-small-55.bmp,wizard-small.bmp
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; The software (portable single-file build, renamed)
Source: "{#PublishDir}\UsbMaker.exe"; DestDir: "{app}"; DestName: "{#MyAppExeName}"; Flags: ignoreversion

; WinPE boot payload (Media\ + Tools\). Present once Phase 2 + 3 are built; skipped otherwise.
Source: "{#PayloadDir}\*"; DestDir: "{app}\Payload"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

; Documentation
Source: "..\README.md"; DestDir: "{app}\Docs"; Flags: ignoreversion
Source: "..\docs\ARCHITECTURE.md"; DestDir: "{app}\Docs"; Flags: ignoreversion

[Icons]
; Only the software gets shortcuts: Desktop + Start Menu.
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "COMPUTER PLUS - Windows Bootable USB Creator"
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "COMPUTER PLUS - Windows Bootable USB Creator"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
; Old payload files from previous versions
Type: filesandordirs; Name: "{app}\Payload"
