#define MyAppName "HDBank QR Reconciler"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "HDBank QR Reconciler"
#define MyAppExeName "HDBank_QR_Reconciler.exe"

#define ProjectDir "C:\Users\Admin\Desktop\Code\QrLedgerReconciler"
#define PublishDir ProjectDir + "\bin\Release\net8.0-windows\win-x64\publish"

[Setup]

; ============================================================
; BASIC INFORMATION
; ============================================================

AppId={{8D6D6D5A-5C4D-4F39-9C76-QRLEDGER2026}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

; ============================================================
; INSTALL LOCATION
; ============================================================

DefaultDirName={autopf}\HDBank QR Reconciler
DefaultGroupName={#MyAppName}

; ============================================================
; OUTPUT
; ============================================================

OutputDir={#ProjectDir}\Installer\Output
OutputBaseFilename=HDBank_QR_Reconciler_Setup_v{#MyAppVersion}

; ============================================================
; ARCHITECTURE
; ============================================================

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; ============================================================
; INSTALLER PRIVILEGES
; ============================================================

PrivilegesRequired=admin

; ============================================================
; COMPRESSION
; ============================================================

Compression=lzma2/max
SolidCompression=yes

; ============================================================
; UNINSTALL
; ============================================================

Uninstallable=yes

; ============================================================
; VERSION INFORMATION
; ============================================================

VersionInfoVersion=1.0.0.0
VersionInfoCompany=HDBank QR Reconciler
VersionInfoDescription=Đối chiếu Sổ cái và QR tĩnh HDBank
VersionInfoProductName=HDBank QR Reconciler
VersionInfoProductVersion=1.0.0

[Files]

; ============================================================
; COPY TOÀN BỘ PUBLISH
; ============================================================

Source: "{#PublishDir}\*"; \
    DestDir: "{app}"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]

; ============================================================
; START MENU
; ============================================================

Name: "{autoprograms}\{#MyAppName}"; \
    Filename: "{app}\{#MyAppExeName}"; \
    WorkingDir: "{app}"; \
    Comment: "Đối chiếu Sổ cái và QR tĩnh HDBank"

; ============================================================
; START MENU - UNINSTALL
; ============================================================

Name: "{autoprograms}\Gỡ cài đặt {#MyAppName}"; \
    Filename: "{uninstallexe}"

; ============================================================
; DESKTOP
; ============================================================

Name: "{autodesktop}\{#MyAppName}"; \
    Filename: "{app}\{#MyAppExeName}"; \
    WorkingDir: "{app}"; \
    Comment: "Đối chiếu Sổ cái và QR tĩnh HDBank"

[Run]

; ============================================================
; RUN APPLICATION AFTER INSTALL
; ============================================================

Filename: "{app}\{#MyAppExeName}"; \
    Description: "Chạy {#MyAppName}"; \
    Flags: nowait postinstall skipifsilent