using Muralis.Models;

namespace Muralis.Services;

/// <summary>
/// Migrations ponctuelles de la config persistée, appliquées une fois au démarrage
/// (idempotentes). V1.1 : presets renommés en marques nues, neutres en langue, et sources
/// quotidiennes sorties du diaporama. V2 : les sources gagnent une identité <c>Id</c>
/// (instances multiples d'un même preset) — les <see cref="ScreenConfig.SourceType"/> qui
/// référençaient un nom sont réécrits vers l'Id correspondant.
/// </summary>
public static class ConfigMigrations
{
    private static readonly Dictionary<string, string> LegacySourceNames = new()
    {
        ["Bing (image du jour)"] = "Bing",
        ["Wallhaven (aléatoire)"] = "Wallhaven",
        ["Danbooru (aléatoire)"] = "Danbooru",
        ["e621 (aléatoire)"] = "e621",
        ["Gelbooru (aléatoire)"] = "Gelbooru",
        ["Rule34 (aléatoire)"] = "Rule34",
    };

    /// <summary>Presets d'origine déduits du nom pour les sources antérieures à la V2
    /// (les autres — Gelbooru, Rule34, customs — deviennent « custom »).</summary>
    private static readonly Dictionary<string, string> KnownPresetIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bing"] = "bing",
        ["Wallhaven"] = "wallhaven",
        ["Danbooru"] = "danbooru",
        ["e621"] = "e621",
    };

    /// <summary>Applique les migrations. Retourne vrai si la config a changé (à sauver).</summary>
    public static bool Apply(AppConfig config)
    {
        bool changed = false;

        foreach (var source in config.Sources)
        {
            if (LegacySourceNames.TryGetValue(source.Name, out string? renamed))
            {
                source.Name = renamed;
                changed = true;
            }

            // Bing est une image du jour : les configs antérieures à SourceKind le
            // portaient en Random (défaut de désérialisation).
            if (source.Name == "Bing" && source.Kind != SourceKind.Daily)
            {
                source.Kind = SourceKind.Daily;
                changed = true;
            }
        }

        // V2 : identité par Id. Générer les Id manquants et retenir la correspondance
        // nom → Id pour réécrire les références des écrans.
        var nameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in config.Sources)
        {
            if (string.IsNullOrEmpty(source.Id))
            {
                source.Id = WallpaperSourceConfig.NewId();
                source.PresetId = KnownPresetIds.TryGetValue(source.Name, out string? presetId)
                    ? presetId
                    : WallpaperSourceConfig.CustomPresetId;
                changed = true;
            }
            nameToId.TryAdd(source.Name, source.Id);
        }

        foreach (var screen in config.Screens.Append(config.UnifiedConfig))
        {
            if (LegacySourceNames.TryGetValue(screen.SourceType, out string? renamed))
            {
                screen.SourceType = renamed;
                changed = true;
            }

            // Référence par nom (pré-V2) → référence par Id. Les sentinelles
            // LocalFolder/LocalFile ne sont pas des noms de source et restent telles quelles.
            if (screen.SourceType is not ("" or SlideshowService.LocalFolderSourceType or ScreenConfig.LocalFileSourceType)
                && nameToId.TryGetValue(screen.SourceType, out string? id))
            {
                screen.SourceType = id;
                changed = true;
            }

            // Une source quotidienne n'est plus une source de diaporama : l'écran devient
            // « image fixe alimentée par la source » (rafraîchie à cadence interne).
            var webSource = config.Sources.FirstOrDefault(s => s.Id == screen.SourceType);
            if (screen.Mode == WallpaperMode.Slideshow && webSource?.Kind == SourceKind.Daily)
            {
                screen.Mode = WallpaperMode.Fixed;
                screen.SourcePath = string.Empty;
                changed = true;
            }
        }

        if (config.Version < 2)
        {
            config.Version = 2;
            changed = true;
        }

        return changed;
    }
}
