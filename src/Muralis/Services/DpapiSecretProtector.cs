using System.Security.Cryptography;
using System.Text;
using Muralis.Models;

namespace Muralis.Services;

/// <summary>
/// Chiffrement des clés API persistées dans <c>config.json</c> via DPAPI
/// (<see cref="ProtectedData"/>, scope utilisateur) : pas de secret en clair sur disque,
/// aucune gestion de clé maîtresse. Limite assumée : un blob n'est déchiffrable que par
/// le profil Windows qui l'a écrit — une config copiée sur une autre machine perd ses clés.
/// <para>
/// Entropie propre à Muralis (review s4) : un autre programme du même profil qui appelle
/// DPAPI sans entropie ne déchiffre plus directement nos blobs. Les blobs écrits avant
/// (sans entropie) restent lisibles et sont ré-chiffrés au démarrage
/// (<see cref="UpgradeLegacyBlobs"/>).
/// </para>
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Muralis.ApiKeys.v1");

    public string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));

    public string? Unprotect(string blob) => Decrypt(blob, Entropy) ?? Decrypt(blob, entropy: null);

    /// <summary>Ré-chiffre avec l'entropie les clés du magasin écrites sans. Vrai si la
    /// config a changé (à sauver).</summary>
    public bool UpgradeLegacyBlobs(AppConfig config)
    {
        bool changed = false;
        foreach (var (presetId, blob) in config.ApiKeys.ToList())
        {
            if (Decrypt(blob, Entropy) is not null)
                continue;
            if (Decrypt(blob, entropy: null) is { } plain)
            {
                config.ApiKeys[presetId] = Protect(plain);
                changed = true;
            }
        }
        return changed;
    }

    private static string? Decrypt(string blob, byte[]? entropy)
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                Convert.FromBase64String(blob), entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
