using System.IO;
using Muralis.Models;

namespace Muralis.Services;

/// <summary>
/// Entretien des caches disque de <c>%LocalAppData%\Muralis</c>. Deux moments :
/// une passe complète au démarrage (reliquats d'anciennes versions + plafond), puis une
/// passe de plafond après chaque pose de fond, throttlée. Tout est best-effort — un
/// fichier verrouillé sera repris à la passe suivante.
///
/// <b>Invariant : un fichier actuellement posé comme fond n'est jamais supprimé.</b>
/// Windows relit le fichier au verrouillage de session ou au changement de résolution :
/// le supprimer produirait un écran noir (cf. issue #20). Le jeu des fichiers posés est
/// relu via <see cref="WallpaperService.GetWallpaper"/> à chaque passe.
/// </summary>
public class CacheMaintenanceService
{
    /// <summary>Plafond du cache d'images composées (~60-100 images selon les résolutions).</summary>
    private const long MaxComposedBytes = 500L * 1024 * 1024;

    /// <summary>Au-delà de cet âge, un composé non posé est un reliquat (cycle abandonné,
    /// orphelin clé-v1 du fix #20 — indistinguable par nom) : purgé au démarrage.</summary>
    private static readonly TimeSpan MaxComposedAge = TimeSpan.FromDays(30);

    /// <summary>La passe de plafond re-scanne le dossier : inutile à chaque tick d'un
    /// diaporama rapide.</summary>
    private static readonly TimeSpan PruneThrottle = TimeSpan.FromMinutes(10);

    private readonly ConfigService _configService;
    private readonly WallpaperService _wallpaperService;
    private DateTime _lastPruneUtc = DateTime.MinValue;

    public CacheMaintenanceService(ConfigService configService, WallpaperService wallpaperService)
    {
        _configService = configService;
        _wallpaperService = wallpaperService;
    }

    /// <summary>
    /// Passe complète de démarrage : reliquats d'anciennes versions (tray.ico du
    /// générateur d'icône pré-1.2, dossiers webcache de sources supprimées ou nommés par
    /// nom pré-V2, composés de plus de 30 jours — dont les orphelins clé-v1), puis plafond.
    /// </summary>
    public void CleanupAtStartup(AppConfig config, IReadOnlyList<MonitorInfo> monitors)
    {
        TryDelete(Path.Combine(_configService.DataDirectory, "tray.ico"));
        CleanupWebCache(config);

        var applied = AppliedWallpapers(monitors);
        string composedDir = _configService.ComposedDirectory;
        if (Directory.Exists(composedDir))
        {
            var cutoff = DateTime.UtcNow - MaxComposedAge;
            foreach (var file in SafeEnumerate(composedDir))
            {
                if (file.LastWriteTimeUtc < cutoff && !applied.Contains(file.FullName))
                    TryDelete(file.FullName);
            }
        }

        PruneComposed(applied);
        _lastPruneUtc = DateTime.UtcNow;
    }

    /// <summary>Passe de plafond, appelée après chaque pose de fond (throttlée).</summary>
    public void PruneComposedIfDue(IReadOnlyList<MonitorInfo> monitors)
    {
        if (DateTime.UtcNow - _lastPruneUtc < PruneThrottle)
            return;
        _lastPruneUtc = DateTime.UtcNow;

        PruneComposed(AppliedWallpapers(monitors));
    }

    /// <summary>Ramène le dossier des composés sous le plafond : plus anciens d'abord,
    /// fonds posés toujours exclus de l'éviction.</summary>
    private void PruneComposed(HashSet<string> applied)
    {
        string composedDir = _configService.ComposedDirectory;
        if (!Directory.Exists(composedDir))
            return;

        var files = SafeEnumerate(composedDir).OrderBy(f => f.LastWriteTimeUtc).ToList();
        long total = files.Sum(f => f.Length);

        foreach (var file in files)
        {
            if (total <= MaxComposedBytes)
                break;
            if (applied.Contains(file.FullName))
                continue;

            if (TryDelete(file.FullName))
                total -= file.Length;
        }
    }

    /// <summary>Supprime les sous-dossiers de webcache qui ne correspondent à aucune
    /// source de la config (sources supprimées, anciens dossiers par nom pré-V2).</summary>
    private void CleanupWebCache(AppConfig config)
    {
        string webCacheDir = Path.Combine(_configService.DataDirectory, "webcache");
        if (!Directory.Exists(webCacheDir))
            return;

        var knownIds = config.Sources.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(webCacheDir))
            {
                if (!knownIds.Contains(Path.GetFileName(directory)))
                {
                    try { Directory.Delete(directory, recursive: true); }
                    catch (Exception) { /* verrouillé : repris au prochain démarrage */ }
                }
            }
        }
        catch (Exception)
        {
            // Dossier inaccessible : rien de bloquant.
        }
    }

    /// <summary>Chemins des fonds actuellement posés (relus à chaque passe : le jeu change
    /// au fil des diaporamas).</summary>
    private HashSet<string> AppliedWallpapers(IReadOnlyList<MonitorInfo> monitors)
    {
        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in monitors)
        {
            if (_wallpaperService.GetWallpaper(monitor.DeviceId) is { Length: > 0 } path)
                applied.Add(path);
        }
        return applied;
    }

    private static IEnumerable<FileInfo> SafeEnumerate(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).EnumerateFiles().ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch (Exception)
        {
            return false; // Verrouillé (fond en cours de lecture par le shell…) : repris plus tard.
        }
    }
}
