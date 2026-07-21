namespace Muralis.Models;

/// <summary>
/// Source web d'images, pilotée par configuration (aucune classe par source, cf. AGENTS.md) :
/// une requête HTTP GET dont la réponse JSON contient l'URL de l'image, extraite via
/// <see cref="ImageUrlJsonPath"/>. Persisté dans <c>config.json</c> pour les sources que
/// l'utilisateur a ajoutées (presets du catalogue ou sources custom).
/// </summary>
public class WallpaperSourceConfig
{
    /// <summary>Nom unique affiché dans l'UI et référencé par <see cref="ScreenConfig.SourceType"/>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Nature de la source : aléatoire (diaporama) ou image du jour (mode fixe).</summary>
    public SourceKind Kind { get; set; } = SourceKind.Random;

    public string RequestUrl { get; set; } = string.Empty;

    /// <summary>Chemin vers l'URL d'image dans la réponse JSON, ex. <c>images[0].url</c> ou <c>[0].file_url</c>.</summary>
    public string ImageUrlJsonPath { get; set; } = string.Empty;

    /// <summary>Nom de l'en-tête HTTP portant la clé API (vide si aucune auth).</summary>
    public string ApiKeyHeader { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name)
        && Uri.TryCreate(RequestUrl, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(ImageUrlJsonPath);
}
