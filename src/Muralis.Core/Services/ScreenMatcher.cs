using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Muralis.Models;

namespace Muralis.Services;

/// <summary>
/// Associe une config à un écran absent de <see cref="AppConfig.Screens"/> (issue #42) :
/// <list type="number">
/// <item>même écran physique vu par un autre port de la dock : le device path change mais
/// garde l'identifiant matériel EDID (<c>\\?\DISPLAY#DELA0B5#…</c>) — on reprend la config
/// s'il n'y a qu'un candidat ;</item>
/// <item>sinon, pour un écran branché pendant la session, le fond Windows par défaut.</item>
/// </list>
/// Ces configs sont de l'exécution pure : elles ne sont jamais écrites dans <c>config.json</c>
/// tant que l'utilisateur n'a pas configuré l'écran.
/// </summary>
public static partial class ScreenMatcher
{
    /// <summary>Identifiant matériel (modèle EDID) d'un device path, ou null s'il n'a pas
    /// la forme <c>\\?\DISPLAY#&lt;id&gt;#…</c>.</summary>
    internal static string? HardwareId(string devicePath)
    {
        var parts = devicePath.Split('#');
        return parts.Length >= 3
            && parts[0].EndsWith("DISPLAY", StringComparison.OrdinalIgnoreCase)
            && parts[1].Length > 0
            ? parts[1]
            : null;
    }

    /// <summary>
    /// Config persistée d'un écran inconnu, retrouvée par identifiant matériel. Null si aucun
    /// candidat, ou en cas d'ambiguïté (deux écrans du même modèle).
    /// </summary>
    public static ScreenConfig? FindByHardware(AppConfig config, MonitorInfo monitor, IReadOnlyList<MonitorInfo> monitors)
    {
        if (HardwareId(monitor.DeviceId) is not { } hardwareId)
            return null;

        // Les configs d'écrans branchés sont prises ; seules les orphelines sont candidates.
        var connected = monitors.Select(m => m.DeviceId).ToHashSet();
        var candidates = config.Screens
            .Where(s => !connected.Contains(s.DeviceId) && SameHardware(s.DeviceId, hardwareId))
            .Take(2)
            .ToList();
        int unknownOfSameModel = monitors.Count(m =>
            config.FindScreen(m.DeviceId) is null && SameHardware(m.DeviceId, hardwareId));

        return candidates.Count == 1 && unknownOfSameModel == 1 ? candidates[0] : null;
    }

    /// <summary>Copie d'une config pour un autre device path (aucune propriété oubliée,
    /// même celles ajoutées plus tard).</summary>
    public static ScreenConfig CloneFor(ScreenConfig source, string deviceId)
    {
        var clone = JsonSerializer.Deserialize<ScreenConfig>(JsonSerializer.Serialize(source))!;
        clone.DeviceId = deviceId;
        return clone;
    }

    /// <summary>
    /// Complète <paramref name="config"/> <b>en mémoire</b> pour les écrans connectés sans
    /// config, et renvoie les device paths complétés. <paramref name="fallbackWallpaper"/>
    /// fournit l'image de repli d'un écran (null : rien, l'écran garde son fond Windows).
    /// Ne jamais sauvegarder la config ainsi complétée.
    /// </summary>
    public static IReadOnlyList<string> CompleteForRuntime(
        AppConfig config,
        IReadOnlyList<MonitorInfo> monitors,
        Func<MonitorInfo, string?> fallbackWallpaper)
    {
        if (config.Unified)
            return [];

        var added = new List<ScreenConfig>();
        foreach (var monitor in monitors)
        {
            if (config.FindScreen(monitor.DeviceId) is not null)
                continue;

            if (FindByHardware(config, monitor, monitors) is { } match)
            {
                Log.Info($"Screen '{monitor.DeviceId}' reuses the configuration of '{match.DeviceId}' (same hardware id)");
                added.Add(CloneFor(match, monitor.DeviceId));
            }
            else if (fallbackWallpaper(monitor) is { } path)
            {
                Log.Info($"Screen '{monitor.DeviceId}' is not configured: Windows default wallpaper");
                added.Add(new ScreenConfig
                {
                    DeviceId = monitor.DeviceId,
                    Mode = WallpaperMode.Fixed,
                    SourceType = ScreenConfig.LocalFileSourceType,
                    SourcePath = path,
                    DisplayMode = DesktopWallpaperPosition.Fill,
                });
            }
        }

        config.Screens.AddRange(added);
        return added.Select(s => s.DeviceId).ToList();
    }

    /// <summary>Même disposition d'écrans (device paths et rectangles, ordre indifférent).</summary>
    public static bool SameLayout(IReadOnlyList<MonitorInfo> a, IReadOnlyList<MonitorInfo> b) =>
        a.Count == b.Count && a.ToHashSet().SetEquals(b);

    private static bool SameHardware(string devicePath, string hardwareId) =>
        string.Equals(HardwareId(devicePath), hardwareId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Fond Windows par défaut le mieux adapté à un écran : variante 4K de
    /// résolution la plus proche si elle existe, sinon <c>img0.jpg</c>. Null si absent.</summary>
    public static string? WindowsDefaultWallpaper(MonitorInfo monitor)
    {
        string web = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Web");
        string baseImage = Path.Combine(web, "Wallpaper", "Windows", "img0.jpg");
        string variantsDir = Path.Combine(web, "4K", "Wallpaper", "Windows");

        IEnumerable<string> variants;
        try
        {
            variants = Directory.Exists(variantsDir) ? Directory.EnumerateFiles(variantsDir, "img0_*.jpg").ToList() : [];
        }
        catch (Exception)
        {
            variants = [];
        }

        return PickDefaultWallpaper(monitor.Width, monitor.Height, variants)
            ?? (File.Exists(baseImage) ? baseImage : null);
    }

    /// <summary>Variante <c>img0_LxH.jpg</c> la plus proche (écart log en largeur et en
    /// hauteur, donc à la fois ratio et taille), ou null.</summary>
    internal static string? PickDefaultWallpaper(int width, int height, IEnumerable<string> variants)
    {
        string? best = null;
        double bestScore = double.MaxValue;
        foreach (string path in variants)
        {
            var match = VariantSize().Match(Path.GetFileName(path));
            if (!match.Success)
                continue;
            double w = double.Parse(match.Groups[1].Value);
            double h = double.Parse(match.Groups[2].Value);
            double score = Math.Abs(Math.Log(w / width)) + Math.Abs(Math.Log(h / height));
            if (score < bestScore)
            {
                bestScore = score;
                best = path;
            }
        }
        return best;
    }

    [GeneratedRegex(@"^img0_(\d+)x(\d+)\.jpg$", RegexOptions.IgnoreCase)]
    private static partial Regex VariantSize();
}
