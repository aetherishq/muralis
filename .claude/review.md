# Review Muralis — 2026-11-17

Périmètre : tout `src/Muralis` (v1.2.2, `main` @ 3854f61), lecture seule.
Hors périmètre (déjà suivi) : **#41** (pipeline compose + `SetWallpaper` sur l'UI thread, report plein écran, regroupement des poses) et **#42** (dock/undock, moniteurs capturés dans les closures de `SlideshowService.Restart`, device paths disparus). Les constats ci-dessous qui touchent ces sujets le signalent explicitement au lieu de les re-signaler.

Chaque constat a été vérifié dans le code. Le format d'URL Bing (M5) a aussi été vérifié par un appel réel à l'API.

## Suivi des correctifs

Tableau tenu à jour à chaque PR. ✅ = corrigé et mergé, ⏳ = issue ouverte, 🔗 = suivi dans une issue préexistante.

| Constat | Sévérité | Issue | Statut |
|---|---|---|---|
| C1 — Config illisible écrasée au démarrage | Critique | #43 | ✅ PR #55 (v1.2.3) |
| C2 — Intervalle > 24,8 j : crash permanent au démarrage | Critique | #44 | ✅ PR #53 (v1.2.3) |
| M1 — Téléchargements concurrents de la même URL | Majeur | #45 | ✅ PR #59 (v1.2.4) |
| M2 — Cache web empoisonné (fichier partiel / non-image) | Majeur | #45 | ✅ PR #59 (v1.2.4) |
| M3 — Timeouts HTTP avalés comme annulation | Majeur | #46 | ✅ PR #57 (v1.2.4) |
| M4 — « Appliquer » interrompu à la première erreur | Majeur | #47 | ✅ PR #60 (v1.2.4) |
| M5 — « Enregistrer le fond actuel » cassé pour Bing | Majeur | #48 | ✅ PR #58 (v1.2.4) |
| M6 — Ni journalisation ni gestionnaire global | Majeur | #46 | ✅ PR #57 (v1.2.4) |
| m1 — Fuite d'abonnements au changement de langue | Mineur | #49 | ✅ PR #64 |
| m2 — Miniatures décodées sur l'UI thread | Mineur | #49 | ✅ PR #64 |
| m3 — `GetMonitors()` évalué avant le throttle | Mineur | #49 | ✅ PR #64 |
| m4 — Exception d'abonné maquillée en échec de source | Mineur | #46 | ✅ PR #57 (v1.2.4) |
| m5 — Interop COM (instances, `SetPosition` systématique) | Mineur | #41 | ✅ PR #67 |
| m6 — Source supprimée toujours interrogée | Mineur | #47 | ✅ PR #60 (v1.2.4) — seules les cibles de la source sont relancées |
| m7 — Taille de téléchargement non bornée | Mineur | #45 | ✅ PR #59 (v1.2.4) |
| m8 — Config relue sur disque à chaque requête | Mineur | #43 | ✅ PR #55 (v1.2.3) |
| m9 — Clé Wallhaven sauvée à chaque frappe | Mineur | #43 | ✅ PR #55 (v1.2.3) |
| m10 — Messages techniques en français dans l'UI | Mineur | #46 | ✅ PR #57 (v1.2.4) |
| m11 — DESIGN.md : `Maximum` de la NumberBox | Mineur | #44 | ✅ PR #53 (v1.2.3) |
| m11 — DESIGN.md : CheckBox « Adapter à l'écran », `FontSize` en dur | Mineur | #50 | ✅ PR #65 |
| s1, s3, s4, s6 — CTS, User-Agent, entropie DPAPI, `PooledConnectionLifetime` | Suggestion | #52 | ⏳ |
| s2 — `_urlByPath` en mémoire sans borne | Suggestion | #48 | ✅ PR #58 (v1.2.4) — borné ; persistance jugée inutile (l'URL est ré-enregistrée au premier fetch après redémarrage) |
| s5 — Fire-and-forget et exceptions non observées | Suggestion | #46 | ✅ PR #57 (v1.2.4) |
| Testabilité (a) — projet de tests | — | #51 | 🟡 amorcé : `tests/Muralis.Tests` + CI (PR #53), tests config/migrations/intervalle |
| Testabilité (b) — extraction `Muralis.Core` | — | #52 | ⏳ |

---

## Critique

