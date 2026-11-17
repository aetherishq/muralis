namespace Muralis.Services;

/// <summary>
/// Reconnaissance des formats acceptés (<see cref="ImageFiles.Extensions"/>) par leurs
/// premiers octets. Sert à refuser une réponse HTTP qui n'est pas une image (page HTML d'un
/// portail captif servie en 200, erreur JSON…) avant qu'elle n'entre dans le cache (#45).
/// </summary>
public static class ImageSignature
{
    /// <summary>Nombre d'octets d'en-tête nécessaires (WebP : « RIFF » + taille + « WEBP »).</summary>
    public const int Length = 12;

    public static bool IsImage(ReadOnlySpan<byte> header) =>
        StartsWith(header, [0xFF, 0xD8, 0xFF])                                 // JPEG
        || StartsWith(header, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) // PNG
        || StartsWith(header, "GIF87a"u8) || StartsWith(header, "GIF89a"u8)    // GIF
        || StartsWith(header, "BM"u8)                                          // BMP
        || (StartsWith(header, "RIFF"u8) && header.Length >= 12 && header[8..12].SequenceEqual("WEBP"u8));

    private static bool StartsWith(ReadOnlySpan<byte> header, ReadOnlySpan<byte> signature) =>
        header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature);
}
