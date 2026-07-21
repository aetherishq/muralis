using Muralis.Models;

namespace Muralis.Services;

/// <summary>
/// Migrations ponctuelles de la config persistée, appliquées une fois au démarrage
/// (idempotentes). V1.1 : les presets du catalogue ont perdu leur parenthèse descriptive
/// française (« Bing (image du jour) » → « Bing ») pour devenir neutres en langue — le nom
/// étant l'identité des sources, les configs existantes doivent suivre.
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

        var dailyNames = config.Sources
            .Where(s => s.Kind == SourceKind.Daily)
            .Select(s => s.Name)
            .ToHashSet();

        foreach (var screen in config.Screens.Append(config.UnifiedConfig))
        {
            if (LegacySourceNames.TryGetValue(screen.SourceType, out string? renamed))
            {
                screen.SourceType = renamed;
                changed = true;
            }

            // Une source quotidienne n'est plus une source de diaporama : l'écran devient
            // « image fixe alimentée par la source » (rafraîchie à cadence interne).
            if (screen.Mode == WallpaperMode.Slideshow && dailyNames.Contains(screen.SourceType))
            {
                screen.Mode = WallpaperMode.Fixed;
                screen.SourcePath = string.Empty;
                changed = true;
            }
        }

        return changed;
    }
}
