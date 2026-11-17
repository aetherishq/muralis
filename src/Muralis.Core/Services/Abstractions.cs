using Muralis.Models;

namespace Muralis.Services;

/// <summary>
/// Le bureau de la plateforme : écrans connectés, pose et lecture des fonds. Implémentation
/// Windows : <c>IDesktopWallpaper</c> (COM) dans l'app. Appelé sur le contexte de l'UI.
/// </summary>
public interface IDesktopBackend
{
    /// <summary>Écrans actifs, identifiés par leur device path.</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>Pose un lot d'images d'une traite. Chaque requête est conclue
    /// individuellement (<see cref="PoseRequest.Succeed"/> / <see cref="PoseRequest.Fail"/>).</summary>
    void ApplyPoses(IReadOnlyList<PoseRequest> batch);

    /// <summary>Fond actuellement posé sur un écran, ou null.</summary>
    string? GetWallpaper(string deviceId);
}

/// <summary>
/// Compose une image à la taille exacte d'un écran selon un mode d'affichage, et renvoie le
/// chemin de l'image composée (en cache). Doit rendre la main sans bloquer l'appelant.
/// Une image source indécodable (format inconnu, fichier tronqué) lève une
/// <see cref="System.IO.InvalidDataException"/> : le cache web l'oublie alors.
/// </summary>
public interface IImageComposer
{
    Task<string> ComposeAsync(string sourcePath, int targetWidth, int targetHeight, DesktopWallpaperPosition mode);
}

/// <summary>Chiffrement des clés API persistées (DPAPI sous Windows).</summary>
public interface ISecretProtector
{
    /// <summary>Chiffre une clé en blob stockable dans la config.</summary>
    string Protect(string plain);

    /// <summary>Déchiffre un blob ; null si illisible (autre profil, valeur corrompue).</summary>
    string? Unprotect(string blob);
}
