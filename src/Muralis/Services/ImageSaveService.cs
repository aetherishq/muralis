using System.IO;
using Muralis.Services.Sources;

namespace Muralis.Services;

/// <summary>Résultat d'une tentative d'enregistrement du fond courant.</summary>
public enum SaveOutcome
{
    /// <summary>Image copiée dans le dossier d'enregistrement.</summary>
    Saved,

    /// <summary>Un fichier du même nom y est déjà (pas de doublon créé).</summary>
    AlreadySaved,

    /// <summary>Aucune image web actuellement posée sur cet écran.</summary>
    NothingToSave,
}

/// <summary>
/// Enregistre l'image web actuellement affichée sur un écran dans le dossier de
/// l'utilisateur (<c>Images\Muralis</c> par défaut, modifiable dans Paramètres).
/// L'image étant déjà dans le cache local, c'est une copie — jamais un re-téléchargement.
/// Le nom conserve l'identifiant d'origine quand l'URL le fournit
/// (<c>wallhaven-94x38z.jpg</c> — permet de retrouver la page source), sinon horodatage.
/// </summary>
public class ImageSaveService(ConfigService configService, SlideshowService slideshowService, WebWallpaperFetcher fetcher)
{
    /// <summary>Dossier par défaut : <c>%UserProfile%\Pictures\Muralis</c>.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Muralis");

    /// <summary>Dossier d'enregistrement effectif (config, sinon défaut).</summary>
    public string ResolveDirectory() =>
        configService.Load().SaveDirectory is { Length: > 0 } dir ? dir : DefaultDirectory;

    /// <summary>Enregistre le fond web courant de l'écran (<paramref name="deviceId"/> vide =
    /// cible unifiée). Retourne l'issue et le nom de fichier concerné (null si rien à faire).</summary>
    public (SaveOutcome Outcome, string? FileName) SaveCurrent(string deviceId)
    {
        string? current = slideshowService.CurrentWebImage(deviceId);
        if (current is null || !File.Exists(current))
            return (SaveOutcome.NothingToSave, null);

        string directory = ResolveDirectory();
        Directory.CreateDirectory(directory);

        string fileName = BuildFileName(current);
        string target = Path.Combine(directory, fileName);
        if (File.Exists(target))
            return (SaveOutcome.AlreadySaved, fileName);

        File.Copy(current, target);
        return (SaveOutcome.Saved, fileName);
    }

    /// <summary>Nom d'origine tiré de l'URL source si connue (session courante), sinon
    /// horodatage — le cache local, lui, est nommé par hash d'URL.</summary>
    private string BuildFileName(string cachePath)
    {
        string leaf = fetcher.SourceUrlFor(cachePath) is { } url
            ? Path.GetFileName(url.AbsolutePath)
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(leaf))
        {
            var invalid = Path.GetInvalidFileNameChars();
            leaf = string.Concat(leaf.Select(c => invalid.Contains(c) ? '_' : c));
            if (Path.GetFileNameWithoutExtension(leaf).Length > 0)
                return leaf;
        }

        return $"Muralis-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(cachePath)}";
    }
}
