using System.IO;
using Muralis.Models;
using Muralis.Services.Interop;

namespace Muralis.Services;

/// <summary>
/// Applique la configuration aux moniteurs via <c>IDesktopWallpaper</c>.
/// Chaque écran en mode <see cref="WallpaperMode.Fixed"/> reçoit son image, pré-composée à sa
/// résolution pour honorer un mode d'affichage indépendant (voir <see cref="WallpaperComposer"/>).
/// Le mode <see cref="DesktopWallpaperPosition.Span"/> est délégué nativement à Windows.
/// </summary>
public class WallpaperService
{
    private readonly WallpaperComposer _composer;

    public WallpaperService(WallpaperComposer composer)
    {
        _composer = composer;
    }

    public void Apply(AppConfig config, IReadOnlyList<MonitorInfo> monitors)
    {
        var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();

        if (config.Unified)
        {
            ApplyUnified(wallpaper, config.UnifiedConfig, monitors);
            return;
        }

        // Config par écran : une image par moniteur, pré-composée à sa taille pixel.
        // Position globale neutre : l'image étant déjà au bon format, Fill la rend 1:1.
        wallpaper.SetPosition(DesktopWallpaperPosition.Fill);

        foreach (var monitor in monitors)
        {
            var screen = config.FindScreen(monitor.DeviceId);
            if (screen is null || screen.Mode != WallpaperMode.Fixed || !HasImage(screen))
                continue;

            string composed = _composer.Compose(
                screen.SourcePath, monitor.Width, monitor.Height, screen.DisplayMode);
            wallpaper.SetWallpaper(monitor.DeviceId, composed);
        }
    }

    /// <summary>
    /// Applique un même fond à tous les écrans. En <see cref="DesktopWallpaperPosition.Span"/>,
    /// Windows étale nativement l'image sur toute la surface ; sinon l'image est composée à la
    /// résolution de chaque moniteur puis assignée individuellement.
    /// </summary>
    private void ApplyUnified(IDesktopWallpaper wallpaper, ScreenConfig unified, IReadOnlyList<MonitorInfo> monitors)
    {
        if (!HasImage(unified))
            return;

        if (unified.DisplayMode == DesktopWallpaperPosition.Span)
        {
            wallpaper.SetPosition(DesktopWallpaperPosition.Span);
            wallpaper.SetWallpaper(null, unified.SourcePath); // null = tous les moniteurs
            return;
        }

        wallpaper.SetPosition(DesktopWallpaperPosition.Fill);
        foreach (var monitor in monitors)
        {
            string composed = _composer.Compose(
                unified.SourcePath, monitor.Width, monitor.Height, unified.DisplayMode);
            wallpaper.SetWallpaper(monitor.DeviceId, composed);
        }
    }

    private static bool HasImage(ScreenConfig screen) =>
        !string.IsNullOrEmpty(screen.SourcePath) && File.Exists(screen.SourcePath);
}
