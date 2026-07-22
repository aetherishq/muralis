using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Muralis.Models;

namespace Muralis.Services.Sources;

/// <summary>
/// Récupère une image depuis une source web et la matérialise en fichier local dans
/// <c>%LocalAppData%\Muralis\webcache\&lt;Id de source&gt;\</c>, pour que le pipeline habituel
/// (composition par moniteur, <c>SetWallpaper</c>) travaille toujours sur un fichier.
/// Le dossier est nommé par Id (stable au renommage, propre à chaque instance) ;
/// le cache est purgé au fil de l'eau.
///
/// Wallhaven : l'URL est construite à la requête depuis les options typées (adaptée à
/// l'écran destinataire), et une <b>page de 24 résultats</b> est récupérée d'un coup —
/// les rotations suivantes piochent dedans localement (quota API ~45 req/min). Le cache
/// de page vit en mémoire, par couple (source, écran), invalidé après ~1 h ou à épuisement.
/// </summary>
public class WebWallpaperFetcher
{
    /// <summary>Assez pour couvrir une page Wallhaven complète sans ré-évincer sa rotation.</summary>
    private const int MaxCachedPerSource = 30;

    private static readonly TimeSpan WallhavenPageLifetime = TimeSpan.FromHours(1);

    private readonly HttpClient _http;
    private readonly ConfigService _configService;
    private readonly string _cacheRoot;

    /// <summary>Pages Wallhaven en mémoire. Un couple (source, écran) n'a qu'un seul
    /// consommateur (sa cible de diaporama, protégée par FetchInProgress) : seul l'accès
    /// au dictionnaire lui-même doit être verrouillé.</summary>
    private readonly Dictionary<(string SourceId, string DeviceId), WallhavenPage> _wallhavenPages = [];

    public WebWallpaperFetcher(HttpClient http, ConfigService configService)
    {
        _http = http;
        _configService = configService;
        _cacheRoot = Path.Combine(configService.DataDirectory, "webcache");
    }

    /// <summary>
    /// Résout l'image courante de la source et la matérialise en fichier local.
    /// <paramref name="monitor"/> : écran destinataire, pour l'adaptation automatique
    /// Wallhaven (null en mode unifié — les filtres d'écran sont omis). Le nom de
    /// fichier est un hash de l'URL d'image : une URL déjà en cache n'est pas re-téléchargée
    /// et rend le même chemin — l'appelant peut ainsi détecter « image inchangée » par simple
    /// comparaison de chemins (source quotidienne, doublon d'une source aléatoire). Retourne
    /// <c>null</c> en cas d'échec (réseau, JSON inattendu…) — l'appelant garde alors le fond
    /// courant et retentera au tick suivant.
    /// </summary>
    public async Task<string?> FetchAsync(WallpaperSourceConfig source, MonitorInfo? monitor, CancellationToken ct)
    {
        try
        {
            Uri imageUrl = source is { IsWallhaven: true, Wallhaven: not null }
                ? await NextWallhavenImageAsync(source, monitor, ct).ConfigureAwait(false)
                : await new HttpJsonSource(_http, source, ResolveApiKey(source)).GetImageUrlAsync(ct).ConfigureAwait(false);

            return await DownloadAsync(source.Id, imageUrl, ct).ConfigureAwait(false);
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

    /// <summary>Prochaine image de la page Wallhaven du couple (source, écran) —
    /// re-remplie si vide, épuisée, périmée, ou si l'URL de recherche a changé (paramètres
    /// du formulaire modifiés puis ré-appliqués : les résultats des anciens filtres — NSFW
    /// notamment — ne doivent pas continuer à sortir de la page en mémoire). Une page vide
    /// (filtres trop stricts, NSFW sans clé…) lève : l'appelant signale l'échec.</summary>
    private async Task<Uri> NextWallhavenImageAsync(WallpaperSourceConfig source, MonitorInfo? monitor, CancellationToken ct)
    {
        var key = (source.Id, monitor?.DeviceId ?? string.Empty);
        string url = WallhavenUrlBuilder.Build(source.Wallhaven!, monitor);

        WallhavenPage? page;
        lock (_wallhavenPages)
            _wallhavenPages.TryGetValue(key, out page);

        if (page is null
            || page.Url != url
            || page.Remaining.Count == 0
            || DateTime.UtcNow - page.FetchedAtUtc > WallhavenPageLifetime)
        {
            page = await FetchWallhavenPageAsync(source, url, ct).ConfigureAwait(false);
            lock (_wallhavenPages)
                _wallhavenPages[key] = page;
        }

        if (page.Remaining.Count == 0)
            throw new InvalidOperationException("Recherche Wallhaven sans résultat.");
        return page.Remaining.Dequeue();
    }

    private async Task<WallhavenPage> FetchWallhavenPageAsync(WallpaperSourceConfig source, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        string? apiKey = ResolveApiKey(source);
        if (!string.IsNullOrWhiteSpace(source.ApiKeyHeader) && !string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation(source.ApiKeyHeader, apiKey);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        var page = new WallhavenPage { Url = url, FetchedAtUtc = DateTime.UtcNow };
        using var document = JsonDocument.Parse(json);
        foreach (var item in document.RootElement.GetProperty("data").EnumerateArray())
        {
            if (item.TryGetProperty("path", out var path)
                && path.GetString() is { Length: > 0 } value
                && Uri.TryCreate(value, UriKind.Absolute, out var imageUrl))
            {
                page.Remaining.Enqueue(imageUrl);
            }
        }
        return page;
    }

    /// <summary>Matérialise l'URL d'image en fichier du cache disque (nom = hash d'URL,
    /// re-téléchargement évité si déjà présent).</summary>
    private async Task<string?> DownloadAsync(string sourceId, Uri imageUrl, CancellationToken ct)
    {
        string directory = Path.Combine(_cacheRoot, Sanitize(sourceId));
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

    /// <summary>Clé effective d'une source : magasin central (par fournisseur, chiffré
    /// DPAPI) pour un preset connu, sinon la clé portée par l'instance (source custom).
    /// Résolue à chaque requête : une clé changée dans Paramètres prend effet au tick suivant.</summary>
    private string? ResolveApiKey(WallpaperSourceConfig source)
    {
        if (source.IsCustom)
            return null;

        return _configService.Load().ApiKeys.TryGetValue(source.PresetId, out string? blob)
            ? ApiKeyProtector.Unprotect(blob)
            : null;
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

    private sealed class WallhavenPage
    {
        public Queue<Uri> Remaining { get; } = new();

        /// <summary>URL de recherche qui a produit la page — empreinte d'invalidation :
        /// si l'URL reconstruite diffère, les filtres ont changé.</summary>
        public required string Url { get; init; }

        public required DateTime FetchedAtUtc { get; init; }
    }
}
