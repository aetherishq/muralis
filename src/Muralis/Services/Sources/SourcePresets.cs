using Muralis.Models;

namespace Muralis.Services.Sources;

/// <summary>Un preset du catalogue : gabarit de config + indication de clé API.</summary>
/// <param name="Name">Nom affiché (unique dans le catalogue).</param>
/// <param name="RequiresKey">Vrai si la source ne fonctionne pas sans clé API.</param>
/// <param name="Note">Aide courte affichée dans l'UI (obtention de clé, particularités…).</param>
public sealed record SourcePreset(string Name, bool RequiresKey, string Note)
{
    public required string RequestUrl { get; init; }
    public required string ImageUrlJsonPath { get; init; }
    public string ApiKeyHeader { get; init; } = string.Empty;

    /// <summary>Instancie une config ajoutable (copie : le catalogue reste immuable).</summary>
    public WallpaperSourceConfig ToConfig(string apiKey = "") => new()
    {
        Name = Name,
        RequestUrl = RequestUrl,
        ImageUrlJsonPath = ImageUrlJsonPath,
        ApiKeyHeader = ApiKeyHeader,
        ApiKey = apiKey,
    };
}

/// <summary>
/// Catalogue statique embarqué des sources préconfigurées (cf. AGENTS.md — jamais dans
/// config.json : seules les sources que l'utilisateur ajoute y sont persistées).
/// </summary>
public static class SourcePresets
{
    public static readonly IReadOnlyList<SourcePreset> All =
    [
        new("Bing (image du jour)", RequiresKey: false, "Image du jour Bing, sans clé.")
        {
            RequestUrl = "https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=fr-FR",
            ImageUrlJsonPath = "images[0].url",
        },
        new("Wallhaven (aléatoire)", RequiresKey: false, "SFW par défaut ; éditer purity/categories dans l'URL. NSFW : clé du compte requise — en-tête X-API-Key, ou &apikey=CLE dans l'URL.")
        {
            RequestUrl = "https://wallhaven.cc/api/v1/search?sorting=random&categories=111&purity=100",
            ImageUrlJsonPath = "data[0].path",
        },
        new("Danbooru (aléatoire)", RequiresKey: false, "Post aléatoire ; filtres via tags dans l'URL.")
        {
            RequestUrl = "https://danbooru.donmai.us/posts/random.json",
            ImageUrlJsonPath = "file_url",
        },
        new("e621 (aléatoire)", RequiresKey: false, "rating:s par défaut, ajustable via tags dans l'URL.")
        {
            RequestUrl = "https://e621.net/posts.json?limit=1&tags=order:random+rating:s",
            ImageUrlJsonPath = "posts[0].file.url",
        },

        // Gelbooru et Rule34 exigent désormais une authentification par api_key/user_id dans
        // l'URL (401/« Missing authentication » constatés) : retirés du catalogue. Restent
        // utilisables en source personnalisée, clé incluse dans l'URL de requête.
    ];
}
