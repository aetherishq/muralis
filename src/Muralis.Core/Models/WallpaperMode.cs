namespace Muralis.Models;

/// <summary>
/// Mode d'alimentation d'un écran en fond d'écran : image fixe, ou diaporama
/// (dossier local en M3, sources web en M4 via <c>SourceType</c>).
/// </summary>
public enum WallpaperMode
{
    Fixed,
    Slideshow,
}
