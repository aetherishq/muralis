using System.IO;
using System.Windows.Threading;
using Muralis.Models;
using Muralis.Services.Sources;

namespace Muralis.Services;

/// <summary>
/// Fait tourner les diaporamas : une cible par écran en mode <see cref="WallpaperMode.Slideshow"/>
/// (ou une seule cible en mode unifié), chacune avec son propre <see cref="DispatcherTimer"/>.
/// Deux types de source : dossier local (« LocalFolder » — cycle aléatoire sans répétition ou
/// alphabétique selon <see cref="ScreenConfig.Shuffle"/>, dossier ré-énuméré à chaque cycle) ou
/// source web (le <see cref="ScreenConfig.SourceType"/> référence une
/// <see cref="WallpaperSourceConfig"/> par nom : chaque tick télécharge une nouvelle image via
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

    public SlideshowService(WallpaperService wallpaperService, WebWallpaperFetcher fetcher)
    {
        _wallpaperService = wallpaperService;
        _fetcher = fetcher;
    }

    /// <summary>
    /// Levé après chaque pose effective d'une image (toujours sur l'UI thread). Permet à l'UI
    /// de rafraîchir ses aperçus « fond appliqué » au bon moment — la première pose d'un
    /// diaporama web arrive bien après le <see cref="Restart"/> (téléchargement asynchrone).
    /// </summary>
    public event Action? WallpaperApplied;

    /// <summary>
    /// Levé quand une source web n'a pas pu fournir d'image (réseau, API en erreur, config
    /// invalide…), avec le nom de la source. Toujours sur l'UI thread. Le fond courant est
    /// conservé et une nouvelle tentative aura lieu au tick suivant.
    /// </summary>
    public event Action<string>? WebFetchFailed;

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
            TryAddTarget(config, unified,
                path => _wallpaperService.ApplyAllMonitors(path, unified.DisplayMode, monitors));
            return;
        }

        foreach (var monitor in monitors)
        {
            var screen = config.FindScreen(monitor.DeviceId);
            if (screen is not null)
            {
                var target = monitor;
                TryAddTarget(config, screen,
                    path => _wallpaperService.ApplyMonitor(target, path, screen.DisplayMode));
            }
        }
    }

    /// <summary>Passe immédiatement à l'image suivante sur toutes les cibles (menu tray).</summary>
    public void AdvanceAll()
    {
        foreach (var target in _targets)
        {
            // Redémarrer le timer pour que le prochain changement auto reparte d'un intervalle complet.
            target.Timer.Stop();
            Advance(target);
            target.Timer.Start();
        }
    }

    public void Stop()
    {
        _cts.Cancel();
        foreach (var target in _targets)
            target.Timer.Stop();
        _targets.Clear();
    }

    private void TryAddTarget(AppConfig config, ScreenConfig screen, Action<string> apply)
    {
        if (screen.Mode != WallpaperMode.Slideshow)
            return;

        WallpaperSourceConfig? webSource = null;
        if (screen.SourceType == LocalFolderSourceType || string.IsNullOrEmpty(screen.SourceType))
        {
            if (!Directory.Exists(screen.SourcePath))
                return;
        }
        else
        {
            webSource = config.Sources.FirstOrDefault(s => s.Name == screen.SourceType);
            if (webSource is null || !webSource.IsValid)
                return; // Source supprimée ou incomplète : cible ignorée.
        }

        // Plancher de sécurité : composition locale à 5 s min, requêtes web à 60 s min.
        var minInterval = webSource is null ? TimeSpan.FromSeconds(5) : TimeSpan.FromMinutes(1);
        var interval = screen.SlideshowInterval < minInterval ? minInterval : screen.SlideshowInterval;

        var target = new SlideshowTarget(screen, apply, webSource)
        {
            Timer = new DispatcherTimer { Interval = interval },
        };
        target.Timer.Tick += (_, _) => Advance(target);
        _targets.Add(target);

        Advance(target);
        target.Timer.Start();
    }

    private void Advance(SlideshowTarget target)
    {
        if (target.WebSource is not null)
        {
            _ = AdvanceWebAsync(target, _cts.Token);
            return;
        }

        string? next = NextLocalImage(target);
        if (next is null)
            return;

        try
        {
            target.Apply(next);
            WallpaperApplied?.Invoke();
        }
        catch (Exception)
        {
            // Image illisible/corrompue : on laisse le tick suivant tenter la prochaine.
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
            string? path = await _fetcher.FetchAsync(target.WebSource!, ct);
            if (ct.IsCancellationRequested)
                return;

            if (path is null)
            {
                WebFetchFailed?.Invoke(target.WebSource!.Name);
                return;
            }

            target.Apply(path);
            WallpaperApplied?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Arrêt/redémarrage du diaporama : silencieux.
        }
        catch (Exception)
        {
            // Échec d'application : fond courant conservé, retentative au prochain tick.
            WebFetchFailed?.Invoke(target.WebSource!.Name);
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
        catch (Exception)
        {
            return; // Dossier supprimé/inaccessible : cycle vide, on retentera au prochain tick.
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

    private sealed class SlideshowTarget(ScreenConfig config, Action<string> apply, WallpaperSourceConfig? webSource)
    {
        public ScreenConfig Config { get; } = config;
        public Action<string> Apply { get; } = apply;
        public WallpaperSourceConfig? WebSource { get; } = webSource;
        public required DispatcherTimer Timer { get; init; }
        public Queue<string> Queue { get; } = new();
        public string? LastImage { get; set; }
        public bool FetchInProgress { get; set; }
    }
}
