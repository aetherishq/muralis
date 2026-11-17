namespace Muralis.Services;

/// <summary>Rectangle en pixels physiques, coordonnées du bureau virtuel (neutre en plateforme).</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>
/// Un moniteur physique détecté, identifié par son device path stable.
/// </summary>
/// <param name="DeviceId">Device path (clé d'identification, cf. AGENTS.md).</param>
/// <param name="Bounds">Rectangle du moniteur en pixels physiques (coordonnées virtuelles du bureau).</param>
public record MonitorInfo(string DeviceId, PixelRect Bounds)
{
    public int Width => Bounds.Width;
    public int Height => Bounds.Height;

    /// <summary>Résolution lisible pour l'UI, ex. « 2560 × 1440 ».</summary>
    public string ResolutionLabel => $"{Width} × {Height}";
}
