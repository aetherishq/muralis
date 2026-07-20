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

    /// <summary>Applique les cibles en mode <see cref="WallpaperMode.Fixed"/> de la config.</summary>
    public void Apply(AppConfig config, IReadOnlyList<MonitorInfo> monitors)
    {
        if (config.Unified)
        {
            var unified = config.UnifiedConfig;
            if (unified.Mode == WallpaperMode.Fixed && HasImage(unified))
                ApplyAllMonitors(unified.SourcePath, unified.DisplayMode, monitors);
            return;
        }

        foreach (var monitor in monitors)
        {
            var screen = config.FindScreen(monitor.DeviceId);
            if (screen is { Mode: WallpaperMode.Fixed } && HasImage(screen))
                ApplyMonitor(monitor, screen.SourcePath, screen.DisplayMode);
        }
    }

    /// <summary>
    /// Pose une image sur un moniteur donné, pré-composée à sa taille pixel.
    /// Position globale neutre : l'image étant déjà au bon format, Fill la rend 1:1.
    /// </summary>
    public void ApplyMonitor(MonitorInfo monitor, string imagePath, DesktopWallpaperPosition displayMode)
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
    public void ApplyAllMonitors(string imagePath, DesktopWallpaperPosition displayMode, IReadOnlyList<MonitorInfo> monitors)
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

    private static bool HasImage(ScreenConfig screen) =>
        !string.IsNullOrEmpty(screen.SourcePath) && File.Exists(screen.SourcePath);
}
