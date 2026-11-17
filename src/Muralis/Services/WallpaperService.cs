using System.IO;
using Muralis.Models;
using Muralis.Services.Interop;

namespace Muralis.Services;

/// <summary>
/// Applique les fonds d'écran via <c>IDesktopWallpaper</c>. Les images sont pré-composées à la
/// résolution du moniteur pour honorer un mode d'affichage indépendant par écran (voir
/// <see cref="WallpaperComposer"/>) ; <see cref="DesktopWallpaperPosition.Span"/> est délégué
/// nativement à Windows. Les écrans en mode <see cref="WallpaperMode.Slideshow"/> ne sont pas
/// traités ici : c'est <see cref="SlideshowService"/> qui rappelle
/// <see cref="ApplyMonitor"/> / <see cref="ApplyAllMonitors"/> à chaque changement d'image.
/// </summary>
public class WallpaperService
{
    private readonly WallpaperComposer _composer;

    public WallpaperService(WallpaperComposer composer)
    {
        _composer = composer;
    }

    /// <summary>
    /// Applique les cibles en mode <see cref="WallpaperMode.Fixed"/> de la config. Ne lève
    /// pas : chaque écran est isolé — une image illisible sur l'un (fichier OneDrive non
    /// hydraté, rendu vide #35…) n'empêche plus les suivants (issue #47). Les échecs sont
    /// journalisés et comptés dans le rapport.
    /// </summary>
    public ApplyReport Apply(AppConfig config, IReadOnlyList<MonitorInfo> monitors)
    {
        if (config.Unified)
        {
            var unified = config.UnifiedConfig;
            if (unified.Mode == WallpaperMode.Fixed && HasImage(unified))
            {
                return TryApply("all screens", unified.SourcePath,
                    () => ApplyAllMonitors(unified.SourcePath, unified.DisplayMode, monitors));
            }
            return ApplyReport.Empty;
        }

        var report = ApplyReport.Empty;
        foreach (var monitor in monitors)
        {
            var screen = config.FindScreen(monitor.DeviceId);
            if (screen is { Mode: WallpaperMode.Fixed } && HasImage(screen))
            {
                report += TryApply(monitor.DeviceId, screen.SourcePath,
                    () => ApplyMonitor(monitor, screen.SourcePath, screen.DisplayMode));
            }
        }
        return report;
    }

    private static ApplyReport TryApply(string target, string imagePath, Action apply)
    {
        try
        {
            apply();
            return new ApplyReport(Applied: 1, Failed: 0);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not apply '{imagePath}' to {target}", ex);
            return new ApplyReport(Applied: 0, Failed: 1);
        }
    }

    /// <summary>
    /// Pose une image sur un moniteur donné, pré-composée à sa taille pixel.
    /// Position globale neutre : l'image étant déjà au bon format, Fill la rend 1:1.
    /// Virtuelle : substituée dans les tests (aucun appel COM réel).
    /// </summary>
    public virtual void ApplyMonitor(MonitorInfo monitor, string imagePath, DesktopWallpaperPosition displayMode)
    {
        var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
        wallpaper.SetPosition(DesktopWallpaperPosition.Fill);

        string composed = _composer.Compose(imagePath, monitor.Width, monitor.Height, displayMode);
        wallpaper.SetWallpaper(monitor.DeviceId, composed);
    }

    /// <summary>
    /// Pose la même image sur tous les écrans. En <see cref="DesktopWallpaperPosition.Span"/>,
    /// Windows étale nativement l'image sur toute la surface ; sinon elle est composée à la
    /// résolution de chaque moniteur puis assignée individuellement.
    /// </summary>
    public virtual void ApplyAllMonitors(string imagePath, DesktopWallpaperPosition displayMode, IReadOnlyList<MonitorInfo> monitors)
    {
        var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();

        if (displayMode == DesktopWallpaperPosition.Span)
        {
            wallpaper.SetPosition(DesktopWallpaperPosition.Span);
            wallpaper.SetWallpaper(null, imagePath); // null = tous les moniteurs
            return;
        }

        wallpaper.SetPosition(DesktopWallpaperPosition.Fill);
        foreach (var monitor in monitors)
        {
            string composed = _composer.Compose(imagePath, monitor.Width, monitor.Height, displayMode);
            wallpaper.SetWallpaper(monitor.DeviceId, composed);
        }
    }

    /// <summary>
    /// Chemin du fond actuellement appliqué sur un moniteur (lecture seule, pour l'aperçu du
    /// sélecteur d'écrans). Null si Windows ne peut pas le fournir.
    /// </summary>
    public string? GetWallpaper(string deviceId)
    {
        try
        {
            var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            string path = wallpaper.GetWallpaper(deviceId);
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool HasImage(ScreenConfig screen) =>
        !string.IsNullOrEmpty(screen.SourcePath) && File.Exists(screen.SourcePath);
}

/// <summary>Bilan d'un <see cref="WallpaperService.Apply"/> : cibles fixes posées / en échec.</summary>
public readonly record struct ApplyReport(int Applied, int Failed)
{
    public static ApplyReport Empty => default;

    public static ApplyReport operator +(ApplyReport a, ApplyReport b) =>
        new(a.Applied + b.Applied, a.Failed + b.Failed);
}
