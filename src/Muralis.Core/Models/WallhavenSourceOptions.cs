using CommunityToolkit.Mvvm.ComponentModel;

namespace Muralis.Models;

/// <summary>
/// Paramètres typés d'une instance Wallhaven : l'URL de recherche est construite à chaque
/// requête à partir d'eux (cf. <c>WallhavenUrlBuilder</c>), plus jamais éditée à la main.
/// <c>sorting=random</c> est forcé (seule valeur utile en diaporama) ; <c>order</c>,
/// <c>topRange</c>, <c>resolutions</c> et <c>colors</c> sont volontairement hors modèle.
/// Observable : les cases de la card source réagissent en direct (avertissement NSFW).
/// </summary>
public partial class WallhavenSourceOptions : ObservableObject
{
    /// <summary>Tags séparés par des virgules, préfixe <c>-</c> pour exclure. Mode strict :
    /// chaque tag saisi est obligatoire (opérateur <c>+tag</c> de la recherche avancée).</summary>
    [ObservableProperty]
    private string tags = string.Empty;

    [ObservableProperty]
    private bool categoryGeneral = true;

    [ObservableProperty]
    private bool categoryAnime;

    [ObservableProperty]
    private bool categoryPeople = true;

    [ObservableProperty]
    private bool puritySfw = true;

    [ObservableProperty]
    private bool puritySketchy;

    [ObservableProperty]
    private bool purityNsfw;

    /// <summary>Vrai : <c>atleast</c> (résolution de l'écran) et <c>ratios</c>
    /// (landscape/portrait) sont calculés par écran au moment de la requête —
    /// rien n'est stocké. Faux : les deux champs manuels ci-dessous font foi.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManualFit))]
    private bool autoFitScreen = true;

    /// <summary>Visibilité des champs manuels dans la card (inverse de l'adaptation auto).</summary>
    public bool ManualFit => !AutoFitScreen;

    /// <summary>Copie indépendante (bouton Dupliquer d'une source).</summary>
    public WallhavenSourceOptions Clone() => new()
    {
        Tags = Tags,
        CategoryGeneral = CategoryGeneral,
        CategoryAnime = CategoryAnime,
        CategoryPeople = CategoryPeople,
        PuritySfw = PuritySfw,
        PuritySketchy = PuritySketchy,
        PurityNsfw = PurityNsfw,
        AutoFitScreen = AutoFitScreen,
        AtLeast = AtLeast,
        Ratios = Ratios,
    };

    /// <summary>Résolution minimale manuelle, ex. <c>1920x1080</c> (vide = non envoyée).</summary>
    [ObservableProperty]
    private string atLeast = string.Empty;

    /// <summary>Ratios manuels, ex. <c>16x9,16x10</c> (vide = non envoyés).</summary>
    [ObservableProperty]
    private string ratios = string.Empty;
}
