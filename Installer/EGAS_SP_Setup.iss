#define MyAppName "EGAS SP"
#define MyAppVersion "1.0"
#define MyAppPublisher "EGAS SP"
#define MyAppExeName "EGAS.exe"

#define ProjectDir "C:\Users\Admin\Desktop\Code\QrLedgerReconciler"
#define PublishDir ProjectDir + "\bin\Publish"

[Setup]

; ============================================================
; BASIC INFORMATION
; ============================================================

AppId={{6C0A8E4F-7B2D-4E1F-A3C9-2D5B8F1E6A74}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

; ============================================================
; INSTALL LOCATION
; ============================================================

DefaultDirName={autopf}\EGAS SP
DefaultGroupName={#MyAppName}

; ============================================================
; OUTPUT
; ============================================================

OutputDir={#ProjectDir}\Installer\Output
OutputBaseFilename=EGAS_SP_Setup_v{#MyAppVersion}

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
VersionInfoCompany=EGAS SP
VersionInfoDescription=EGAS SP - Công cụ đối chiếu và xử lý nghiệp vụ EGAS
VersionInfoProductName=EGAS SP
VersionInfoProductVersion=1.0.0.0

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
    Comment: "EGAS SP - Công cụ đối chiếu và xử lý nghiệp vụ EGAS"

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
    Comment: "EGAS SP - Công cụ đối chiếu và xử lý nghiệp vụ EGAS"

[Run]

; ============================================================
; RUN APPLICATION AFTER INSTALL
; ============================================================

Filename: "{app}\{#MyAppExeName}"; \
    Description: "Chạy {#MyAppName}"; \
    Flags: nowait postinstall skipifsilent
