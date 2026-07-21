namespace Muralis.Services.Sources;

/// <summary>
/// Fournisseur d'URL d'image de fond d'écran (cf. AGENTS.md). Chaque appel peut retourner
/// une image différente (endpoints « random »/« du jour »).
/// </summary>
public interface IWallpaperSource
{
    string Name { get; }

    Task<Uri> GetImageUrlAsync(CancellationToken ct);
}
