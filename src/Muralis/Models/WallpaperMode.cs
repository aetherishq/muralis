namespace Muralis.Models;

/// <summary>
/// Mode d'alimentation d'un écran en fond d'écran.
/// M2 n'utilise que <see cref="Fixed"/> ; <see cref="Slideshow"/> arrive en M3.
/// </summary>
public enum WallpaperMode
{
    Fixed,
    Slideshow,
}
