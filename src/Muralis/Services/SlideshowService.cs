using System.IO;
using System.Windows.Threading;
using Muralis.Models;
using Muralis.Resources;
using Muralis.Services.Sources;

namespace Muralis.Services;

/// <summary>
/// Fait tourner les diaporamas : une cible par écran en mode <see cref="WallpaperMode.Slideshow"/>
/// (ou une seule cible en mode unifié), chacune avec son propre <see cref="DispatcherTimer"/>.
/// Deux types de source : dossier local (« LocalFolder » — cycle aléatoire sans répétition ou
/// alphabétique selon <see cref="ScreenConfig.Shuffle"/>, dossier ré-énuméré à chaque cycle) ou
/// source web (le <see cref="ScreenConfig.SourceType"/> référence une
/// <see cref="WallpaperSourceConfig"/> par Id : chaque tick télécharge une nouvelle image via
/// <see cref="WebWallpaperFetcher"/>, sans jamais bloquer l'UI ; en cas d'échec le fond courant
/// est conservé et on retente au tick suivant).
/// </summary>
public class SlideshowService
{
    public const string LocalFolderSourceType = "LocalFolder";

    private readonly WallpaperService _wallpaperService;
    private readonly WebWallpaperFetcher _fetcher;
    private readonly List<SlideshowTarget> _targets = [];
    private CancellationTokenSource _cts = new();

    /// <summary>Vrai quand une pose doit attendre (jeu plein écran au premier plan, #41).</summary>
    private readonly Func<bool> _shouldDefer;

    /// <summary>Re-vérification des poses en attente : active seulement tant qu'il y en a.</summary>
    private DispatcherTimer? _deferTimer;

    internal static readonly TimeSpan DeferRecheckInterval = TimeSpan.FromSeconds(30);

    public SlideshowService(WallpaperService wallpaperService, WebWallpaperFetcher fetcher, Func<bool>? shouldDefer = null)
    {
        _wallpaperService = wallpaperService;
        _fetcher = fetcher;
        _shouldDefer = shouldDefer ?? (() => false);
    }

    /// <summary>
    /// Levé après chaque pose effective d'une image (toujours sur l'UI thread). Permet à l'UI
    /// de rafraîchir ses aperçus « fond appliqué » au bon moment — la première pose d'un
    /// diaporama web arrive bien après le <see cref="Restart"/> (téléchargement asynchrone).
    /// </summary>
    public event Action? WallpaperApplied;

    /// <summary>
    /// Levé quand une source web n'a pas pu fournir d'image (réseau, API en erreur, config
    /// invalide…), avec le nom de la source et un motif court localisé. Toujours sur l'UI
    /// thread. Le fond courant est conservé et une nouvelle tentative aura lieu au tick suivant.
    /// </summary>
    public event Action<string, string>? WebFetchFailed;

    /// <summary>
    /// Arrête les diaporamas en cours puis relance ceux décrits par la config.
    /// Chaque cible démarrée applique immédiatement une première image.
    /// </summary>
    public void Restart(AppConfig config, IReadOnlyList<MonitorInfo> monitors)
    {
        Stop();
        _cts = new CancellationTokenSource();

        if (config.Unified)
        {
            var unified = config.UnifiedConfig;
            // Pas d'écran destinataire unique : l'adaptation Wallhaven par écran est omise.
            AddTargetSafely(config, unified, monitor: null,
                path => _wallpaperService.ApplyAllMonitorsAsync(path, unified.DisplayMode, monitors));
            return;
        }

        foreach (var monitor in monitors)
        {
            var screen = config.FindScreen(monitor.DeviceId);
            if (screen is not null)
            {
                var target = monitor;
                AddTargetSafely(config, screen, monitor,
                    path => _wallpaperService.ApplyMonitorAsync(target, path, screen.DisplayMode));
            }
        }
    }

    /// <summary>
    /// Une cible invalide (config éditée à la main, valeur hors bornes…) ne doit ni bloquer
    /// les autres cibles ni faire lever <see cref="Restart"/> — appelé au démarrage, hors de
    /// tout try : une exception y tuait le process à chaque lancement (issue #44).
    /// </summary>
    private void AddTargetSafely(AppConfig config, ScreenConfig screen, MonitorInfo? monitor, Func<string, Task> apply)
    {
        try
        {
            TryAddTarget(config, screen, monitor, apply);
        }
        catch (Exception ex)
        {
            Log.Error($"Slideshow target ignored (screen '{screen.DeviceId}', source '{screen.SourceType}')", ex);
        }
    }

