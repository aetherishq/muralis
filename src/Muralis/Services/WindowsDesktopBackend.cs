using System.Diagnostics;
using Muralis.Services.Interop;

namespace Muralis.Services;

/// <summary>
/// Bureau Windows via <c>IDesktopWallpaper</c> (COM). Les moniteurs sont identifiés par leur
/// <b>device path</b> (jamais par index) avec leur rectangle pixel, nécessaire pour composer
/// une image à la résolution exacte de l'écran. Une session COM par opération
/// (<see cref="DesktopWallpaperSession"/>). Appelé sur l'UI thread (STA).
/// </summary>
public sealed class WindowsDesktopBackend : IDesktopBackend
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        using var session = new DesktopWallpaperSession();
        var wallpaper = session.Api;
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

            monitors.Add(new MonitorInfo(deviceId, new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom)));
        }

        return monitors;
    }

    /// <summary>Exécute un lot de poses d'une traite, dans une seule session COM. Chaque pose
    /// est isolée ; la position globale n'est changée que si elle diffère (review m5).
    /// Durées journalisées séparément de la composition (#41).</summary>
    public void ApplyPoses(IReadOnlyList<PoseRequest> batch)
    {
        var positionTime = new Stopwatch();
        var setTime = new Stopwatch();
        int failed = 0;

        using (var session = new DesktopWallpaperSession())
        {
            foreach (var request in batch)
            {
                try
                {
                    positionTime.Start();
                    session.EnsurePosition(request.Position);
                    positionTime.Stop();

                    setTime.Start();
                    session.Api.SetWallpaper(request.DeviceId, request.Path);
                    setTime.Stop();

                    request.Succeed();
                }
                catch (Exception ex)
                {
                    positionTime.Stop();
                    setTime.Stop();
                    failed++;
                    request.Fail(ex);
                }
            }
        }

        Log.Info($"Wallpaper pose: {batch.Count} image(s){(failed > 0 ? $", {failed} failed" : "")}, " +
            $"SetPosition {positionTime.ElapsedMilliseconds} ms, SetWallpaper {setTime.ElapsedMilliseconds} ms");
    }

    public string? GetWallpaper(string deviceId)
    {
        using var session = new DesktopWallpaperSession();
        return session.Api.GetWallpaper(deviceId);
    }
}
