namespace Muralis.Models;

/// <summary>
/// Nature d'une source web : détermine où elle est proposée dans l'éditeur d'écran.
/// <see cref="Random"/> : chaque requête renvoie une image différente → diaporama.
/// <see cref="Daily"/> : l'image change une fois par jour (ex. Bing) → card « Image »
/// en mode fixe, rafraîchie automatiquement à cadence interne.
/// </summary>
public enum SourceKind
{
    Random,
    Daily,
}
