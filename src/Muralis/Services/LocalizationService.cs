using System.Globalization;
using Microsoft.Win32;

namespace Muralis.Services;

/// <summary>
/// Langue de l'UI. Anglais = langue neutre (Strings.resx), français en satellite ;
/// <c>null</c> = suivre la langue de Windows. À appeler avant toute construction d'UI —
/// un changement à chaud exige de reconstruire fenêtre, tray et ViewModels (les libellés
/// y sont capturés à la création).
/// </summary>
public static class LocalizationService
{
    /// <summary>Codes des cultures traduites (proposés dans Paramètres).</summary>
    public static readonly IReadOnlyList<string> Supported = ["fr", "en"];

    private static readonly CultureInfo SystemUiCulture = CultureInfo.CurrentUICulture;

    /// <summary>Applique la culture UI demandée (<c>null</c> : celle de Windows).</summary>
    public static void Apply(string? code)
    {
        var culture = code is null ? SystemUiCulture : CultureInfo.GetCultureInfo(code);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    /// <summary>
    /// Langue choisie dans l'assistant d'installation (« french »/« english », écrite par
    /// Inno Setup dans HKCU\Software\Muralis). Consommée une seule fois, au premier
    /// lancement, quand la config n'a pas encore de langue.
    /// </summary>
    public static string? ReadInstallerSeed()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Muralis");
            return (key?.GetValue("PreferredLanguage") as string) switch
            {
                "french" => "fr",
                "english" => "en",
                _ => null,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
