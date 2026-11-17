using Muralis.Resources;
using Muralis.Services.Sources;

namespace Muralis.Services;

/// <summary>Texte court et localisé d'un échec de source web, pour la barre de statut
/// (le cœur ne produit que la cause, #52).</summary>
public static class FetchFailureText
{
    public static string Describe(FetchFailure failure) => failure.Kind switch
    {
        FetchFailureKind.Timeout => Strings.FetchFailure_Timeout,
        FetchFailureKind.Network => Strings.FetchFailure_Network,
        FetchFailureKind.Http when failure.HttpStatus is { } status => string.Format(Strings.FetchFailure_HttpFormat, status),
        FetchFailureKind.NoResults => Strings.FetchFailure_NoResults,
        FetchFailureKind.UnexpectedResponse => Strings.FetchFailure_UnexpectedResponse,
        FetchFailureKind.InvalidImage => Strings.FetchFailure_InvalidImage,
        _ => Strings.FetchFailure_Other,
    };
}
