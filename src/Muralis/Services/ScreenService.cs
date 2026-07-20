using Muralis.Services.Interop;

namespace Muralis.Services;

/// <summary>
/// Énumère les moniteurs connectés via <c>IDesktopWallpaper</c>. Chaque moniteur est
/// identifié par son <b>device path</b> (jamais par index) et accompagné de son rectangle
/// pixel, nécessaire pour composer une image à la résolution exacte de l'écran.
/// </summary>
public class ScreenService
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
        var monitors = new List<MonitorInfo>();

        uint count = wallpaper.GetMonitorDevicePathCount();
        for (uint i = 0; i < count; i++)
        {
            string deviceId = wallpaper.GetMonitorDevicePathAt(i);

            // Un device path peut correspondre à un moniteur déconnecté : il ne renvoie
            // alors pas de RECT valide. On l'ignore silencieusement.
            if (string.IsNullOrEmpty(deviceId))
                continue;

            RECT rect;
            try
            {
                rect = wallpaper.GetMonitorRECT(deviceId);
            }
            catch (Exception)
            {
                continue; // moniteur non actif
            }

            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            monitors.Add(new MonitorInfo(deviceId, rect));
        }

        return monitors;
    }
}
