using System.IO;
using Muralis.Models;
using Muralis.Services;

namespace Muralis.Core.Tests;

/// <summary>Issue #43 : une config illisible ne doit jamais être écrasée, l'écriture doit
/// être atomique et une config d'une version future jamais réécrite.</summary>
public sealed class ConfigServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "muralis-tests-" + Guid.NewGuid().ToString("N"));

    public ConfigServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private ConfigService NewService() => new(_dir);

    private string ConfigPath => Path.Combine(_dir, "config.json");

    private string[] QuarantinedFiles() => Directory.GetFiles(_dir, "config.corrupt-*.json");

    /// <summary>Séquence exacte de <c>App.OnStartup</c> : charger, migrer, sauver si changé.</summary>
    private static AppConfig RunStartup(ConfigService service)
    {
        var config = service.Load();
        if (ConfigMigrations.Apply(config, Fakes.Secrets))
            service.Save(config);
        return config;
    }

    private static AppConfig SampleConfig() => new()
    {
        Version = AppConfig.CurrentVersion,
        Theme = ThemePreference.Light,
        Language = "fr",
        Screens = [new ScreenConfig { DeviceId = @"\\?\DISPLAY#TEST", SourcePath = @"C:\img.jpg" }],
    };

    [Fact]
    public void MissingFile_LoadsDefaults_AndSaveCreatesIt()
    {
        var service = NewService();
        Assert.Empty(service.Load().Screens);

        service.Save(SampleConfig());

        Assert.True(File.Exists(ConfigPath));
        Assert.Equal("fr", NewService().Load().Language);
    }

    [Fact]
    public void Save_RoundTrips_AndLeavesNoTempFile()
    {
        NewService().Save(SampleConfig());

        var reloaded = NewService().Load();
        Assert.Equal(ThemePreference.Light, reloaded.Theme);
        Assert.Equal(@"\\?\DISPLAY#TEST", Assert.Single(reloaded.Screens).DeviceId);
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void Save_KeepsPreviousVersionAsBackup()
    {
        var service = NewService();
        service.Save(SampleConfig());
        var second = SampleConfig();
        second.Language = "en";
        service.Save(second);

        Assert.Contains("\"fr\"", File.ReadAllText(service.BackupPath));
        Assert.Contains("\"en\"", File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void TruncatedConfig_IsNeverOverwrittenAtStartup()
    {
        // Le bug : JSON tronqué → config vide → migration → sauvée par-dessus l'original.
        string truncated = "{\n  \"Version\": 2,\n  \"Theme\": \"Light\",\n  \"Screens\": [ { \"DeviceId\": \"x\"";
        File.WriteAllText(ConfigPath, truncated);

        RunStartup(NewService());

        string quarantined = Assert.Single(QuarantinedFiles());
        Assert.Equal(truncated, File.ReadAllText(quarantined));
    }

    [Fact]
    public void TruncatedConfig_IsRestoredFromBackup()
    {
        var service = NewService();
        service.Save(SampleConfig());
        service.Save(SampleConfig()); // .bak = config valide
        File.WriteAllText(ConfigPath, "{ \"Version\": 2, \"Thè");

        var restarted = NewService();
        var config = RunStartup(restarted);

        Assert.Equal("fr", config.Language);
        Assert.Single(config.Screens);
        Assert.Single(QuarantinedFiles());
        Assert.False(restarted.IsReadOnly);
        Assert.Equal("fr", NewService().Load().Language); // restaurée sur disque aussi
    }

    [Fact]
    public void FutureVersion_IsReadButNeverRewritten()
    {
        string future = """
            { "Version": 99, "Theme": "Dark", "Language": "en", "Screens": [] }
            """;
        File.WriteAllText(ConfigPath, future);

        var service = NewService();
        var config = RunStartup(service);
        config.Language = "fr";
        service.Save(config);

        Assert.True(service.IsReadOnly);
        Assert.Equal(future, File.ReadAllText(ConfigPath));
        Assert.Equal("fr", service.Load().Language); // la session reste cohérente en mémoire
        Assert.Empty(QuarantinedFiles());
    }

    [Fact]
    public void FutureVersion_WithUnknownEnum_IsLeftUntouched()
    {
        // Downgrade : une version future a ajouté une valeur d'enum que ce build ignore.
        string future = """
            { "Version": 99, "Theme": "HighContrast", "Screens": [] }
            """;
        File.WriteAllText(ConfigPath, future);

        var service = NewService();
        RunStartup(service);

        Assert.True(service.IsReadOnly);
        Assert.Equal(future, File.ReadAllText(ConfigPath));
        Assert.Empty(QuarantinedFiles());
    }

    [Fact]
    public void Load_ReturnsIndependentInstances()
    {
        var service = NewService();
        service.Save(SampleConfig());

        var first = service.Load();
        first.Language = "en"; // mutation non sauvée

        Assert.Equal("fr", service.Load().Language);
    }

    [Fact]
    public void Load_IsServedFromMemory_AfterFirstRead()
    {
        var service = NewService();
        service.Save(SampleConfig());
        service.Load();

        File.Delete(ConfigPath); // plus de lecture disque à chaque requête web (m8)

        Assert.Equal("fr", service.Load().Language);
    }
}