    /// <summary>
    /// Intervalle effectif d'un diaporama : plancher de sécurité (composition locale 5 s,
    /// requêtes web 60 s) et plafond <see cref="ScreenConfig.MaxSlideshowInterval"/>
    /// (limite de <see cref="DispatcherTimer"/>, issue #44).
    /// </summary>
    internal static TimeSpan EffectiveInterval(TimeSpan requested, bool isWeb)
    {
        var min = isWeb ? TimeSpan.FromMinutes(1) : ScreenConfig.MinSlideshowInterval;
        if (requested < min)
            return min;
        return requested > ScreenConfig.MaxSlideshowInterval ? ScreenConfig.MaxSlideshowInterval : requested;
    }

    /// <summary>Chemin (cache local) de l'image web actuellement posée sur un écran —
    /// null si la cible n'est pas web ou n'a pas encore servi d'image. DeviceId vide :
    /// cible unifiée.</summary>
    public string? CurrentWebImage(string deviceId) =>
        _targets.FirstOrDefault(t => t.WebSource is not null && t.Config.DeviceId == deviceId)?.LastImage;

    /// <summary>Cibles web ayant une image posée : (DeviceId — vide en mode unifié, chemin).
    /// Alimente le sous-menu « Enregistrer le fond actuel » du tray.</summary>
    public IReadOnlyList<(string DeviceId, string Path)> CurrentWebImages() =>
        _targets
            .Where(t => t.WebSource is not null && t.LastImage is not null)
            .Select(t => (t.Config.DeviceId, t.LastImage!))
            .ToList();

    /// <summary>Passe immédiatement à l'image suivante sur toutes les cibles (menu tray) —
    /// demande explicite : jamais différée.</summary>
    public void AdvanceAll()
    {
        foreach (var target in _targets)
        {
            // Redémarrer le timer pour que le prochain changement auto reparte d'un intervalle complet.
            target.Timer.Stop();
            target.Pending = false;
            Advance(target, force: true);
            target.Timer.Start();
        }
    }

    public void Stop()
    {
        _deferTimer?.Stop();
        _cts.Cancel();
        foreach (var target in _targets)
            StopTarget(target);
        _targets.Clear();
    }

    /// <summary>
    /// Une source web a été modifiée ou retirée (page Sources) : seules les cibles qui
    /// l'utilisent sont arrêtées puis recréées depuis <paramref name="config"/> — retirée,
    /// elle n'est plus interrogée ; modifiée (filtres Wallhaven, URL…), ses nouveaux réglages
    /// s'appliquent tout de suite. Les autres écrans ne changent pas (issue #47).
    /// </summary>
    public void RefreshSource(AppConfig config, string sourceId)
    {
        var affected = _targets.Where(t => t.WebSource?.Id == sourceId).ToList();
        foreach (var target in affected)
        {
            StopTarget(target);
            _targets.Remove(target);
        }

        // Recréées seulement si la source existe encore et reste valide (TryAddTarget).
        foreach (var target in affected)
            AddTargetSafely(config, target.Config, target.Monitor, target.Apply);
    }

    private static void StopTarget(SlideshowTarget target)
    {
        target.Timer.Stop();
        target.Stopped = true; // un téléchargement en vol ne posera pas son image
    }

    /// <summary>Cadence interne de re-vérification des sources quotidiennes (non configurable).</summary>
    private static readonly TimeSpan DailyRefreshInterval = TimeSpan.FromHours(1);

    private void TryAddTarget(AppConfig config, ScreenConfig screen, MonitorInfo? monitor, Func<string, Task> apply)
    {
        WallpaperSourceConfig? webSource;
        TimeSpan interval;

        if (screen.Mode == WallpaperMode.Slideshow)
        {
            webSource = null;
            if (screen.SourceType == LocalFolderSourceType || string.IsNullOrEmpty(screen.SourceType))
            {
                if (!Directory.Exists(screen.SourcePath))
                    return;
            }
            else
            {
                webSource = config.Sources.FirstOrDefault(s => s.Id == screen.SourceType && s.Kind == SourceKind.Random);
                if (webSource is null || !webSource.IsValid)
                    return; // Source supprimée, incomplète ou non aléatoire : cible ignorée.
            }

            interval = EffectiveInterval(screen.SlideshowInterval, isWeb: webSource is not null);
        }
        else
        {
            // Mode fixe : seule une source quotidienne crée une cible (re-vérification
            // périodique, ré-application uniquement quand l'image du jour change) —
            // un fichier local est statique et posé par WallpaperService.Apply.
            if (screen.SourceType == ScreenConfig.LocalFileSourceType || string.IsNullOrEmpty(screen.SourceType))
                return;

            webSource = config.Sources.FirstOrDefault(s => s.Id == screen.SourceType && s.Kind == SourceKind.Daily);
            if (webSource is null || !webSource.IsValid)
                return;
            interval = DailyRefreshInterval;
        }

        var target = new SlideshowTarget(screen, apply, webSource, monitor)
        {
            Timer = new DispatcherTimer { Interval = interval },
        };
        target.Timer.Tick += (_, _) => Advance(target);
        _targets.Add(target);

        Advance(target);
        target.Timer.Start();
    }

