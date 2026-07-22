namespace Muralis.Models;

/// <summary>
/// Instance de source web d'images, pilotée par configuration (aucune classe par source,
/// cf. AGENTS.md) : une requête HTTP GET dont la réponse JSON contient l'URL de l'image,
/// extraite via <see cref="ImageUrlJsonPath"/>. Persisté dans <c>config.json</c> pour les
/// sources que l'utilisateur a ajoutées. Un même preset du catalogue peut être instancié
/// plusieurs fois (ex. deux Wallhaven avec des paramètres différents) : l'identité est
/// <see cref="Id"/>, jamais le nom.
/// </summary>
public class WallpaperSourceConfig
{
    /// <summary>Identité stable de l'instance (Guid hexadécimal), référencée par
    /// <see cref="ScreenConfig.SourceType"/>. Générée à l'ajout ; vide dans les configs
    /// antérieures à la V2 (complétée par <c>ConfigMigrations</c> au démarrage).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Preset du catalogue dont l'instance dérive (« bing », « wallhaven »…),
    /// ou « custom » pour une source personnalisée.</summary>
    public string PresetId { get; set; } = CustomPresetId;

    /// <summary>Valeur de <see cref="PresetId"/> pour une source personnalisée.</summary>
    public const string CustomPresetId = "custom";

    /// <summary>Valeur de <see cref="PresetId"/> du preset Wallhaven (formulaire dédié).</summary>
    public const string WallhavenPresetId = "wallhaven";

    /// <summary>Vrai pour une source personnalisée : ses champs En-tête/Clé API sont alors
    /// éditables par instance (les presets connus partagent la clé de <see cref="AppConfig.ApiKeys"/>).</summary>
    public bool IsCustom => PresetId == CustomPresetId;

    /// <summary>Vrai pour une instance Wallhaven : sa card affiche le formulaire typé
    /// (<see cref="Wallhaven"/>) au lieu des champs URL/chemin JSON.</summary>
    public bool IsWallhaven => PresetId == WallhavenPresetId;

    /// <summary>Vrai quand la card d'édition doit montrer les champs bruts URL/chemin JSON
    /// (toute source sauf Wallhaven, qui a son formulaire).</summary>
    public bool ShowRawConfig => !IsWallhaven;

    /// <summary>Paramètres du formulaire Wallhaven (null pour les autres sources).
    /// L'URL de recherche est construite à la requête depuis ces options ;
    /// <see cref="RequestUrl"/> ne sert alors qu'à l'affichage.</summary>
    public WallhavenSourceOptions? Wallhaven { get; set; }

    /// <summary>Nom d'affichage, éditable par l'utilisateur (l'identité est <see cref="Id"/>).</summary>
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

    /// <summary>Génère l'identité d'une nouvelle instance.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");
}