### C1 — Une config illisible est écrasée au démarrage : toute la configuration et les clés API sont perdues
- **Où** : `Services/ConfigService.cs:41-45` (corruption avalée → `new AppConfig()`), `Services/ConfigService.cs:48-53` (écriture non atomique), `Models/AppConfig.cs:10` (`Version = 1` par défaut), `Services/ConfigMigrations.cs:127-131`, `App.xaml.cs:65-67`.
- **Description** : `Save` fait un `File.WriteAllText` direct sur `config.json`. Un kill, une coupure de courant ou un disque plein pendant l'écriture laisse un JSON tronqué. Au lancement suivant, `Load` avale la `JsonException` et renvoie une config vierge (`Version = 1`). `ConfigMigrations.Apply` la juge alors « à migrer » (`Version < 2` → `changed = true`) et `App.OnStartup` la **sauvegarde aussitôt** par-dessus le fichier d'origine. Il n'y a pas de sauvegarde, pas de log, pas d'avertissement.
- **Autre déclencheur** : un downgrade. Une config écrite par une version future qui contient une valeur d'enum inconnue (nouveau `SourceKind`, `ThemePreference`…) fait lever `JsonStringEnumConverter`. Le résultat est le même : la config est remise à zéro et réécrite au démarrage.
- **Scénario** : le PC s'éteint pendant la saisie de la clé Wallhaven, qui sauve à chaque frappe (voir m9). Au redémarrage, les écrans, sources, clés API, la langue et le dossier d'enregistrement ont disparu.
- **Correctif** :
  1. Écriture atomique : écrire `config.json.tmp`, puis `File.Replace(tmp, config.json, config.json.bak)` (ou `File.Move(…, overwrite: true)`), comme le fait déjà `WallpaperComposer.SavePng`.
  2. `Load` doit distinguer « absent » de « illisible ». Si le fichier est illisible : le renommer en `config.corrupt-<horodatage>.json`, tenter le `.bak`, logger, et **ne jamais sauvegarder par-dessus**.
  3. Garde de version : si `config.Version` dépasse la version connue, ne rien écrire (mode lecture seule, ou avertissement).

