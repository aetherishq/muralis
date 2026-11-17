using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Muralis.Models;

namespace Muralis.Services.Sources;

/// <summary>Issue d'une récupération : chemin local de l'image, ou motif court (localisé)
/// de l'échec.</summary>
public sealed record FetchResult(string? Path, FetchFailure? Failure)
{
    public static FetchResult Success(string path) => new(path, null);

    public static FetchResult Failed(FetchFailure failure) => new(null, failure);
}

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
    private readonly ISecretProtector _secrets;
    private readonly string _cacheRoot;

    /// <summary>Pages Wallhaven en mémoire. Un couple (source, écran) n'a qu'un seul
    /// consommateur (sa cible de diaporama, protégée par FetchInProgress) : seul l'accès
    /// au dictionnaire lui-même doit être verrouillé.</summary>
    private readonly Dictionary<(string SourceId, string DeviceId), WallhavenPage> _wallhavenPages = [];

    /// <summary>URL d'origine de chaque fichier servi cette session (le cache est nommé par
    /// hash) — permet à « Enregistrer le fond actuel » de restituer le nom d'origine.</summary>
    private readonly Dictionary<string, Uri> _urlByPath = [];

    /// <summary>Téléchargements en vol, par chemin de cache : deux écrans sur la même
    /// source (Bing sur deux écrans, timers synchrones) partagent un seul téléchargement au
    /// lieu d'écrire le même fichier en concurrence (issue #45).</summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<bool>>> _inflight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Taille maximale d'une image téléchargée (une source custom mal réglée ne
    /// doit pas pouvoir remplir la mémoire ou le disque).</summary>
    internal const long MaxImageBytes = 50L * 1024 * 1024;

    public WebWallpaperFetcher(HttpClient http, ConfigService configService, ISecretProtector secrets)
    {
        _http = http;
        _configService = configService;
        _secrets = secrets;
        _cacheRoot = Path.Combine(configService.DataDirectory, "webcache");
    }

    /// <summary>
    /// Résout l'image courante de la source et la matérialise en fichier local.
    /// <paramref name="monitor"/> : écran destinataire, pour l'adaptation automatique
    /// Wallhaven (null en mode unifié — les filtres d'écran sont omis). Le nom de
    /// fichier est un hash de l'URL d'image : une URL déjà en cache n'est pas re-téléchargée
    /// et rend le même chemin — l'appelant peut ainsi détecter « image inchangée » par simple
    /// comparaison de chemins (source quotidienne, doublon d'une source aléatoire). En cas
    /// d'échec (réseau, timeout, HTTP, JSON inattendu…), le résultat porte un motif court
    /// localisé et la cause complète est journalisée — l'appelant garde le fond courant et
    /// retentera au tick suivant. Seule une annulation demandée via <paramref name="ct"/>
    /// lève : un timeout HTTP (aussi une <see cref="OperationCanceledException"/>) est un
    /// échec ordinaire (issue #46).
    /// </summary>
    public async Task<FetchResult> FetchAsync(WallpaperSourceConfig source, MonitorInfo? monitor, CancellationToken ct)
    {
        try
        {
            Uri imageUrl = source is { IsWallhaven: true, Wallhaven: not null }
                ? await NextWallhavenImageAsync(source, monitor, ct).ConfigureAwait(false)
                : await new HttpJsonSource(_http, source, ResolveApiKey(source)).GetImageUrlAsync(ct).ConfigureAwait(false);

            string? path = await DownloadAsync(source.Id, imageUrl, ct).ConfigureAwait(false);
            return path is null
                ? FetchResult.Failed(new FetchFailure(FetchFailureKind.UnexpectedResponse))
                : FetchResult.Success(path);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = DescribeFailure(ex);
            Log.Warn($"Source '{source.Name}' ({source.Id}): fetch failed — {failure.Kind}{(failure.HttpStatus is { } code ? $" {code}" : "")}", ex);
            return FetchResult.Failed(failure);
        }
    }

    /// <summary>Cause d'un échec, traduite par l'app pour la barre de statut.</summary>
    internal static FetchFailure DescribeFailure(Exception ex) => ex switch
    {
        OperationCanceledException => new(FetchFailureKind.Timeout), // annulation non demandée = timeout HttpClient
        HttpRequestException { StatusCode: { } status } => new(FetchFailureKind.Http, (int)status),
        HttpRequestException => new(FetchFailureKind.Network),
        NoResultsException => new(FetchFailureKind.NoResults),
        InvalidDataException => FetchFailure.InvalidImage,
        JsonException or InvalidOperationException or KeyNotFoundException or FormatException
            => new(FetchFailureKind.UnexpectedResponse),
        _ => FetchFailure.Other,
    };

    /// <summary>Recherche sans résultat (filtres trop stricts, NSFW sans clé…).</summary>
    internal sealed class NoResultsException(string message) : Exception(message);

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
            throw new NoResultsException("Wallhaven search returned no results.");
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

    /// <summary>
    /// Matérialise l'URL d'image en fichier du cache disque (nom = hash d'URL,
    /// re-téléchargement évité si déjà présent et valide). Garanties (issue #45) : un seul
    /// téléchargement par fichier à la fois, écriture dans un <c>.part</c> puis renommage
    /// (jamais de fichier partiel sous le nom final), signature d'image vérifiée (un portail
    /// captif qui répond du HTML en 200 n'empoisonne pas le cache), taille bornée.
    /// </summary>
    private async Task<string?> DownloadAsync(string sourceId, Uri imageUrl, CancellationToken ct)
    {
        string directory = Path.Combine(_cacheRoot, Sanitize(sourceId));
        Directory.CreateDirectory(directory);

        string fileName = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(imageUrl.AbsoluteUri)))[..16]
            + GuessExtension(imageUrl);
        string path = Path.Combine(directory, fileName);

        if (!IsCachedImage(path))
        {
            var download = _inflight.GetOrAdd(path, p => new Lazy<Task<bool>>(
                () => DownloadToCacheAsync(imageUrl, p, ct)));
            try
            {
                if (!await download.Value.ConfigureAwait(false))
                    return null;
            }
            finally
            {
                _inflight.TryRemove(KeyValuePair.Create(path, download));
            }
            Prune(directory, keepNewest: MaxCachedPerSource);
        }

        lock (_urlByPath)
        {
            _urlByPath[path] = imageUrl;
            // Borne (#48) : une entrée par image servie, sans fin sur une app qui tourne des
            // semaines. Les fichiers purgés du cache n'ont plus besoin de leur URL. (Après un
            // redémarrage, le premier fetch ré-enregistre l'URL même si le fichier est en cache.)
            if (_urlByPath.Count > MaxTrackedUrls)
            {
                foreach (string stale in _urlByPath.Keys.Where(p => !File.Exists(p)).ToList())
                    _urlByPath.Remove(stale);
            }
        }
        return path;
    }

    /// <summary>Seuil de nettoyage de <see cref="_urlByPath"/>.</summary>
    private const int MaxTrackedUrls = 256;

    /// <summary>Télécharge en flux vers un fichier temporaire unique, vérifie taille et
    /// signature, puis le renomme en <paramref name="path"/>. Faux si la réponse est vide.</summary>
    private async Task<bool> DownloadToCacheAsync(Uri imageUrl, string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxImageBytes)
            throw new InvalidDataException($"Image too large: {response.Content.Headers.ContentLength} bytes (max {MaxImageBytes}).");

        string part = $"{path}.{Guid.NewGuid():N}.part";
        try
        {
            long total = 0;
            byte[] header = new byte[ImageSignature.Length];
            int headerLength = 0;

            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    if (headerLength < header.Length)
                    {
                        int take = Math.Min(read, header.Length - headerLength);
                        Array.Copy(buffer, 0, header, headerLength, take);
                        headerLength += take;
                    }
                    total += read;
                    if (total > MaxImageBytes)
                        throw new InvalidDataException($"Image too large: more than {MaxImageBytes} bytes.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
            }

            if (total == 0)
                return false;
            if (!ImageSignature.IsImage(header.AsSpan(0, headerLength)))
            {
                throw new InvalidDataException(
                    $"Not an image (Content-Type '{response.Content.Headers.ContentType?.MediaType}', {total} bytes).");
            }

            try
            {
                // overwrite : remplace un fichier empoisonné d'une version antérieure. Les
                // téléchargements concurrents de ce chemin sont déjà dédoublonnés.
                File.Move(part, path, overwrite: true);
            }
            catch (IOException) when (IsCachedImage(path))
            {
                // Verrouillé mais déjà valide (autre instance de l'app…) : même contenu.
            }
            return true;
        }
        finally
        {
            try { File.Delete(part); } catch (Exception) { /* .part orphelin : purgé avec le dossier */ }
        }
    }

    /// <summary>Vrai si le fichier du cache existe et commence par une signature d'image
    /// (un fichier partiel ou empoisonné d'une version antérieure est re-téléchargé).</summary>
    private static bool IsCachedImage(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> header = stackalloc byte[ImageSignature.Length];
            int read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            return ImageSignature.IsImage(header[..read]);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Oublie un fichier du cache que le pipeline n'a pas pu utiliser (décodage ou
    /// composition en échec) : il sera re-téléchargé à la prochaine demande au lieu d'être
    /// resservi indéfiniment. Sans effet hors du dossier de cache.
    /// </summary>
    public void Invalidate(string path)
    {
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(Path.GetFullPath(_cacheRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            File.Delete(full);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not drop unusable cached image '{full}'", ex);
        }
        lock (_urlByPath)
            _urlByPath.Remove(path);
    }

    /// <summary>URL d'origine d'un fichier du cache (null si inconnue — fichier servi lors
    /// d'une session précédente).</summary>
    public Uri? SourceUrlFor(string path)
    {
        lock (_urlByPath)
            return _urlByPath.GetValueOrDefault(path);
    }

    /// <summary>Clé effective d'une source : magasin central (par fournisseur, chiffré
    /// DPAPI) pour un preset connu, sinon la clé portée par l'instance (source custom).
    /// Résolue à chaque requête : une clé changée dans Paramètres prend effet au tick suivant.</summary>
    private string? ResolveApiKey(WallpaperSourceConfig source)
    {
        if (source.IsCustom)
            return null;

        return _configService.Load().ApiKeys.TryGetValue(source.PresetId, out string? blob)
            ? _secrets.Unprotect(blob)
            : null;
    }

    private static string Sanitize(string name) => FileNames.Sanitize(name);

    private static string GuessExtension(Uri imageUrl)
    {
        string extension = Path.GetExtension(imageUrl.AbsolutePath);
        return ImageFiles.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ? extension : ".jpg";
    }

    private static void Prune(string directory, int keepNewest)
    {
        try
        {
            // Images seulement : les .part de téléchargements en cours ne comptent pas.
            var stale = new DirectoryInfo(directory)
                .EnumerateFiles()
                .Where(f => ImageFiles.Extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
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
