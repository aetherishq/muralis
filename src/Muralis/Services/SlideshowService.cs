using System.IO;
using System.Windows.Threading;
using Muralis.Models;

namespace Muralis.Services;

/// <summary>
/// Fait tourner les diaporamas locaux : une cible par écran en mode
/// <see cref="WallpaperMode.Slideshow"/> (ou une seule cible en mode unifié), chacune avec son
/// propre <see cref="DispatcherTimer"/> et son intervalle. Selon <see cref="ScreenConfig.Shuffle"/>,
/// les images sont servies en ordre aléatoire sans répétition à l'intérieur d'un cycle, ou en ordre
/// alphabétique. Le dossier est ré-énuméré à chaque nouveau cycle, donc ajouts/suppressions sont
/// pris en compte sans redémarrage.
/// </summary>
public class SlideshowService
{
    private readonly WallpaperService _wallpaperService;
    private readonly List<SlideshowTarget> _targets = [];

    public SlideshowService(WallpaperService wallpaperService)
    {
        _wallpaperService = wallpaperService;
    }

    /// <summary>
    /// Arrête les diaporamas en cours puis relance ceux décrits par la config.
    /// Chaque cible démarrée applique immédiatement une première image.
    /// </summary>
    public void Restart(AppConfig config, IReadOnlyList<MonitorInfo> monitors)
    {
        Stop();

        if (config.Unified)
        {
            var unified = config.UnifiedConfig;
            if (IsActive(unified))
                AddTarget(unified, path => _wallpaperService.ApplyAllMonitors(path, unified.DisplayMode, monitors));
            return;
        }

        foreach (var monitor in monitors)
        {
            var screen = config.FindScreen(monitor.DeviceId);
            if (screen is not null && IsActive(screen))
            {
                var target = monitor;
                AddTarget(screen, path => _wallpaperService.ApplyMonitor(target, path, screen.DisplayMode));
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
        foreach (var target in _targets)
            target.Timer.Stop();
        _targets.Clear();
    }

    private static bool IsActive(ScreenConfig screen) =>
        screen.Mode == WallpaperMode.Slideshow && Directory.Exists(screen.SourcePath);

    private void AddTarget(ScreenConfig config, Action<string> apply)
    {
        // Plancher de sécurité : chaque changement recompose une image, pas de tick effréné.
        var minInterval = TimeSpan.FromSeconds(5);
        var interval = config.SlideshowInterval < minInterval ? minInterval : config.SlideshowInterval;
        var target = new SlideshowTarget(config, apply)
        {
            Timer = new DispatcherTimer { Interval = interval },
        };
        target.Timer.Tick += (_, _) => Advance(target);
        _targets.Add(target);

        Advance(target);
        target.Timer.Start();
    }

    private static void Advance(SlideshowTarget target)
    {
        string? next = NextImage(target);
        if (next is null)
            return;

        try
        {
            target.Apply(next);
        }
        catch (Exception)
        {
            // Image illisible/corrompue : on laisse le tick suivant tenter la prochaine.
        }
    }

    private static string? NextImage(SlideshowTarget target)
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

    private sealed class SlideshowTarget(ScreenConfig config, Action<string> apply)
    {
        public ScreenConfig Config { get; } = config;
        public Action<string> Apply { get; } = apply;
        public required DispatcherTimer Timer { get; init; }
        public Queue<string> Queue { get; } = new();
        public string? LastImage { get; set; }
    }
}
