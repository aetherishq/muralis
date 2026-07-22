using Muralis.Models;
using Muralis.Resources;

namespace Muralis.Services.Sources;

/// <summary>Un preset du catalogue : gabarit de config des instances ajoutées.</summary>
/// <param name="PresetId">Identifiant stable du preset (« bing », « wallhaven »…), recopié
/// dans les instances pour retrouver leur origine (formulaire dédié, clé API partagée).</param>
/// <param name="Name">Nom affiché (unique dans le catalogue) : marque nue, neutre en langue
/// (le descriptif vit dans la note localisée). Sert de nom d'affichage par défaut aux
/// instances, suffixé en cas de collision.</param>
/// <param name="NoteKey">Clé resx de l'aide courte affichée dans l'UI.</param>
public sealed record SourcePreset(string PresetId, string Name, string NoteKey)
{
    /// <summary>Aide localisée, résolue à l'affichage (suit les changements de langue).</summary>
    public string Note => Strings.ResourceManager.GetString(NoteKey) ?? string.Empty;

    public required string RequestUrl { get; init; }
    public required string ImageUrlJsonPath { get; init; }

    /// <summary>En-tête HTTP portant la clé API du fournisseur (vide si aucune auth).
    /// La clé elle-même vit dans <see cref="AppConfig.ApiKeys"/>, jamais dans le preset.</summary>
    public string ApiKeyHeader { get; init; } = string.Empty;

    /// <summary>Nature de la source (cf. <see cref="SourceKind"/>).</summary>
    public SourceKind Kind { get; init; } = SourceKind.Random;

    /// <summary>Instancie une config ajoutable (copie : le catalogue reste immuable).</summary>
    public WallpaperSourceConfig ToConfig() => new()
    {
        Id = WallpaperSourceConfig.NewId(),
        PresetId = PresetId,
        Name = Name,
        Kind = Kind,
        RequestUrl = RequestUrl,
        ImageUrlJsonPath = ImageUrlJsonPath,
        ApiKeyHeader = ApiKeyHeader,
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
        new("bing", "Bing", nameof(Strings.Preset_BingNote))
        {
            RequestUrl = "https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=fr-FR",
            ImageUrlJsonPath = "images[0].url",
            Kind = SourceKind.Daily,
        },
        new("wallhaven", "Wallhaven", nameof(Strings.Preset_WallhavenNote))
        {
            RequestUrl = "https://wallhaven.cc/api/v1/search?sorting=random&categories=111&purity=100",
            ImageUrlJsonPath = "data[0].path",
            ApiKeyHeader = "X-API-Key",
        },
        // Catalogue volontairement réduit à Bing + Wallhaven (V1.2, issue #14). Gelbooru et
        // Rule34 exigeaient de toute façon une auth api_key/user_id (401 constatés) ;
        // Danbooru, e621 et le reste passent par la source personnalisée (URL + chemin
        // JSON + en-tête libre), sous la responsabilité de l'utilisateur. Les instances
        // déjà ajoutées continuent de fonctionner : leur config est persistée.
    ];
}
