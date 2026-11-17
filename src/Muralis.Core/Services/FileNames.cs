namespace Muralis.Services;

/// <summary>
/// Assainissement de noms de fichiers <b>indépendant de la plateforme</b> :
/// <c>Path.GetInvalidFileNameChars()</c> ne contient que <c>/</c> et <c>\0</c> sous Linux.
/// Une image enregistrée doit garder un nom valide sous Windows, d'où un jeu fixe
/// (caractères interdits par Windows + séparateurs + contrôles).
/// </summary>
public static class FileNames
{
    private static readonly HashSet<char> Invalid =
    [
        '<', '>', ':', '"', '/', '\\', '|', '?', '*',
        .. Enumerable.Range(0, 32).Select(i => (char)i),
    ];

    /// <summary>Remplace chaque caractère interdit par <c>_</c>.</summary>
    public static string Sanitize(string name) =>
        string.Concat(name.Select(c => Invalid.Contains(c) ? '_' : c));
}
