using System.Security.Cryptography;
using System.Text;

namespace Muralis.Services;

/// <summary>
/// Chiffrement des clés API persistées dans <c>config.json</c> via DPAPI
/// (<see cref="ProtectedData"/>, scope utilisateur) : pas de secret en clair sur disque,
/// aucune gestion de clé maîtresse. Limite assumée : un blob n'est déchiffrable que par
/// le profil Windows qui l'a écrit — une config copiée sur une autre machine perd ses clés.
/// </summary>
public static class ApiKeyProtector
{
    /// <summary>Chiffre une clé en blob base64 stockable dans la config.</summary>
    public static string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plain), optionalEntropy: null, DataProtectionScope.CurrentUser));

    /// <summary>Déchiffre un blob de la config. Null si illisible (autre profil/machine,
    /// valeur corrompue) : la clé est alors simplement considérée absente.</summary>
    public static string? Unprotect(string blob)
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                Convert.FromBase64String(blob), optionalEntropy: null, DataProtectionScope.CurrentUser));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
