using Muralis.Models;
using Muralis.Resources;

namespace Muralis.Services.Sources;

/// <summary>Un preset du catalogue : gabarit de config + indication de clé API.</summary>
/// <param name="Name">Nom affiché (unique dans le catalogue). Identité persistée des sources
/// ajoutées : jamais traduit.</param>
/// <param name="RequiresKey">Vrai si la source ne fonctionne pas sans clé API.</param>
/// <param name="NoteKey">Clé resx de l'aide courte affichée dans l'UI.</param>
public sealed record SourcePreset(string Name, bool RequiresKey, string NoteKey)
{
    /// <summary>Aide localisée, résolue à l'affichage (suit les changements de langue).</summary>
    public string Note => Strings.ResourceManager.GetString(NoteKey) ?? string.Empty;

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
        new("Bing (image du jour)", RequiresKey: false, nameof(Strings.Preset_BingNote))
        {
            RequestUrl = "https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=fr-FR",
            ImageUrlJsonPath = "images[0].url",
        },
        new("Wallhaven (aléatoire)", RequiresKey: false, nameof(Strings.Preset_WallhavenNote))
        {
            RequestUrl = "https://wallhaven.cc/api/v1/search?sorting=random&categories=111&purity=100",
            ImageUrlJsonPath = "data[0].path",
        },
        new("Danbooru (aléatoire)", RequiresKey: false, nameof(Strings.Preset_DanbooruNote))
        {
            RequestUrl = "https://danbooru.donmai.us/posts/random.json",
            ImageUrlJsonPath = "file_url",
        },
        new("e621 (aléatoire)", RequiresKey: false, nameof(Strings.Preset_E621Note))
        {
            RequestUrl = "https://e621.net/posts.json?limit=1&tags=order:random+rating:s",
            ImageUrlJsonPath = "posts[0].file.url",
        },

        // Gelbooru et Rule34 exigent désormais une authentification par api_key/user_id dans
        // l'URL (401/« Missing authentication » constatés) : retirés du catalogue. Restent
        // utilisables en source personnalisée, clé incluse dans l'URL de requête.
    ];
}
