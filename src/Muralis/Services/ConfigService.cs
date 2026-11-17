using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Muralis.Models;

namespace Muralis.Services;

/// <summary>
/// Charge et sauvegarde la configuration dans <c>%LocalAppData%\Muralis\config.json</c>.
///
/// Garanties (issue #43) :
/// <list type="bullet">
/// <item>Écriture atomique : fichier temporaire écrit sur disque (write-through) puis
/// substitué via <see cref="File.Replace(string, string, string?)"/> — une coupure laisse
/// l'ancienne config ou la nouvelle, jamais un JSON tronqué. La version précédente est
/// gardée en <c>config.json.bak</c>.</item>
/// <item>Une config illisible n'est <b>jamais</b> écrasée : elle est mise en quarantaine
/// (<c>config.corrupt-&lt;horodatage&gt;.json</c>) puis la sauvegarde <c>.bak</c> est
/// restaurée si elle est lisible, sinon on repart d'une config vide.</item>
/// <item>Une config écrite par une version plus récente de Muralis
/// (<see cref="AppConfig.Version"/> &gt; <see cref="AppConfig.CurrentVersion"/>) est lue
/// mais jamais réécrite : un downgrade ne détruit rien.</item>
/// </list>
/// La config est mise en cache en mémoire (sous forme sérialisée : chaque
/// <see cref="Load"/> rend une instance indépendante, mutable sans effet de bord) — plus de
/// lecture disque à chaque requête web. Les éditions manuelles de config.json pendant que
/// l'app tourne ne sont donc prises en compte qu'au redémarrage.
/// </summary>
public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();

    /// <summary>Dernière config connue, sérialisée (null : pas encore chargée).</summary>
    private string? _cachedJson;

    /// <summary>Vrai quand le fichier sur disque ne doit pas être réécrit (config d'une
    /// version future, ou config illisible qu'on n'a pas pu mettre en quarantaine).</summary>
    private bool _readOnly;

    public ConfigService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Muralis"))
    {
    }

    /// <summary>Dossier de données injectable (tests).</summary>
    public ConfigService(string dataDirectory)
    {
        DataDirectory = dataDirectory;
    }

    /// <summary>Dossier racine des données locales : <c>%LocalAppData%\Muralis</c>.</summary>
    public string DataDirectory { get; }

    public string ConfigPath => Path.Combine(DataDirectory, "config.json");

    /// <summary>Version précédente de la config, conservée à chaque sauvegarde.</summary>
    public string BackupPath => ConfigPath + ".bak";

    /// <summary>Dossier de cache des images composées par écran.</summary>
    public string ComposedDirectory => Path.Combine(DataDirectory, "composed");

    /// <summary>Vrai si les sauvegardes sont ignorées pour protéger le fichier existant
    /// (cf. résumé de la classe).</summary>
    public bool IsReadOnly
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _readOnly;
            }
        }
    }

    /// <summary>Config courante — une nouvelle instance à chaque appel.</summary>
    public AppConfig Load()
    {
        lock (_gate)
        {
            EnsureLoaded();
            return Deserialize(_cachedJson!) ?? new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        string json = JsonSerializer.Serialize(config, JsonOptions);
        lock (_gate)
        {
            EnsureLoaded();
            // Le cache suit toujours : l'app reste cohérente pendant la session même quand
            // le disque est protégé.
            _cachedJson = json;
            if (_readOnly)
                return;

            Directory.CreateDirectory(DataDirectory);
            WriteAtomically(json);
        }
    }

    private void EnsureLoaded()
    {
        if (_cachedJson is not null)
            return;
        _cachedJson = JsonSerializer.Serialize(ReadFromDisk(), JsonOptions);
    }

    private AppConfig ReadFromDisk()
    {
        if (!File.Exists(ConfigPath))
        {
            // Premier lancement — ou crash entre deux étapes d'un ancien remplacement :
            // une sauvegarde lisible vaut mieux qu'une config vide.
            return TryReadFile(BackupPath, out var fromBackup, out _) ? fromBackup! : new AppConfig();
        }

        if (TryReadFile(ConfigPath, out var config, out int? version))
        {
            if (config!.Version > AppConfig.CurrentVersion)
            {
                Log.Warn($"config.json is from a newer version ({config.Version} > {AppConfig.CurrentVersion}): read-only, never rewritten");
                _readOnly = true;
            }
            return config;
        }

        if (version > AppConfig.CurrentVersion)
        {
            // Écrite par une version plus récente (valeur d'enum inconnue…) : illisible ici,
            // mais légitime. On ne touche à rien et on tourne sur une config vide.
            Log.Warn($"config.json from newer version {version} cannot be read by this build: running on defaults, file left untouched");
            _readOnly = true;
            return new AppConfig();
        }

        // Réellement corrompue : on la préserve avant tout.
        if (!TryQuarantine(out string? quarantined))
        {
            Log.Error("config.json is unreadable and could not be quarantined: running on defaults, file left untouched");
            _readOnly = true; // impossible de la mettre à l'abri : ne surtout pas l'écraser
            return new AppConfig();
        }

        if (TryReadFile(BackupPath, out var restored, out _))
        {
            Log.Warn($"config.json was unreadable: moved to '{quarantined}', restored from config.json.bak");
            try { File.Copy(BackupPath, ConfigPath, overwrite: false); }
            catch (Exception) { /* sera réécrite à la prochaine sauvegarde */ }
            return restored!;
        }

        Log.Warn($"config.json was unreadable: moved to '{quarantined}', no usable backup — starting from defaults");
        return new AppConfig();
    }

    /// <summary>Lit et désérialise un fichier de config. <paramref name="version"/> est lue
    /// à part (quand le JSON est bien formé) pour reconnaître une config future même si sa
    /// désérialisation typée échoue.</summary>
    private static bool TryReadFile(string path, out AppConfig? config, out int? version)
    {
        config = null;
        version = null;
        string json;
        try
        {
            if (!File.Exists(path))
                return false;
            json = File.ReadAllText(path);
        }
        catch (Exception)
        {
            return false;
        }

        version = TryReadVersion(json);
        try
        {
            config = Deserialize(json);
            return config is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static int? TryReadVersion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(nameof(AppConfig.Version), out var v)
                && v.TryGetInt32(out int value)
                ? value
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static AppConfig? Deserialize(string json) =>
        JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);

    private bool TryQuarantine(out string? target)
    {
        target = Path.Combine(DataDirectory, $"config.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        try
        {
            File.Move(ConfigPath, target, overwrite: false);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not quarantine config.json to '{target}'", ex);
            target = null;
            return false;
        }
    }

    private void WriteAtomically(string json)
    {
        string tmp = ConfigPath + ".tmp";

        // Write-through + flush disque : après le Replace, le contenu est réellement sur
        // le disque (sinon une coupure peut laisser un fichier renommé mais vide).
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(ConfigPath))
            File.Replace(tmp, ConfigPath, BackupPath, ignoreMetadataErrors: true);
        else
            File.Move(tmp, ConfigPath);
    }
}
