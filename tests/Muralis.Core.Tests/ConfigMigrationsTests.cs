using Muralis.Models;
using Muralis.Services;
using Muralis.Services.Sources;

namespace Muralis.Core.Tests;

/// <summary>Migrations de config (filet de sécurité pour #43 : elles tournent à chaque
/// démarrage et leur résultat est sauvé).</summary>
public class ConfigMigrationsTests
{
    private static AppConfig LegacyV1() => new()
    {
        Version = 1,
        Sources =
        [
            new WallpaperSourceConfig { Name = "Bing (image du jour)", RequestUrl = "https://www.bing.com/x", ImageUrlJsonPath = "images[0].url" },
            new WallpaperSourceConfig
            {
                Name = "Wallhaven (aléatoire)",
                RequestUrl = "https://wallhaven.cc/api/v1/search?categories=010&purity=110&q=%2Bcar%20-anime&atleast=2560x1440",
                ImageUrlJsonPath = "data[0].path",
                ApiKey = "secret-key",
            },
        ],
        Screens =
        [
            new ScreenConfig { DeviceId = "a", Mode = WallpaperMode.Slideshow, SourceType = "Wallhaven (aléatoire)" },
            new ScreenConfig { DeviceId = "b", Mode = WallpaperMode.Slideshow, SourceType = "Bing (image du jour)", SourcePath = "x" },
            new ScreenConfig { DeviceId = "c", Mode = WallpaperMode.Slideshow, SourceType = SlideshowService.LocalFolderSourceType },
        ],
    };

    [Fact]
    public void LegacyConfig_IsMigratedToCurrentVersion()
    {
        var config = LegacyV1();

        Assert.True(ConfigMigrations.Apply(config, Fakes.Secrets));
        Assert.Equal(AppConfig.CurrentVersion, config.Version);

        var bing = config.Sources[0];
        var wallhaven = config.Sources[1];
        Assert.Equal("Bing", bing.Name);
        Assert.Equal(SourceKind.Daily, bing.Kind);
        Assert.Equal("bing", bing.PresetId);
        Assert.Equal("wallhaven", wallhaven.PresetId);
        Assert.All(config.Sources, s => Assert.False(string.IsNullOrEmpty(s.Id)));

        // Références par nom → par Id ; la source quotidienne sort du diaporama.
        Assert.Equal(wallhaven.Id, config.Screens[0].SourceType);
        Assert.Equal(bing.Id, config.Screens[1].SourceType);
        Assert.Equal(WallpaperMode.Fixed, config.Screens[1].Mode);
        Assert.Equal(string.Empty, config.Screens[1].SourcePath);
        Assert.Equal(SlideshowService.LocalFolderSourceType, config.Screens[2].SourceType);
    }

    [Fact]
    public void LegacyWallhavenKey_MovesToEncryptedStore()
    {
        var config = LegacyV1();
        ConfigMigrations.Apply(config, Fakes.Secrets);

        var wallhaven = config.Sources[1];
        Assert.Equal(string.Empty, wallhaven.ApiKey);
        Assert.Equal("X-API-Key", wallhaven.ApiKeyHeader);
        Assert.Equal("secret-key", Fakes.Secrets.Unprotect(config.ApiKeys["wallhaven"]));
    }

    [Fact]
    public void LegacyWallhavenUrl_BecomesTypedOptions()
    {
        var config = LegacyV1();
        ConfigMigrations.Apply(config, Fakes.Secrets);

        var options = config.Sources[1].Wallhaven!;
        Assert.Equal(WallhavenUrlBuilder.SearchBaseUrl, config.Sources[1].RequestUrl);
        Assert.False(options.CategoryGeneral);
        Assert.True(options.CategoryAnime);
        Assert.True(options.PuritySfw);
        Assert.True(options.PuritySketchy);
        Assert.False(options.PurityNsfw);
        Assert.Equal("car, -anime", options.Tags);
        Assert.Equal("2560x1440", options.AtLeast);
        Assert.False(options.AutoFitScreen);
    }

    [Fact]
    public void Apply_IsIdempotent()
    {
        var config = LegacyV1();
        ConfigMigrations.Apply(config, Fakes.Secrets);

        Assert.False(ConfigMigrations.Apply(config, Fakes.Secrets));
    }

    [Fact]
    public void FreshConfig_OnlyBumpsVersion()
    {
        var config = new AppConfig();

        Assert.True(ConfigMigrations.Apply(config, Fakes.Secrets));
        Assert.Equal(AppConfig.CurrentVersion, config.Version);
        Assert.False(ConfigMigrations.Apply(config, Fakes.Secrets));
    }

    [Fact]
    public void V3_EnablesFullscreenPause_ForExistingConfigs()
    {
        var config = new AppConfig { Version = 2, PauseDuringFullscreen = false };

        Assert.True(ConfigMigrations.Apply(config, Fakes.Secrets));
        Assert.True(config.PauseDuringFullscreen);
    }

    [Fact]
    public void V3_KeepsTheUserChoice_OnceMigrated()
    {
        var config = new AppConfig { Version = 3, PauseDuringFullscreen = false };

        ConfigMigrations.Apply(config, Fakes.Secrets);
        Assert.False(config.PauseDuringFullscreen);
    }
}
