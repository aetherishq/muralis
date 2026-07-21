using System.Net.Http;
using System.Text.Json;
using Muralis.Models;

namespace Muralis.Services.Sources;

/// <summary>
/// Implémentation générique d'<see cref="IWallpaperSource"/> pour toute API qui renvoie du JSON
/// contenant une URL d'image : GET sur <see cref="WallpaperSourceConfig.RequestUrl"/> (+ en-tête
/// de clé API éventuel), extraction via un chemin JSON simple (<c>images[0].url</c>, <c>[0].file_url</c>,
/// <c>urls.full</c>…). Les URLs relatives sont résolues contre l'URL de requête (cas Bing).
/// </summary>
public class HttpJsonSource : IWallpaperSource
{
    private readonly HttpClient _http;
    private readonly WallpaperSourceConfig _config;

    public HttpJsonSource(HttpClient http, WallpaperSourceConfig config)
    {
        _http = http;
        _config = config;
    }

    public string Name => _config.Name;

    public async Task<Uri> GetImageUrlAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _config.RequestUrl);
        if (!string.IsNullOrWhiteSpace(_config.ApiKeyHeader) && !string.IsNullOrWhiteSpace(_config.ApiKey))
            request.Headers.TryAddWithoutValidation(_config.ApiKeyHeader, _config.ApiKey);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);

        string? imageUrl = Navigate(document.RootElement, _config.ImageUrlJsonPath).GetString();
        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new InvalidOperationException($"Chemin JSON « {_config.ImageUrlJsonPath} » : URL vide.");

        // Uri(base, relative) laisse une URL absolue inchangée.
        return new Uri(new Uri(_config.RequestUrl), imageUrl);
    }

    /// <summary>
    /// Navigation JSON minimaliste : segments séparés par des points, index de tableau entre
    /// crochets. Ex. <c>images[0].url</c>, <c>[0].file_url</c> (racine tableau), <c>urls.full</c>.
    /// </summary>
    internal static JsonElement Navigate(JsonElement element, string path)
    {
        var current = element;
        foreach (string segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            string rest = segment;
            while (rest.Length > 0)
            {
                int bracket = rest.IndexOf('[');
                string name = bracket < 0 ? rest : rest[..bracket];

                if (name.Length > 0)
                {
                    if (!current.TryGetProperty(name, out current))
                        throw new InvalidOperationException($"Propriété JSON « {name} » absente (chemin « {path} »).");
                    rest = bracket < 0 ? string.Empty : rest[bracket..];
                    continue;
                }

                int close = rest.IndexOf(']');
                if (close < 2 || !int.TryParse(rest[1..close], out int index))
                    throw new InvalidOperationException($"Index de tableau invalide dans « {path} ».");
                if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
                    throw new InvalidOperationException($"Index [{index}] hors du tableau (chemin « {path} »).");

                current = current[index];
                rest = rest[(close + 1)..];
            }
        }
        return current;
    }
}
