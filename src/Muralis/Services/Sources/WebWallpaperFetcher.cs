using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Muralis.Models;

namespace Muralis.Services.Sources;

/// <summary>
/// Récupère une image depuis une source web et la matérialise en fichier local dans
/// <c>%LocalAppData%\Muralis\webcache\&lt;Id de source&gt;\</c>, pour que le pipeline habituel
/// (composition par moniteur, <c>SetWallpaper</c>) travaille toujours sur un fichier.
/// Le dossier est nommé par Id (stable au renommage, propre à chaque instance) ;
/// le cache est purgé au fil de l'eau (12 fichiers max par source).
/// </summary>
public class WebWallpaperFetcher
{
    private const int MaxCachedPerSource = 12;

    private readonly HttpClient _http;
    private readonly string _cacheRoot;

    public WebWallpaperFetcher(HttpClient http, ConfigService configService)
    {
        _http = http;
        _cacheRoot = Path.Combine(configService.DataDirectory, "webcache");
    }

    /// <summary>
    /// Résout l'image courante de la source et la matérialise en fichier local. Le nom de
    /// fichier est un hash de l'URL d'image : une URL déjà en cache n'est pas re-téléchargée
    /// et rend le même chemin — l'appelant peut ainsi détecter « image inchangée » par simple
    /// comparaison de chemins (source quotidienne, doublon d'une source aléatoire). Retourne
    /// <c>null</c> en cas d'échec (réseau, JSON inattendu…) — l'appelant garde alors le fond
    /// courant et retentera au tick suivant.
    /// </summary>
    public async Task<string?> FetchAsync(WallpaperSourceConfig source, CancellationToken ct)
    {
        try
        {
            var wallpaperSource = new HttpJsonSource(_http, source);
            Uri imageUrl = await wallpaperSource.GetImageUrlAsync(ct).ConfigureAwait(false);

            string directory = Path.Combine(_cacheRoot, Sanitize(source.Id));
            Directory.CreateDirectory(directory);

            string fileName = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(imageUrl.AbsoluteUri)))[..16]
                + GuessExtension(imageUrl);
            string path = Path.Combine(directory, fileName);

            if (!File.Exists(path))
            {
                byte[] bytes = await _http.GetByteArrayAsync(imageUrl, ct).ConfigureAwait(false);
                if (bytes.Length == 0)
                    return null;

                await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
                Prune(directory, keepNewest: MaxCachedPerSource);
            }

            return path;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static string GuessExtension(Uri imageUrl)
    {
        string extension = Path.GetExtension(imageUrl.AbsolutePath);
        return ImageFiles.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ? extension : ".jpg";
    }

    private static void Prune(string directory, int keepNewest)
    {
        try
        {
            var stale = new DirectoryInfo(directory)
                .EnumerateFiles()
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(keepNewest);
            foreach (var file in stale)
                file.Delete();
        }
        catch (Exception)
        {
            // Purge best-effort : un fichier verrouillé (fond en cours) sera repris plus tard.
        }
    }
}
