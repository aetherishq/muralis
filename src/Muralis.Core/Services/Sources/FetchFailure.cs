namespace Muralis.Services.Sources;

/// <summary>Cause d'échec d'une source web. Le cœur ne produit pas de texte : l'app le
/// traduit pour la barre de statut (#52).</summary>
public enum FetchFailureKind
{
    Timeout,
    Network,
    Http,
    NoResults,
    UnexpectedResponse,
    InvalidImage,
    Other,
}

/// <summary>Échec d'une source web : cause, et code HTTP le cas échéant.</summary>
public sealed record FetchFailure(FetchFailureKind Kind, int? HttpStatus = null)
{
    public static readonly FetchFailure InvalidImage = new(FetchFailureKind.InvalidImage);
    public static readonly FetchFailure Other = new(FetchFailureKind.Other);
}