    private void Advance(SlideshowTarget target, bool force = false)
    {
        if (!force && ShouldDefer())
        {
            Defer(target);
            return;
        }

        if (target.WebSource is not null)
            _ = AdvanceWebAsync(target, _cts.Token);
        else
            _ = AdvanceLocalAsync(target);
    }

    private bool ShouldDefer()
    {
        try
        {
            return _shouldDefer();
        }
        catch (Exception ex)
        {
            Log.Warn("Full-screen detection failed: wallpaper change not deferred", ex);
            return false;
        }
    }

    /// <summary>
    /// Pose reportée : une seule en attente par cible (pas d'empilement si plusieurs ticks
    /// tombent pendant le jeu), re-vérifiée toutes les ~30 s, posée dès la sortie du jeu (#41).
    /// </summary>
    private void Defer(SlideshowTarget target)
    {
        target.Pending = true;
        if (_deferTimer is { IsEnabled: true })
            return;

        Log.Info("Wallpaper change deferred: a full-screen application is in the foreground");
        if (_deferTimer is null)
        {
            _deferTimer = new DispatcherTimer { Interval = DeferRecheckInterval };
            _deferTimer.Tick += (_, _) => ResumeDeferred();
        }
        _deferTimer.Start();
    }

    /// <summary>Re-vérification : si plus rien n'empêche la pose, les cibles en attente
    /// avancent. Interne pour les tests (le DispatcherTimer n'y tourne pas).</summary>
    internal void ResumeDeferred()
    {
        if (ShouldDefer())
            return;

        _deferTimer?.Stop();
        var pending = _targets.Where(t => t.Pending).ToList();
        if (pending.Count > 0)
            Log.Info($"Full-screen application left: applying {pending.Count} deferred wallpaper change(s)");
        foreach (var target in pending)
        {
            target.Pending = false;
            Advance(target, force: true);
        }
    }

    private async Task AdvanceLocalAsync(SlideshowTarget target)
    {
        if (target.ApplyInProgress)
            return; // Composition précédente pas finie (intervalle très court) : on saute.

        string? next = NextLocalImage(target);
        if (next is null)
            return;

        target.ApplyInProgress = true;
        try
        {
            await target.Apply(next);
        }
        catch (Exception ex)
        {
            // Image illisible/corrompue : on laisse le tick suivant tenter la prochaine.
            Log.Warn($"Slideshow: could not apply '{next}' (screen '{target.Config.DeviceId}')", ex);
            return;
        }
        finally
        {
            target.ApplyInProgress = false;
        }
        if (!target.Stopped)
            RaiseWallpaperApplied();
    }

    /// <summary>
    /// Notifie une pose réussie. Hors du try de la pose : un abonné qui lève ne doit ni
    /// faire passer une pose réussie pour un échec de source, ni casser le diaporama (#46).
    /// </summary>
    private void RaiseWallpaperApplied()
    {
        try
        {
            WallpaperApplied?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("WallpaperApplied handler failed", ex);
        }
    }

    /// <summary>Échecs de décodage WPF (format non reconnu, fichier tronqué) — par
    /// opposition aux erreurs COM de pose ou de rendu, qui ne mettent pas le fichier en cause.</summary>
    internal static bool IsUndecodableImage(Exception ex) =>
        ex is NotSupportedException or FileFormatException or EndOfStreamException or ArgumentException;

    private void RaiseWebFetchFailed(string sourceName, string reason)
    {
        try
        {
            WebFetchFailed?.Invoke(sourceName, reason);
        }
        catch (Exception ex)
        {
            Log.Error("WebFetchFailed handler failed", ex);
        }
    }

