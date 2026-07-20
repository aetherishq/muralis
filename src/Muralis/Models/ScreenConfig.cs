namespace Muralis.Models;

/// <summary>
/// Configuration persistée d'un écran. Un écran est toujours identifié par son
/// <see cref="DeviceId"/> (device path renvoyé par <c>IDesktopWallpaper</c>),
/// jamais par un index — l'ordre des écrans peut changer entre deux sessions Windows.
/// </summary>
public class ScreenConfig
{
    /// <summary>Device path du moniteur (clé stable d'identification).</summary>
    public string DeviceId { get; set; } = string.Empty;

    public WallpaperMode Mode { get; set; } = WallpaperMode.Fixed;

    /// <summary>"LocalFolder" ou nom d'une <c>IWallpaperSource</c> (M4). Vide en mode Fixed.</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>Chemin local de l'image (mode Fixed) ou config de source web (M3/M4).</summary>
    public string SourcePath { get; set; } = string.Empty;

    public TimeSpan SlideshowInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Diaporama : vrai = ordre aléatoire, faux = ordre alphabétique du dossier.</summary>
    public bool Shuffle { get; set; } = true;

    public DesktopWallpaperPosition DisplayMode { get; set; } = DesktopWallpaperPosition.Fill;
}