### C2 — Un intervalle de plus de 24,8 jours rend l'application impossible à lancer
- **Où** : `Views/Pages/WallpapersPage.xaml:223-224` (`Maximum="86400"` quelle que soit l'unité), `ViewModels/ScreenSettingsViewModel.cs:295-299`, `Services/SlideshowService.cs:152-155`, `App.xaml.cs:92`. Aucun gestionnaire `DispatcherUnhandledException`.
- **Description** : en unité « minutes », la NumberBox accepte jusqu'à 86 400, soit 60 jours. Le setter `DispatcherTimer.Interval` lève `ArgumentOutOfRangeException` au-delà de `Int32.MaxValue` ms, soit environ 35 791 minutes.
  - Au clic sur **Appliquer** : `_configService.Save(config)` a déjà persisté la valeur, puis `Restart` lève. Le message affiché est « Échec : … », mais la config est écrite et `Stop()` a déjà coupé tous les diaporamas.
  - Au lancement suivant : `App.OnStartup` → `Restart` → exception non gérée dans `OnStartup`, et le process meurt. Cela se reproduit à chaque démarrage, y compris via la clé Run, jusqu'à une édition manuelle de `config.json`.
- **Scénario** : un utilisateur gêné par #41 met l'intervalle à « 40000 minutes ». L'app ne redémarre plus jamais.
- **Correctif** :
  - Borner à la source : dans `ToConfig`, plafonner à 24 jours. Ajuster `Maximum` selon l'unité (`1440*24` en minutes, `86400` en secondes), ou proposer une unité « heures/jours ».
  - Côté `SlideshowService`, plafonner aussi l'intervalle (défense en profondeur sur une config éditée à la main). Isoler chaque `TryAddTarget` dans un try pour qu'une cible invalide ne casse pas les autres.
  - Voir aussi M6 pour le gestionnaire global.

---

## Majeur

### M1 — Deux écrans sur la même source téléchargent le même fichier en concurrence
- **Où** : `Services/Sources/WebWallpaperFetcher.cs:144-151`.
- **Description** : le test `File.Exists(path)` puis `GetByteArrayAsync` puis `WriteAllBytesAsync(path)` n'est pas protégé. Deux cibles qui résolvent la même URL écrivent le même chemin en parallèle (pool de threads, après `ConfigureAwait(false)`).
- **Scénario** : Bing en mode fixe sur 2 écrans, en mode séparé. `Restart` lance les deux `Advance` à la même milliseconde, et leurs timers horaires tombent ensemble.
  - Les deux téléchargements finissent quasi simultanément. Le second `WriteAllBytesAsync` (FileMode.Create) prend une violation de partage, ou bien c'est la lecture `FileShare.Read` du composer sur l'UI thread qui bloque l'écriture. Dans les deux cas : `IOException` → `null` → `WebFetchFailed("Bing")`, et l'écran 2 garde l'ancien fond pendant une heure.
  - Variante : la cible B voit `File.Exists == true` pendant que A écrit encore. B compose un fichier partiel, d'où une exception de décodage ou une image tronquée.
- **Correctif** :
  - Écriture atomique : télécharger vers `path + "." + Guid + ".tmp"`, puis `File.Move(tmp, path, overwrite: false)`, en traitant « déjà présent » comme un succès.
  - Ou dédupliquer les téléchargements en vol avec un `ConcurrentDictionary<string, Lazy<Task<string?>>>` indexé par chemin.

### M2 — Cache web empoisonné : fichier partiel ou non-image servi indéfiniment
- **Où** : `Services/Sources/WebWallpaperFetcher.cs:144-150`.
- **Description** : le fichier est écrit directement à son nom final, sans vérifier le `Content-Type` ni la décodabilité. S'il est partiel (kill pendant l'écriture, annulation pendant `WriteAllBytesAsync`) ou si c'est une page HTML servie en 200 (portail captif, CDN), il reste en cache sous le hash de l'URL. Comme `File.Exists` court-circuite tout nouveau téléchargement, cette URL est cassée jusqu'à la purge (30 fichiers plus récents).
- **Scénario** : Wi-Fi d'hôtel avec portail captif. L'URL Bing du jour est « téléchargée » sous forme de HTML de login. La composition échoue à chaque re-vérification horaire toute la journée, même une fois connecté.
- **Correctif** :
  - Même écriture tmp + move que M1.
  - Vérifier que `response.Content.Headers.ContentType` commence par `image/` (pour les sources custom, tolérer `application/octet-stream`).
  - En cas d'échec de décodage dans `Compose` d'un fichier `webcache`, supprimer ce fichier pour forcer un nouveau téléchargement.

### M3 — Les timeouts HTTP sont avalés comme des annulations : échec silencieux
- **Où** : `Services/Sources/WebWallpaperFetcher.cs:69-72`, `Services/SlideshowService.cs:215-218`.
- **Description** : depuis .NET 5, le `Timeout` (30 s) de `HttpClient` lève `TaskCanceledException`, qui hérite de `OperationCanceledException`. `FetchAsync` la relance, puis `AdvanceWebAsync` l'attrape comme « arrêt du diaporama » sans émettre `WebFetchFailed`.
- **Scénario** : Wallhaven lent ou réseau dégradé. Aucune image ne change et aucun message n'apparaît, ce qui contredit le contrat documenté de `WebFetchFailed`.
- **Correctif** : filtrer sur le token, avec `catch (OperationCanceledException) when (ct.IsCancellationRequested)` aux deux endroits. Le reste suit le chemin d'échec normal.

