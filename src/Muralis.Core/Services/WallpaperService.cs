using System.IO;
using Muralis.Models;

namespace Muralis.Services;

/// <summary>
/// Applique les fonds d'écran via le bureau de la plateforme (<see cref="IDesktopBackend"/>).
/// Les images sont pré-composées (<see cref="IImageComposer"/>) à la résolution du moniteur
/// pour honorer un mode d'affichage indépendant par écran ; <see cref="DesktopWallpaperPosition.Span"/> est délégué
/// nativement à Windows. Les écrans en mode <see cref="WallpaperMode.Slideshow"/> ne sont pas
/// traités ici : c'est <see cref="SlideshowService"/> qui rappelle
/// <see cref="ApplyMonitorAsync"/> / <see cref="ApplyAllMonitorsAsync"/> à chaque changement d'image.
/// <para>
/// Pipeline (#41) : composition sur un thread STA dédié (l'UI ne gèle jamais), puis poses
/// regroupées (<see cref="PoseBatcher"/>) et enchaînées sur l'UI thread, seul l'appel COM y
/// restant. Les durées de composition et de pose sont journalisées séparément.
/// </para>
/// </summary>
public class WallpaperService
{
    /// <summary>Fenêtre de regroupement des poses : assez longue pour réunir les écrans dont
    /// les diaporamas tombent ensemble, assez courte pour rester imperceptible.</summary>
    private static readonly TimeSpan PoseBatchWindow = TimeSpan.FromMilliseconds(1500);

    private readonly IImageComposer _composer;
    private readonly IDesktopBackend _backend;
    private readonly PoseBatcher _batcher;

    public WallpaperService(IImageComposer composer, IDesktopBackend backend, TimeSpan? poseBatchWindow = null)
    {
        _composer = composer;
        _backend = backend;
        _batcher = new PoseBatcher(backend.ApplyPoses, poseBatchWindow ?? PoseBatchWindow);
    }

    /// <summary>
    /// Applique les cibles en mode <see cref="WallpaperMode.Fixed"/> de la config. Ne lève
    /// pas : chaque écran est isolé — une image illisible sur l'un (fichier OneDrive non
    /// hydraté, rendu vide #35…) n'empêche plus les suivants (issue #47). Les échecs sont
    /// journalisés et comptés dans le rapport. Les écrans sont lancés ensemble : leurs poses
    /// partent dans un même lot.
    /// </summary>
    public async Task<ApplyReport> ApplyAsync(AppConfig config, IReadOnlyList<MonitorInfo> monitors)
    {
        if (config.Unified)
        {
            var unified = config.UnifiedConfig;
            if (unified.Mode == WallpaperMode.Fixed && HasImage(unified))
            {
                return await TryApplyAsync("all screens", unified.SourcePath,
                    () => ApplyAllMonitorsAsync(unified.SourcePath, unified.DisplayMode, monitors));
            }
            return ApplyReport.Empty;
        }

        var tasks = new List<Task<ApplyReport>>();
        foreach (var monitor in monitors)
        {
            var screen = config.FindScreen(monitor.DeviceId);
            if (screen is { Mode: WallpaperMode.Fixed } && HasImage(screen))
            {
                tasks.Add(TryApplyAsync(monitor.DeviceId, screen.SourcePath,
                    () => ApplyMonitorAsync(monitor, screen.SourcePath, screen.DisplayMode)));
            }
        }

        var report = ApplyReport.Empty;
        foreach (var result in await Task.WhenAll(tasks))
            report += result;
        return report;
    }

    private static async Task<ApplyReport> TryApplyAsync(string target, string imagePath, Func<Task> apply)
    {
        try
        {
            await apply();
            return new ApplyReport(Applied: 1, Failed: 0);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not apply '{imagePath}' to {target}", ex);
            return new ApplyReport(Applied: 0, Failed: 1);
        }
    }

    /// <summary>
    /// Pose une image sur un moniteur donné, pré-composée à sa taille pixel (position globale
    /// neutre : l'image étant déjà au bon format, Fill la rend 1:1).
    /// </summary>
    public virtual async Task ApplyMonitorAsync(MonitorInfo monitor, string imagePath, DesktopWallpaperPosition displayMode)
    {
        string composed = await _composer.ComposeAsync(imagePath, monitor.Width, monitor.Height, displayMode);
        await _batcher.Enqueue(new PoseRequest(monitor.DeviceId, composed, DesktopWallpaperPosition.Fill));
    }

    /// <summary>
    /// Pose la même image sur tous les écrans. En <see cref="DesktopWallpaperPosition.Span"/>,
    /// Windows étale nativement l'image sur toute la surface ; sinon elle est composée à la
    /// résolution de chaque moniteur puis assignée individuellement, dans un même lot.
    /// </summary>
    public virtual async Task ApplyAllMonitorsAsync(string imagePath, DesktopWallpaperPosition displayMode, IReadOnlyList<MonitorInfo> monitors)
    {
        if (displayMode == DesktopWallpaperPosition.Span)
        {
            await _batcher.Enqueue(new PoseRequest(null, imagePath, DesktopWallpaperPosition.Span)); // null = tous
            return;
        }

        // Toutes les compositions d'abord, puis toutes les poses ensemble.
        var composed = new List<(MonitorInfo Monitor, string Path)>();
        foreach (var monitor in monitors)
            composed.Add((monitor, await _composer.ComposeAsync(imagePath, monitor.Width, monitor.Height, displayMode)));

        await Task.WhenAll(composed.Select(c =>
            _batcher.Enqueue(new PoseRequest(c.Monitor.DeviceId, c.Path, DesktopWallpaperPosition.Fill))));
    }

    /// <summary>
    /// Chemin du fond actuellement appliqué sur un moniteur (lecture seule, pour l'aperçu du
    /// sélecteur d'écrans). Null si Windows ne peut pas le fournir.
    /// </summary>
    public string? GetWallpaper(string deviceId)
    {
        try
        {
            return _backend.GetWallpaper(deviceId) is { Length: > 0 } path ? path : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool HasImage(ScreenConfig screen) =>
        !string.IsNullOrEmpty(screen.SourcePath) && File.Exists(screen.SourcePath);
}

/// <summary>Bilan d'un <see cref="WallpaperService.ApplyAsync"/> : cibles fixes posées / en échec.</summary>
public readonly record struct ApplyReport(int Applied, int Failed)
{
    public static ApplyReport Empty => default;

    public static ApplyReport operator +(ApplyReport a, ApplyReport b) =>
        new(a.Applied + b.Applied, a.Failed + b.Failed);
}
