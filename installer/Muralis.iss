; Installeur Muralis — Inno Setup 6.
; Deux modes d'installation au choix de l'utilisateur (PrivilegesRequiredOverridesAllowed=dialog) :
;   - « pour tout le monde »  : {autopf}  -> C:\Program Files\Muralis (admin requis)
;   - « pour moi seulement »  : {autopf}  -> %LocalAppData%\Programs\Muralis (sans admin)
; Toujours les constantes {auto*}, jamais {pf} en dur (cf. AGENTS.md).
; La config de l'app vit dans %LocalAppData%\Muralis quel que soit le mode, et est
; volontairement conservée à la désinstallation.

#define MyAppName "Muralis"
#define MyAppVersion "1.2.0"
#define MyAppPublisher "Arkatul"
#define MyAppURL "https://github.com/Arkatul/muralis"
#define MyAppExeName "Muralis.exe"
#define PublishDir "..\src\Muralis\bin\Release\net10.0-windows\win-x64\publish"

[Setup]
AppId={{4F0AE54E-9E6C-4A28-BB67-FAD9B25399D3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
SetupIconFile=..\src\Muralis\Assets\muralis.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; L'app tourne en tray : laisser le Restart Manager la fermer avant mise à jour.
CloseApplications=yes
; La tâche « startup » écrit dans HKCU : en install élevée pour un autre compte que
; l'admin, la valeur irait dans la ruche de l'admin. Cas marginal assumé — le toggle
; « Lancer au démarrage » de la page Paramètres de l'app reste la source de vérité.
UsedUserAreasWarning=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
english.StartupTask=Start {#MyAppName} when Windows starts (notification area)
french.StartupTask=Lancer {#MyAppName} au démarrage de Windows (zone de notification)

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartupTask}"

[Files]
; Publish single-file self-contained : l'essentiel est dans l'exe ; on copie le dossier
; publish entier par sûreté (hors symboles de debug).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Même clé/valeur que StartupService (HKCU\...\Run, "<exe>" --minimized) : l'app relit
; cet état tel quel dans sa page Paramètres.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; \
    Flags: uninsdeletevalue; Tasks: startup
; Langue choisie dans l'assistant : amorce la langue de l'app au premier lancement
; (consommée par LocalizationService uniquement si la config n'a pas encore de langue).
Root: HKCU; Subkey: "Software\Muralis"; ValueType: string; ValueName: "PreferredLanguage"; \
    ValueData: "{language}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
