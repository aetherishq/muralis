# AGENTS.md — Muralis

Gestionnaire de fonds d'écran multi-moniteurs pour Windows, en WPF/.NET.
Ce fichier cadre les décisions déjà prises pour que Claude Code n'ait pas à les redemander ou à les remettre en question sans raison.

## Résumé du projet

Muralis permet de configurer, écran par écran, un fond d'écran fixe, un slideshow local (répertoire), ou un slideshow depuis une source web (Bing, Wallhaven, ou toute source personnalisée), avec un mode d'affichage indépendant par écran (stretch/fit/fill/tile/center/span).

## Stack & décisions déjà tranchées (ne pas rediscuter sans raison forte)

- **.NET 10 (LTS), WPF** (pas WinUI 3) — choisi pour : packaging simple en single-file self-contained, aucun runtime externe à déployer, modèle "app tray sans fenêtre principale" natif, gros corpus de doc/exemples. .NET 10 est LTS jusqu'en novembre 2028 ; .NET 8 (utilisé un temps dans les brouillons de ce projet) sort de support en novembre 2026, ne pas partir dessus. `TargetFramework` du `.csproj` : `net10.0-windows`.
- **WPF-UI** (lepoco/wpfui) pour le look Fluent (Mica, thèmes clair/sombre, contrôles style Windows 11) sans les contraintes MSIX/App SDK.
- **H.NotifyIcon** pour l'icône de tray (fork maintenu de Hardcodet.NotifyIcon.Wpf).
- **System.Text.Json** pour la config, stockée dans `%LocalAppData%\Muralis\config.json` — **Local, pas Roaming** : pas de sémantique de sync domaine nécessaire, et c'est aussi là que vivra le cache d'images de slideshow (fichiers potentiellement volumineux, jamais dans Roaming). LocalLow n'a pas lieu d'être ici : c'est réservé aux process à intégrité Windows réduite (sandbox type ancien IE Protected Mode), pas à une app WPF standard.
- **Inno Setup** pour l'installeur — un seul `.exe` de sortie. Pas de MSIX, pas de WiX sauf besoin futur explicite. Proposer les deux modes d'install via `PrivilegesRequiredOverridesAllowed=dialog` : "pour tout le monde" (Program Files, admin requis) ou "pour moi seulement" (`%LocalAppData%\Programs`, pas d'admin). Utiliser systématiquement les constantes `{autopf}` / `{autoprograms}` / `{autostartmenu}` dans le script `.iss`, jamais `{pf}` en dur, sinon le mode "moi seulement" plante en accès refusé.
- Pas de tâche planifiée pour le démarrage auto : une entrée registre `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` suffit et ne nécessite pas de droits admin.

## Architecture

### Application windowless / tray-first
L'app ne montre **jamais** de `MainWindow` au démarrage par défaut. Elle démarre dans le tray (icône + menu contextuel). La fenêtre de settings s'ouvre à la demande (double-clic tray ou item de menu) et se cache (pas se ferme) sur `Closing` pour éviter de tuer le process.

Lancement avec `--minimized` : ne montre rien, juste le tray. Sans argument (double-clic sur le raccourci) : ouvre directement les settings.

### Modèle de configuration par écran

```csharp
public class ScreenConfig
{
    public string DeviceId { get; set; }       // device path du moniteur, pas juste un index
    public WallpaperMode Mode { get; set; }     // Fixed | Slideshow
    public string SourceType { get; set; }      // "LocalFolder" | nom d'une IWallpaperSource
    public string SourcePath { get; set; }       // chemin local ou config source web
    public TimeSpan SlideshowInterval { get; set; }
    public DesktopWallpaperPosition DisplayMode { get; set; } // voir mapping natif ci-dessous
}
```

Toujours identifier les écrans par leur **device path** (via `IDesktopWallpaper.GetMonitorDevicePathAt`), jamais par un index d'écran — l'ordre peut changer entre deux sessions Windows (reconnexion, changement de résolution, etc.).

### Interop natif — API Windows à utiliser en priorité

Utiliser `IDesktopWallpaper` (COM, dispo depuis Windows 8) plutôt que `SystemParametersInfo` — c'est l'API qui gère nativement le multi-écran et le mode d'affichage par moniteur.

Mapping direct avec les modes demandés en V1 :

| Spec V1  | `DESKTOP_WALLPAPER_POSITION` |
|----------|-------------------------------|
| stretch  | `DWPOS_STRETCH`               |
| fit      | `DWPOS_FIT`                    |
| fill     | `DWPOS_FILL`                   |
| tile     | `DWPOS_TILE`                    |
| center   | `DWPOS_CENTER`                  |
| span     | `DWPOS_SPAN`                    |

`span` traite tous les moniteurs comme une seule surface — le combiner avec un choix "séparé par écran" n'a pas de sens fonctionnel ; désactiver/masquer les autres réglages par écran dans l'UI quand `span` est sélectionné sur un écran.

### Abstraction des sources web

```csharp
public interface IWallpaperSource
{
    string Name { get; }
    Task<Uri> GetImageUrlAsync(CancellationToken ct);
}
```

Chaque source a un `SourceKind` : `Random` (chaque requête → image différente, proposée en **diaporama**) ou `Daily` (image du jour, ex. Bing, proposée dans la **card Image** en mode fixe — re-vérifiée en interne toutes les heures et re-posée uniquement si l'URL d'image change ; le cache web est nommé par hash d'URL).

Implémentation générique config-driven pour toute source qui renvoie du JSON avec une URL d'image dedans (évite de coder une classe par source). **Exception assumée (V1.2) : Wallhaven** — formulaire typé (`WallhavenSourceOptions` : tags stricts `+tag`, catégories, purity, adaptation auto à l'écran destinataire via `atleast`/`ratios` calculés à la requête), URL construite par `WallhavenUrlBuilder`, et récupération d'une **page de 24 résultats** piochée localement (cache mémoire par couple source/écran, ~1 h) pour respecter le quota API (~45 req/min) :

```csharp
public class HttpJsonSource : IWallpaperSource
{
    public string Name { get; init; }
    public string RequestUrl { get; init; }
    public string ImageUrlJsonPath { get; init; }
    public string ApiKeyHeader { get; init; }
    public string ApiKey { get; init; }
}
```

L'utilisateur doit pouvoir ajouter une source custom (URL + JsonPath) sans recompilation, en plus des sources préconfigurées.

### Sources préconfigurées

**Isolation par profil : déjà acquise gratuitement.** `%LocalAppData%` est per-profil Windows par définition, donc `config.json` (et par extension les sources qu'un utilisateur a ajoutées) est automatiquement isolé d'un profil à l'autre, que l'app soit installée per-user ou pour tous les utilisateurs (voir section Build & packaging pour le choix du mode d'install). Pas besoin de fichier de config séparé ou de logique de catégorisation applicative pour ça.

Une seule liste de presets, proposés dans l'UI "Ajouter une source" (catalogue statique embarqué dans l'app, pas dans `config.json`). Catalogue final (V1.2) : **Bing** (image du jour, pas de clé) et **Wallhaven** (`purity` sfw/sketchy/nsfw, clé requise pour le NSFW). Tout le reste (boorus, Unsplash…) passe par la **source personnalisée** (URL + JsonPath + en-tête libre), sous la responsabilité de l'utilisateur — historique : Gelbooru/Rule34 retirés car auth `api_key/user_id` devenue obligatoire, Danbooru/e621 retirés au resserrage du catalogue, Unsplash jamais embarqué (EULA restrictive). Un preset peut être instancié plusieurs fois (identité = `Id`, pas le nom).

`config.json` d'un profil donné ne contient que les sources que **cet utilisateur** a choisi d'ajouter depuis ce catalogue (+ éventuelles sources custom). Rien d'autre à gérer côté code.

### Démarrage avec Windows

```csharp
private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
private const string AppName = "Muralis";

public static void SetStartup(bool enable)
{
    using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
    if (enable)
        key.SetValue(AppName, $"\"{Process.GetCurrentProcess().MainModule.FileName}\" --minimized");
    else
        key.DeleteValue(AppName, throwOnMissingValue: false);
}
```

La checkbox "Start when Windows starts" dans les settings doit lire son état depuis le registre directement (`key.GetValue(AppName) != null`), jamais depuis une valeur miroir dans `config.json` — sinon désync possible si l'utilisateur modifie le registre à la main.

## Design de l'UI

**Toute modification de XAML ou création de fenêtre/page doit respecter `.claude/DESIGN.md`** (règles Fluent 2 / WPF-UI : cards, espacements, typographie, patterns par page). Lire ce fichier avant tout travail visuel. En cas de pattern incertain, consulter les articles Microsoft Learn listés dans DESIGN.md via le MCP Microsoft Learn plutôt qu'improviser. Ne pas proposer de migration vers WinUI 3 : décision déjà tranchée (voir Stack), le niveau de finition visé est atteignable en WPF-UI.

## Conventions de code

- MVVM (CommunityToolkit.Mvvm pour `[ObservableProperty]` / `[RelayCommand]`, pas de framework MVVM plus lourd).
- Un `ScreenSettingsViewModel` par écran détecté, liste bindée dans la fenêtre de settings.
- Toute couche réseau (fetch de source web) doit être async, annulable (`CancellationToken`), et ne jamais bloquer l'UI thread — le slideshow tourne en fond via un `DispatcherTimer` ou un `System.Threading.Timer` qui marshalle vers l'UI thread seulement pour l'appel `IDesktopWallpaper`.
- Pas de dépendance à un framework DI lourd pour une V1 aussi petite — injection manuelle simple dans `App.xaml.cs` suffit.

## Localisation (depuis V1.1)

L'UI est multilingue : `src/Muralis/Resources/Strings.resx` (**anglais = langue neutre**) + `Strings.fr.resx`. **Toute nouvelle chaîne visible passe par ces deux fichiers** — jamais de littéral UI en dur (XAML : `{x:Static res:Strings.Clé}` ; C# : `Strings.Clé` / `string.Format(Strings.CléFormat, …)`), puis régénérer l'accessor avec `tools\gen-strings.ps1`. La langue (`AppConfig.Language`, null = Windows) s'applique **avant** toute construction d'UI ; un changement à chaud reconstruit fenêtre + tray + ViewModels (`App.ApplyLanguage`). Ne jamais traduire les identités persistées : `SourceType` (« LocalFolder »), noms des sources/presets.

## Documentation publique (depuis V1.2.1)

Le repo a un `README.md` (anglais) avec captures (`assets/screenshots/`) et une licence **GPL-3.0** (`LICENSE`). **Toute feature visible utilisateur doit être reflétée dans README.md** (section Features, captures si le visuel change significativement). **Les notes de release GitHub sont rédigées en anglais** (cohérence avec le README ; l'installeur, lui, reste bilingue).

## Hors scope V1 (ne pas implémenter sans qu'on en discute)

- Mise à jour auto de l'app
- Association de type de fichier
- Wallpapers vidéo/animés
- Sync cloud de la config

## Environnement de dev

### Captures d'écran des itérations

Les captures de validation visuelle sont déposées dans `C:\Users\alexi\Pictures\Screenshots` (éventuellement un sous-dossier par sujet, ex. `muralis\`). Quand l'utilisateur dit « screenshot 3 » / « capture 3 », cela désigne le fichier `3.png` le plus récent de ce dossier — pas besoin qu'il re-précise le chemin.

Terminal Windows natif (PowerShell 7+ recommandé, `cmd.exe` fonctionne aussi) — **pas WSL**. WPF dépend d'API Win32/COM natives (`IDesktopWallpaper`, registre `HKCU`, rendu DirectX/PresentationCore) qui n'existent pas sous Linux ; même si `dotnet build` peut techniquement restaurer/compiler une cible `net10.0-windows` depuis WSL, l'app ne peut ni s'exécuter ni être testée dans cet environnement. Inno Setup est également un outil Windows natif.

### Prérequis à installer avant de démarrer

```powershell
winget install Microsoft.DotNet.SDK.10
winget install Git.Git
```
Vérifier après réouverture du terminal : `dotnet --version` (doit afficher du `10.x`).

Éditeur : VS Code + extension "C# Dev Kit" suffit pour démarrer (Claude Code fait l'essentiel du code). Visual Studio Community + workload ".NET Desktop" seulement si le XAML à l'aveugle devient pénible (apporte designer visuel + Hot Reload). Inno Setup et Windows SDK : pas nécessaires avant la phase packaging.

## Build & packaging

- Build complet de l'installeur : `installer\build.ps1` (publish + ISCC, sortie dans `dist\`, non versionné). Inno Setup 6 : `winget install -e --id JRSoftware.InnoSetup` (ISCC dans `%LocalAppData%\Programs\Inno Setup 6`).
- Publish : `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
- Installeur : script Inno Setup dans `/installer`, embarque le binaire self-contained.
  - `PrivilegesRequiredOverridesAllowed=dialog` pour laisser le choix "pour tout le monde" (Program Files, admin) vs "pour moi seulement" (`{localappdata}\Programs`, pas d'admin) — constantes `{autopf}` / `{autoprograms}` / `{autostartmenu}` obligatoires dans le script, jamais `{pf}` en dur.
  - Config toujours dans `{localappdata}\Muralis\config.json`, quel que soit le mode d'install choisi.
  - Crée le raccourci menu démarrer, propose la case "lancer au démarrage" via l'appel à `SetStartup(true)` au premier lancement post-install.
