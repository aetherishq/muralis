using System.IO;

namespace Muralis.Services;

/// <summary>
/// Formats d'image acceptés par l'app (référence unique pour le slideshow, les previews
/// et les filtres des boîtes de dialogue).
/// </summary>
public static class ImageFiles
{
    public static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    /// <summary>Énumère les images d'un dossier (non récursif). Peut lever si le dossier est inaccessible.</summary>
    public static IEnumerable<string> Enumerate(string folder) =>
        Directory.EnumerateFiles(folder)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
}