### M4 — « Appliquer » s'interrompt à la première erreur : config sauvée mais état incohérent
- **Où** : `ViewModels/SettingsViewModel.cs:151-160`, `Services/WallpaperService.cs:35-40`.
- **Description** : `Save`, puis `WallpaperService.Apply` (une boucle sans try par écran), puis `Restart`, le tout dans un seul try. Si un écran en image fixe lève (fichier OneDrive non hydraté, image corrompue, rendu vide issue #35), les écrans suivants ne sont pas posés et **`Restart` n'est pas appelé**. La config est pourtant déjà persistée.
- **Scénario** : écran 1 en image fixe sur un fichier OneDrive en ligne uniquement, écran 2 passé en diaporama. Après Appliquer, l'écran 2 continue l'**ancien** diaporama (ou rien) jusqu'au prochain lancement, alors que l'UI montre la nouvelle config.
- **Correctif** : try/catch par écran dans `Apply`, en collectant les erreurs. Appeler `Restart` dans tous les cas, puis afficher un statut agrégé (« 2 écrans appliqués, 1 en échec : … »).

### M5 — « Enregistrer le fond actuel » est cassé pour Bing (et nomme mal les sources custom)
- **Où** : `Services/ImageSaveService.cs:58-73`, `ImageSaveService.cs:49-50`.
- **Description** : le nom est tiré de `Path.GetFileName(url.AbsolutePath)`. Bing renvoie `"/th?id=OHR.TrailCedars_FR-FR7583904016_1920x1080.jpg&rf=…"` (vérifié en appel réel). `AbsolutePath` vaut donc `/th` et le fichier est enregistré sous **`th`, sans extension**. Le jour suivant, `File.Exists(target)` est vrai : le résultat est `AlreadySaved`, et **aucune autre image Bing ne peut être enregistrée**.
- Même cause pour une source custom de type `https://api/x/get.php?id=…` : le fichier est enregistré en `get.php`.
- **Correctif** :
  - Forcer l'extension réelle du fichier en cache (celle choisie par `GuessExtension`).
  - Pour Bing, extraire le paramètre `id=` de la query quand le leaf n'a pas d'extension image.
  - Rester sur l'horodatage si le leaf n'a pas d'extension de la liste blanche `ImageFiles.Extensions`.
  - Test unitaire de `BuildFileName` recommandé (voir la section Testabilité).

### M6 — Aucune journalisation ni gestionnaire d'exceptions global
- **Où** : `App.xaml.cs` (pas de `DispatcherUnhandledException`, `AppDomain.UnhandledException` ni `TaskScheduler.UnobservedTaskException`). Catch silencieux qui masquent de vrais échecs :
  - `SlideshowService.cs:180-183` (pose locale)
  - `SlideshowService.cs:219-223` (le message est perdu, seul le nom de la source remonte)
  - `SlideshowService.cs:254-257`
  - `WebWallpaperFetcher.cs:73-76` (toute cause réseau/JSON/HTTP 401/429 réduite à `null`)
  - `ConfigService.cs:41-45` (voir C1)
  - `WallpaperService.cs:92-95`
  - `ScreenService.cs:32-35`
- **Description** : un utilisateur qui signale « Wallhaven ne marche pas » ne peut rien fournir. Il est impossible de distinguer un 401 (NSFW sans clé), un 429 (quota), un JSON inattendu ou un timeout. Une exception dans un handler UI (par ex. `RebuildSaveMenu` → `GetMonitors` COM, `App.xaml.cs:183`) tue le process tray sans trace.
- **Correctif** :
  - Un logger fichier minimal (`%LocalAppData%\Muralis\logs\muralis.log`, rotation par taille). #41 prévoit déjà un fichier de log pour les mesures : le mutualiser.
  - Logger avec le type et le message dans chaque catch listé ci-dessus.
  - Ajouter `DispatcherUnhandledException`, qui logge et marque l'exception comme gérée quand c'est possible, plus `UnobservedTaskException`.
  - Exposer le dernier message d'erreur de la source dans `WebFetchFailed` (par ex. « HTTP 401 »).

---

## Mineur

### m1 — Fuite d'abonnements à chaque changement de langue
- **Où** : `ViewModels/SettingsViewModel.cs:45-50`, `App.xaml.cs:101-106` et `:132`.
- **Description** : `BuildViewModels` crée un nouveau `SettingsViewModel` abonné à `SlideshowService.WallpaperApplied` et `WebFetchFailed`. L'ancien n'est jamais désabonné : le service (qui vit toute la durée de l'app) le garde en vie. Chaque bascule de langue ajoute un VM zombie qui, à chaque pose, refait un `GetWallpaper` COM et décode une miniature par écran.
- **Correctif** : `SettingsViewModel : IDisposable` qui se désabonne. `App.ApplyLanguage` dispose l'ancien VM avant d'en reconstruire un.

### m2 — Calcul de miniatures sur l'UI thread, même fenêtre masquée
- **Où** : `ViewModels/SettingsViewModel.cs:190-200`, `ViewModels/ScreenSettingsViewModel.cs:348-368` et `:264`, `:370-389`.
- **Description** : à chaque pose, pour chaque écran : un `GetWallpaper` COM plus un `BitmapImage` `OnLoad` décodé de façon synchrone (PNG 4K composé, `DecodePixelWidth` ne réduit que la sortie). Cela se produit même quand la fenêtre de settings est masquée, c'est-à-dire presque toujours.
- `OnFolderPathChanged` énumère et décode 8 images sur l'UI thread : un dossier sur un partage réseau lent gèle la fenêtre.
- **Correctif** : ne rafraîchir que si la fenêtre est visible (ou rafraîchir à l'affichage). Décoder les miniatures en tâche de fond (`Task.Run`, `Freeze`, puis assignation sur le Dispatcher).
- **Note** : complémentaire de #41 (qui vise compose et `SetWallpaper`, pas l'aperçu).

### m3 — `GetMonitors()` (COM) appelé à chaque pose malgré le throttle
- **Où** : `App.xaml.cs:85-86`.
- **Description** : l'argument `_screenService.GetMonitors()` est évalué **avant** le test de throttle de `PruneComposedIfDue`. Cela crée une instance COM et énumère les moniteurs à chaque tick, pour rien 9 fois sur 10.
- **Correctif** : passer un `Func<IReadOnlyList<MonitorInfo>>`, ou faire le test de throttle dans le lambda.

### m4 — Une exception d'un abonné est maquillée en « échec de source »
- **Où** : `Services/SlideshowService.cs:177-183` et `:211-223`.
- **Description** : `WallpaperApplied?.Invoke()` est **dans** le try. Si un abonné lève (miniature, `GetMonitors` de la purge…), une pose **réussie** est signalée « Impossible de récupérer une image de X » (web) ou avalée (local).
- **Correctif** : invoquer l'événement hors du try, avec son propre try/log.

### m5 — Interop COM : instance recréée à chaque appel, jamais libérée, `SetPosition` systématique
- **Où** : `Services/WallpaperService.cs:49-50`, `:63`, `:88`, `Services/ScreenService.cs:14`.
- **Description** : chaque pose, chaque aperçu et chaque énumération fait un `CoCreateInstance`. Les RCW ne sont libérés qu'au GC (aucun `Marshal.ReleaseComObject`/`FinalReleaseComObject`). `ApplyMonitor` appelle `SetPosition(Fill)` à **chaque** pose, même si la position vaut déjà Fill, et **avant** `Compose` : si la composition lève, la position globale a quand même changé.
- **Correctif** :
  - Encapsuler l'accès dans un `DesktopWallpaperSession : IDisposable` (créé par opération, `FinalReleaseComObject` dans `Dispose`).
  - Recréer l'instance sur `COMException` RPC_E_DISCONNECTED (redémarrage d'Explorer) : c'est pourquoi un singleton naïf serait une régression.
  - `SetPosition` seulement si `GetPosition() != Fill`, et après `Compose`.
- **Note** : ce dernier point peut économiser un rafraîchissement de bureau par pose. À rattacher à #41 (mesure).

### m6 — Une source supprimée continue d'être interrogée
- **Où** : `ViewModels/SourcesViewModel.cs:212-224`.
- **Description** : `Remove` met à jour la config et les listes d'éditeur, mais les cibles du `SlideshowService` en cours gardent leur copie de la source jusqu'au prochain `Restart`. `config.Screens` référence encore l'Id supprimé tant que l'utilisateur n'a pas cliqué Appliquer, alors que l'éditeur affiche déjà « Dossier local ».
- **Scénario** : on retire une source Wallhaven NSFW. Les images continuent d'arriver jusqu'au prochain Appliquer ou redémarrage.
- **Correctif** : après `Remove` (et `Update`), redémarrer les cibles concernées (`SlideshowService.Restart(_configService.Load(), monitors)`), ou exposer `SlideshowService.RemoveSource(id)`.

### m7 — Aucune limite de taille au téléchargement
- **Où** : `Services/Sources/WebWallpaperFetcher.cs:146`.
- **Description** : `GetByteArrayAsync` met tout en mémoire, jusqu'à `MaxResponseContentBufferSize` (2 Go par défaut). Une source custom mal configurée qui pointe vers un gros binaire provoque un pic mémoire, borné seulement par le timeout de 30 s.
- **Correctif** : `HttpCompletionOption.ResponseHeadersRead`, rejet si `Content-Length` dépasse environ 50 Mo, copie en flux vers le fichier tmp (à combiner avec M1/M2).

### m8 — Clé API relue sur disque à chaque requête, sur l'UI thread
- **Où** : `Services/Sources/WebWallpaperFetcher.cs:170-178`, appelé de façon synchrone avant le premier `await` (`:65`, `:111`).
- **Description** : `_configService.Load()` (lecture disque + désérialisation JSON + DPAPI) à chaque tick web, sur l'UI thread.
- **Correctif** : cache de config en mémoire dans `ConfigService` (invalidé par `Save`), ou passer la clé résolue au moment du `Restart`.

### m9 — La clé Wallhaven est sauvegardée à chaque frappe
- **Où** : `Views/Pages/AppSettingsPage.xaml:116` (`UpdateSourceTrigger=PropertyChanged`), `ViewModels/AppSettingsViewModel.cs:91-102`.
- **Description** : chaque caractère déclenche Load, `ProtectedData.Protect` et une réécriture complète de `config.json`, non atomique (voir C1). Une requête Wallhaven qui part pendant la saisie utilise une clé partielle et prend un 401.
- **Correctif** : `UpdateSourceTrigger=LostFocus` ou un debounce d'environ 500 ms.

### m10 — Des messages techniques en français s'affichent dans une UI anglaise
- **Où** : `Services/WallpaperComposer.cs:59-60`, affiché via `ViewModels/SettingsViewModel.cs:164` (`Status_FailedFormat, ex.Message`). Même logique pour `App.xaml.cs:223`.
- **Description** : en anglais, un rendu vide affiche « Failed: Rendu vide pour '…' : RenderTargetBitmap n'a rien produit. ». Cela contredit la règle de localisation d'AGENTS.md.
- **Correctif** : garder des messages d'exception techniques (logués, voir M6) et afficher à l'utilisateur une chaîne resx générique, par exemple `Status_ApplyFailed` plus le nom du fichier.

### m11 — Non-conformités DESIGN.md
- `Views/Pages/SourcesPage.xaml:108-110` : `CheckBox` pour « Adapter à l'écran », un réglage on/off unique. DESIGN.md réserve la CheckBox aux sélections multiples : il faut un `ui:ToggleSwitch`. Les CheckBox catégories/pureté (`:87-99`) sont conformes (multi-sélection).
- `Views/Pages/WallpapersPage.xaml:60` : `FontSize="12"` en dur sur l'icône du bouton d'enregistrement. Le principe 4 interdit les tailles en dur. Utiliser `ui:SymbolIcon` (`SymbolRegular.ArrowDownload16`) ou une ressource de taille d'icône.
- `Views/Pages/WallpapersPage.xaml:224` : voir C2, le `Maximum` doit dépendre de l'unité.

---

## Suggestions

- **s1 — `CancellationTokenSource` jamais disposé** (`SlideshowService.cs:25`, `:53-54`). Sans `CancelAfter` ni `WaitHandle`, l'impact est négligeable, mais `_cts.Cancel(); _cts.Dispose();` dans `Stop` coûte une ligne. Les CTS des requêtes en vol restent valides car elles ont capturé le token, pas la source : vérifier qu'aucune ne lit `_cts.Token` après `Stop`. Aujourd'hui `Advance` lit `_cts.Token` au moment du tick, ce qui est correct.
- **s2 — `_urlByPath` grossit sans borne** (`WebWallpaperFetcher.cs:40`, `:154-155`) : une entrée par image servie et par session. C'est négligeable (environ 50 entrées/jour à 30 min), mais un LRU (ou un fichier `.url` à côté du cache) rendrait aussi le nom d'origine disponible après redémarrage, ce qui corrige le repli « horodatage » d'`ImageSaveService`.
- **s3 — User-Agent figé `Muralis/1.0`** (`App.xaml.cs:56`) alors que la version est 1.2.2 : utiliser la version de l'assembly et l'URL du repo (`Muralis/1.2.2 (+https://github.com/aetherishq/muralis)`), comme le recommande Wallhaven.
- **s4 — DPAPI sans entropie** (`ApiKeyProtector.cs:17`, `:26`) : tout process de l'utilisateur peut déchiffrer. Une entropie applicative constante ne résiste pas à un attaquant déterminé mais évite le déchiffrement opportuniste par un outil générique. C'est un gain faible : à traiter avec une migration (blobs actuels déchiffrés sans entropie puis re-chiffrés).
- **s5 — Fire-and-forget `_ = AdvanceWebAsync(...)`** (`SlideshowService.cs:167`) : acceptable, car la méthode est entièrement gardée. Seule une exception levée par un abonné de `WebFetchFailed` dans le `catch` deviendrait une exception de tâche non observée et serait perdue. Couvert par M6 (`UnobservedTaskException`) et m4.
- **s6 — `HttpClient` partagé sans `PooledConnectionLifetime`** (`App.xaml.cs:55`) : sur une app tray qui tourne des semaines, un `SocketsHttpHandler { PooledConnectionLifetime = 15 min }` évite de garder une résolution DNS périmée.

### Points vérifiés et sains (pas d'action)
- **Chemins venant du réseau** : le nom de cache est un hash SHA-1 avec une extension en liste blanche (`WebWallpaperFetcher.cs:140-141`, `:186-190`), le dossier de source est assaini (`:180-184`), et le nom d'enregistrement passe par `GetFileName` + caractères invalides remplacés (`ImageSaveService.cs:60-67`). Il n'y a pas de traversée de chemin possible. Une URL d'image `file://` renvoyée par une source custom est rejetée par `HttpClient` (schéma non supporté).
- **Quota Wallhaven** : page de 24 résultats par couple (source, écran), plancher de 60 s par cible, rafraîchissement seulement à épuisement, péremption (1 h) ou changement de filtres. Cela reste largement sous 45 req/min même avec plusieurs écrans. Un 429 suit le chemin d'échec normal, sans backoff : acceptable avec le plancher de 60 s.
- **JSON inattendu** : `GetProperty`/`EnumerateArray`/`Navigate` lèvent, ce qui est capturé et transformé en `null` puis en `WebFetchFailed`. Pas de crash (il manque seulement le log, voir M6).
- **Courses entre timers** : tous les `DispatcherTimer` tournent sur l'UI thread, les reprises d'`await` côté `SlideshowService` reviennent sur l'UI thread, et `FetchInProgress` empêche le chevauchement par cible. Il n'y a pas de course de données. La seule concurrence réelle est côté disque (M1).
- **Cache composé** : écriture atomique, plafond de 500 Mo, éviction des plus anciens en excluant les fonds posés, purge à 30 jours. Les `.tmp` orphelins sont repris par l'âge et le plafond.
- **Démarrage Windows** : conforme à AGENTS.md (état relu depuis le registre).
- **Clés resx** : `Strings.resx` et `Strings.fr.resx` ont exactement le même jeu de clés.

---

## Testabilité et préparation de `Muralis.Core`

Il n'existe aujourd'hui **aucun projet de test**. Priorités, de la plus rentable à la moins rentable :

| # | Cible | Pourquoi | Effort |
|---|---|---|---|
| 1 | `ConfigMigrations.Apply` | Statique et pure, logique de migration critique (C1), fixtures JSON v1/v1.1/v2 faciles à figer | Faible |
| 2 | `ConfigService` (round-trip, fichier corrompu, écriture atomique, version future) | Régression C1 | Faible (injecter le dossier racine) |
| 3 | `WallhavenUrlBuilder.Build` / `BuildTagsQuery`, `HttpJsonSource.Navigate` | Pures, déjà `internal static` (ajouter `InternalsVisibleTo`) | Très faible |
| 4 | `ImageSaveService.BuildFileName` | Aurait détecté M5 | Faible (extraire une fonction pure `(Uri?, cachePath) → string`) |
| 5 | Ordonnancement du diaporama (`RefillQueue`/`NextLocalImage`) | Shuffle sans répétition en tête, reprise alphabétique après la dernière image | Moyen : extraire un `SlideshowQueue` pur qui prend une liste de fichiers et un `Random` |
| 6 | Politique d'éviction du cache (`PruneComposed`) | Invariant « ne jamais supprimer un fond posé » | Moyen : extraire `SelectEvictions(IEnumerable<(path,size,mtime)>, applied, cap)` pure |
| 7 | Géométrie du compositeur (Fill/Fit/Center) | Calcul de rectangles séparable du rendu WPF | Faible : extraire `ComputeDestRect(sw, sh, tw, th, mode)` |
| 8 | `WebWallpaperFetcher` (cache, page Wallhaven, M1/M2/M3) | Concurrence et timeouts | Moyen : injecter un `HttpMessageHandler` de test, `TimeProvider`, et la racine du cache |
| 9 | `ScreenSettingsViewModel.ToConfig`/`LoadFrom` (conversions d'unités, bornes C2) | Régression C2 | Faible (dépend de `Strings` pour les libellés d'unité : comparer sur une enum plutôt que sur le libellé localisé) |

### Découpage proposé (prépare `Muralis.Core` multiplateforme, `net10.0` sans `-windows`)

**Déplaçable tel quel ou presque dans Core :**
- `Models/*`, avec deux réserves :
  - `MonitorInfo` dépend de `Interop.RECT` et `System.Windows.Point` : introduire un `record struct PixelRect` neutre.
  - `WallhavenSourceOptions : ObservableObject` mêle modèle persisté et UI. CommunityToolkit.Mvvm est multiplateforme, donc tolérable, mais un DTO + VM séparé serait plus propre.
- `ConfigMigrations` : il référence `SlideshowService.LocalFolderSourceType` (constante à déplacer vers `ScreenConfig`, à côté de `LocalFileSourceType`).
- `ConfigService`, avec la racine de données injectée.
- `Sources/*` : `WallhavenUrlBuilder`, `HttpJsonSource`, `WebWallpaperFetcher`. `SourcePresets` dépend de `Strings` uniquement pour `Note` : garder `NoteKey` dans Core et résoudre le texte côté UI.

**Interfaces à introduire (implémentées côté Windows) :**
- `IDesktopBackend` : `GetMonitors()`, `SetWallpaper(deviceId, path)`, `GetWallpaper(deviceId)`, `SetPosition(...)`. Il remplace `ScreenService` et la partie COM de `WallpaperService`, et rend `SlideshowService` et `CacheMaintenanceService` testables avec un faux.
- `IImageComposer` : implémentation WPF actuelle. Le futur Core pourrait en fournir une à base d'ImageSharp/SkiaSharp, ce qui règle aussi le problème STA de #41 point 3.
- `ISecretProtector` : DPAPI sous Windows, keychain/libsecret ailleurs. Il remplace l'appel statique `ApiKeyProtector`.
- Timer : remplacer `DispatcherTimer` dans `SlideshowService` par `TimeProvider` + `ITimer` (.NET 8+), avec un `SynchronizationContext` injecté pour le marshalling. Tests déterministes avec `FakeTimeProvider`.
- `ILogger` (Microsoft.Extensions.Logging.Abstractions, léger), pour M6.

**Reste dans `Muralis` (Windows/WPF)** : App, Views, ViewModels, Interop COM, `StartupService` (registre), `ThemeService`, `LocalizationService` (registre de l'installeur), implémentations des interfaces ci-dessus.

---

## Proposition de regroupement en issues GitHub (à arbitrer, non créées)

| Issue proposée | Constats | Priorité | Note |
|---|---|---|---|
| **Config : écriture atomique, sauvegarde, refus d'écraser une config illisible** | C1, m9, m8 (cache mémoire de config) | P0 | Avec tests `ConfigService` + `ConfigMigrations` |
| **Intervalle > 24,8 jours : crash permanent au démarrage** | C2, m11 (`Maximum` NumberBox) | P0 | Correctif court, à sortir en patch 1.2.3 avec l'issue config |
| **Robustesse du cache web : concurrence, écriture atomique, validation, taille** | M1, M2, m7 | P1 | Un seul chantier dans `DownloadAsync` |
| **Journalisation et gestion globale des exceptions** | M6, M3, m4, m10, s5 | P1 | Mutualiser le fichier de log avec #41 point 1 |
| **Appliquer : échec partiel par écran, redémarrage des diaporamas garanti** | M4, m6 | P1 | |
| **« Enregistrer le fond actuel » : nom de fichier Bing / sources custom** | M5, s2 | P1 | Bug visible utilisateur, petit correctif + test |
| **UI : fuite d'abonnements au changement de langue, miniatures hors UI thread** | m1, m2, m3 | P2 | Rejoint l'esprit de #41 (UI jamais gelée) sans le dupliquer |
| **Interop COM : session disposable, `SetPosition` conditionnel** | m5 | P2 | **À rattacher à #41** (commentaire) plutôt qu'en issue séparée : impact possible sur le nombre de rafraîchissements |
| **Conformité DESIGN.md** | m11 (CheckBox AutoFit, FontSize en dur) | P3 | |
| **Projet de tests + extraction `Muralis.Core`** | Section Testabilité, s1, s3, s4, s6 | P2 (tests) / P3 (Core) | À découper en 2 : (a) projet `Muralis.Tests` + tests des fonctions pures 1-4, 7 ; (b) interfaces `IDesktopBackend`/`IImageComposer`/`ISecretProtector`/`TimeProvider` puis Core |
