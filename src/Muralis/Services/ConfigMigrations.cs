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
        }

        foreach (var screen in config.Screens.Append(config.UnifiedConfig))
        {
            if (LegacySourceNames.TryGetValue(screen.SourceType, out string? renamed))
            {
                screen.SourceType = renamed;
                changed = true;
            }
        }

        return changed;
    }
}
