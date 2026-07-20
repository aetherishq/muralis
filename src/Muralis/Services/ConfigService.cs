using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Muralis.Models;

namespace Muralis.Services;

/// <summary>
/// Charge et sauvegarde la configuration dans <c>%LocalAppData%\Muralis\config.json</c>.
/// Tolérant aux erreurs : un fichier absent ou corrompu ne fait jamais planter le démarrage,
/// il retourne simplement une <see cref="AppConfig"/> vide.
/// </summary>
public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Dossier racine des données locales : <c>%LocalAppData%\Muralis</c>.</summary>
    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Muralis");

    public string ConfigPath => Path.Combine(DataDirectory, "config.json");

    /// <summary>Dossier de cache des images composées par écran.</summary>
    public string ComposedDirectory => Path.Combine(DataDirectory, "composed");

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return new AppConfig();

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch (Exception)
        {
            // Config illisible/corrompue : on repart d'une config vide plutôt que de planter.
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
