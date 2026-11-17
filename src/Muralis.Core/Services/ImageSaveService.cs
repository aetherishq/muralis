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

    private string BuildFileName(string cachePath) =>
        BuildFileName(fetcher.SourceUrlFor(cachePath), cachePath, DateTime.Now);

    /// <summary>
    /// Nom d'enregistrement : nom d'origine tiré de l'URL source quand il désigne une image
    /// (<c>wallhaven-94x38z.jpg</c>), sinon paramètre <c>id=</c> de la query s'il en désigne
    /// une (Bing : <c>/th?id=OHR.Nom_FR-FR123_1920x1080.jpg&amp;…</c> — le chemin seul donnait
    /// « th », sans extension, et bloquait tout enregistrement suivant, issue #48), sinon
    /// horodatage. Jamais d'extension hors <see cref="ImageFiles.Extensions"/>
    /// (<c>get.php</c> → horodatage).
    /// </summary>
    internal static string BuildFileName(Uri? sourceUrl, string cachePath, DateTime now)
    {
        if (sourceUrl is not null)
        {
            if (AsImageFileName(Path.GetFileName(sourceUrl.AbsolutePath)) is { } fromPath)
                return fromPath;
            if (AsImageFileName(QueryValue(sourceUrl, "id")) is { } fromId)
                return fromId;
        }

        return $"Muralis-{now:yyyyMMdd-HHmmss}{Path.GetExtension(cachePath)}";
    }

    /// <summary>Le nom assaini s'il a une base non vide et une extension d'image, sinon null.</summary>
    private static string? AsImageFileName(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        string name = FileNames.Sanitize(candidate);
        bool isImage = ImageFiles.Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
        return isImage && Path.GetFileNameWithoutExtension(name).Length > 0 ? name : null;
    }

    private static string? QueryValue(Uri url, string key)
    {
        foreach (string pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0 && string.Equals(pair[..eq], key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return null;
    }
}
