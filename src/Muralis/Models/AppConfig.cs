namespace Muralis.Models;

/// <summary>
/// Racine de la configuration persistée dans
/// <c>%LocalAppData%\Muralis\config.json</c>.
/// </summary>
public class AppConfig
{
    /// <summary>Version du schéma de config, pour migrations futures.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Thème de l'UI (le démarrage Windows, lui, vit uniquement dans le registre).</summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>Langue de l'UI : <c>null</c> = suivre Windows, sinon code culture (« fr », « en »).</summary>
    public string? Language { get; set; }

    /// <summary>
    /// Vrai : un même fond (voir <see cref="UnifiedConfig"/>) s'applique à tous les écrans.
    /// Faux : chaque écran a sa propre config (voir <see cref="Screens"/>).
    /// </summary>
    public bool Unified { get; set; }

    /// <summary>Config commune utilisée quand <see cref="Unified"/> est vrai.</summary>
    public ScreenConfig UnifiedConfig { get; set; } = new();

    public List<ScreenConfig> Screens { get; set; } = [];

    /// <summary>Sources web ajoutées par l'utilisateur (presets du catalogue ou custom).</summary>
    public List<WallpaperSourceConfig> Sources { get; set; } = [];

    /// <summary>Dossier où « Enregistrer le fond actuel » copie les images
    /// (null/vide = <c>Images\Muralis</c> par défaut, cf. <c>ImageSaveService</c>).</summary>
    public string? SaveDirectory { get; set; }

    /// <summary>Clés API par fournisseur (<see cref="WallpaperSourceConfig.PresetId"/> →
    /// blob DPAPI base64, cf. <c>ApiKeyProtector</c>) : une clé partagée par toutes les
    /// instances d'un même preset. Les sources personnalisées gardent leur clé par instance.</summary>
    public Dictionary<string, string> ApiKeys { get; set; } = [];

    /// <summary>Renvoie la config d'un écran par device path, ou <c>null</c> si absente.</summary>
    public ScreenConfig? FindScreen(string deviceId) =>
        Screens.FirstOrDefault(s => s.DeviceId == deviceId);
}
