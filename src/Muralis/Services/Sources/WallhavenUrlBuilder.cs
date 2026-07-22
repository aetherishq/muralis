using Muralis.Models;

namespace Muralis.Services.Sources;

/// <summary>
/// Construit l'URL de recherche Wallhaven depuis les options typées d'une instance,
/// au moment de la requête — c'est ce qui permet l'adaptation automatique à l'écran
/// destinataire (résolution minimale + orientation) sans dupliquer la source.
/// </summary>
public static class WallhavenUrlBuilder
{
    public const string SearchBaseUrl = "https://wallhaven.cc/api/v1/search";

    /// <summary>
    /// URL complète de recherche. <paramref name="monitor"/> : écran destinataire pour
    /// l'adaptation automatique (null en mode unifié — les filtres d'écran sont alors omis).
    /// </summary>
    public static string Build(WallhavenSourceOptions options, MonitorInfo? monitor)
    {
        // sorting=random forcé : toute autre valeur casse l'intérêt du diaporama.
        var query = new List<string> { "sorting=random" };

        // Aucune case cochée = filtre vide qui ne renverrait rien : retomber sur le défaut.
        string categories = Bits(options.CategoryGeneral, options.CategoryAnime, options.CategoryPeople);
        query.Add($"categories={(categories == "000" ? "101" : categories)}");

        string purity = Bits(options.PuritySfw, options.PuritySketchy, options.PurityNsfw);
        query.Add($"purity={(purity == "000" ? "100" : purity)}");

        string q = BuildTagsQuery(options.Tags);
        if (q.Length > 0)
            query.Add($"q={q}");

        if (options.AutoFitScreen)
        {
            if (monitor is not null)
            {
                query.Add($"atleast={monitor.Width}x{monitor.Height}");
                query.Add($"ratios={(monitor.Width >= monitor.Height ? "landscape" : "portrait")}");
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(options.AtLeast))
                query.Add($"atleast={Uri.EscapeDataString(options.AtLeast.Trim())}");
            if (!string.IsNullOrWhiteSpace(options.Ratios))
                query.Add($"ratios={Uri.EscapeDataString(options.Ratios.Trim())}");
        }

        return $"{SearchBaseUrl}?{string.Join('&', query)}";
    }

    private static string Bits(bool first, bool second, bool third) =>
        $"{(first ? 1 : 0)}{(second ? 1 : 0)}{(third ? 1 : 0)}";

    /// <summary>
    /// Liste saisie « voiture, soleil, -anime » → <c>%2Bvoiture%20%2Bsoleil%20-anime</c>.
    /// Mode strict : chaque tag est requis (<c>+tag</c>) — le <c>+</c> littéral doit être
    /// encodé <c>%2B</c>, sinon l'API le lit comme un espace et bascule en recherche floue.
    /// Le préfixe <c>-</c> (exclusion) est conservé tel quel.
    /// </summary>
    internal static string BuildTagsQuery(string tags)
    {
        var terms = tags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.StartsWith('-') ? t : "+" + t);
        return Uri.EscapeDataString(string.Join(' ', terms));
    }
}
