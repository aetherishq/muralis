namespace Muralis.Models;

/// <summary>
/// Mode d'affichage d'une image. Les valeurs sont alignées sur l'enum natif
/// <c>DESKTOP_WALLPAPER_POSITION</c> de l'API COM <c>IDesktopWallpaper</c>,
/// de sorte qu'un cast direct vers l'interop est valide.
/// </summary>
public enum DesktopWallpaperPosition
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5,
}