    private async Task AdvanceWebAsync(SlideshowTarget target, CancellationToken ct)
    {
        if (target.FetchInProgress)
            return; // Téléchargement précédent pas fini (intervalle court/réseau lent) : on saute.

        target.FetchInProgress = true;
        try
        {
            // Pas de ConfigureAwait(false) : Apply (imaging WPF + COM) doit reprendre sur l'UI thread.
            var result = await _fetcher.FetchAsync(target.WebSource!, target.Monitor, ct);
            if (ct.IsCancellationRequested || target.Stopped)
                return;

            if (result.Path is not { } path)
            {
                RaiseWebFetchFailed(target.WebSource!.Name, result.FailureReason ?? Strings.FetchFailure_Other);
                return;
            }

            // Le cache est nommé par hash d'URL : même chemin = même image (source
            // quotidienne pas encore renouvelée, ou doublon d'une source aléatoire) —
            // inutile de recomposer/re-poser.
            if (path == target.LastImage)
                return;

            try
            {
                await target.Apply(path);
            }
            catch (Exception ex)
            {
                // Image téléchargée mais inutilisable : fond courant conservé, retentative au prochain tick.
                Log.Warn($"Source '{target.WebSource!.Name}': could not apply '{path}'", ex);
                // Fichier indécodable : l'oublier, sinon le cache le resservirait (même URL)
                // indéfiniment (#45). Une erreur COM ou un rendu vide (#35) ne met pas le
                // fichier en cause : on le garde.
                if (IsUndecodableImage(ex))
                    _fetcher.Invalidate(path);
                RaiseWebFetchFailed(target.WebSource!.Name, Strings.FetchFailure_InvalidImage);
                return;
            }
            target.LastImage = path;
            RaiseWallpaperApplied();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Arrêt/redémarrage du diaporama : silencieux.
        }
        catch (Exception ex)
        {
            // Filet : FetchAsync ne lève qu'en annulation, mais ce Task n'est pas observé
            // (fire-and-forget) — rien ne doit s'en échapper.
            Log.Error($"Source '{target.WebSource!.Name}': unexpected failure", ex);
            RaiseWebFetchFailed(target.WebSource!.Name, Strings.FetchFailure_Other);
        }
        finally
        {
            target.FetchInProgress = false;
        }
    }

    private static string? NextLocalImage(SlideshowTarget target)
    {
        if (target.Queue.Count == 0)
            RefillQueue(target);

        // Les fichiers peuvent disparaître entre l'énumération et leur tour dans le cycle.
        while (target.Queue.TryDequeue(out string? candidate))
        {
            if (File.Exists(candidate))
            {
                target.LastImage = candidate;
                return candidate;
            }
        }
        return null;
    }

    private static void RefillQueue(SlideshowTarget target)
    {
        List<string> files;
        try
        {
            files = ImageFiles.Enumerate(target.Config.SourcePath).ToList();
        }
        catch (Exception ex)
        {
            // Dossier supprimé/inaccessible : cycle vide, on retentera au prochain tick.
            Log.Warn($"Slideshow: cannot list folder '{target.Config.SourcePath}'", ex);
            return;
        }

        if (target.Config.Shuffle)
        {
            for (int i = files.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (files[i], files[j]) = (files[j], files[i]);
            }

            // Éviter de rejouer la dernière image du cycle précédent en tête du nouveau.
            if (files.Count > 1 && files[0] == target.LastImage)
                (files[0], files[^1]) = (files[^1], files[0]);
        }
        else
        {
            files.Sort(StringComparer.OrdinalIgnoreCase);

            // Reprendre le parcours juste après la dernière image servie (utile quand le
            // contenu du dossier a changé en cours de cycle).
            int lastIndex = target.LastImage is null
                ? -1
                : files.FindIndex(f => string.Equals(f, target.LastImage, StringComparison.OrdinalIgnoreCase));
            if (lastIndex >= 0)
                files = [.. files[(lastIndex + 1)..], .. files[..(lastIndex + 1)]];
        }

        foreach (var file in files)
            target.Queue.Enqueue(file);
    }

    private sealed class SlideshowTarget(ScreenConfig config, Func<string, Task> apply, WallpaperSourceConfig? webSource, MonitorInfo? monitor)
    {
        public ScreenConfig Config { get; } = config;
        public Func<string, Task> Apply { get; } = apply;
        public WallpaperSourceConfig? WebSource { get; } = webSource;

        /// <summary>Écran destinataire (null en mode unifié) — transmis au fetcher pour
        /// l'adaptation Wallhaven par écran.</summary>
        public MonitorInfo? Monitor { get; } = monitor;
        public required DispatcherTimer Timer { get; init; }
        public Queue<string> Queue { get; } = new();
        public string? LastImage { get; set; }
        public bool FetchInProgress { get; set; }
        public bool ApplyInProgress { get; set; }

        /// <summary>Un changement attend la sortie d'une application plein écran (#41).</summary>
        public bool Pending { get; set; }

        /// <summary>Cible retirée (Stop, RefreshSource) : un fetch encore en vol l'ignore.</summary>
        public bool Stopped { get; set; }
    }
}
