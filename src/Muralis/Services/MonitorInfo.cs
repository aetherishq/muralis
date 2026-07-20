using System.Windows;
using Muralis.Services.Interop;

namespace Muralis.Services;

/// <summary>
/// Un moniteur physique détecté, identifié par son device path stable.
/// </summary>
/// <param name="DeviceId">Device path (clé d'identification, cf. AGENTS.md).</param>
/// <param name="Bounds">Rectangle du moniteur en pixels physiques (coordonnées virtuelles du bureau).</param>
public record MonitorInfo(string DeviceId, RECT Bounds)
{
    public int Width => Bounds.Width;
    public int Height => Bounds.Height;

    /// <summary>Résolution lisible pour l'UI, ex. « 2560 × 1440 ».</summary>
    public string ResolutionLabel => $"{Width} × {Height}";

    /// <summary>Position du coin haut-gauche dans l'espace bureau virtuel.</summary>
    public Point Origin => new(Bounds.Left, Bounds.Top);
}
