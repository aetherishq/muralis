using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Muralis.Models;
using Muralis.Services;
using Muralis.Services.Interop;
using Muralis.Services.Sources;
using Muralis.ViewModels;

namespace Muralis.Tests;

/// <summary>Implémentations Windows/WPF des abstractions du cœur (#52) et logique d'UI.</summary>
public sealed class WindowsSideTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "muralis-tests-" + Guid.NewGuid().ToString("N"));

    public WindowsSideTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static RECT Rect(int left, int top, int right, int bottom) => new() { Left = left, Top = top, Right = right, Bottom = bottom };

    // ---- Détection plein écran (#41) ----

    [Fact]
    public void Borderless_CoveringTheMonitor_IsFullscreen()
    {
        var monitor = Rect(1920, 0, 4480, 1440);

        Assert.True(FullscreenDetector.CoversMonitor(Rect(1920, 0, 4480, 1440), monitor, hasCaption: false));
        Assert.True(FullscreenDetector.CoversMonitor(Rect(1912, -8, 4488, 1448), monitor, hasCaption: false));
    }

    [Fact]
    public void MaximizedWindowWithTitleBar_OrSmallerWindow_IsNotFullscreen()
    {
        var monitor = Rect(0, 0, 1920, 1080);

        Assert.False(FullscreenDetector.CoversMonitor(Rect(-8, -8, 1928, 1088), monitor, hasCaption: true));
        Assert.False(FullscreenDetector.CoversMonitor(Rect(0, 0, 1920, 1040), monitor, hasCaption: false));
    }

    // ---- Compositeur WPF ----

    private string Png(string name, int width = 8, int height = 8)
    {
        string path = Path.Combine(_dir, name);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null,
            Enumerable.Repeat((byte)128, width * height).ToArray(), width);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    [Fact]
    public async Task Composition_RunsOnADedicatedStaThread()
    {
        string source = Png("source.png");
        Assert.Equal(ApartmentState.MTA, Thread.CurrentThread.GetApartmentState());
        IImageComposer composer = new WallpaperComposer(new ConfigService(Path.Combine(_dir, "data")));

        string composed = await composer.ComposeAsync(source, 32, 18, DesktopWallpaperPosition.Fill);

        Assert.True(File.Exists(composed));
    }

    [Fact]
    public async Task UndecodableSource_RaisesInvalidData_PerTheComposerContract()
    {
        string garbage = Path.Combine(_dir, "portal.jpg");
        File.WriteAllText(garbage, "<html>captive portal</html>");
        IImageComposer composer = new WallpaperComposer(new ConfigService(Path.Combine(_dir, "data")));

        await Assert.ThrowsAsync<InvalidDataException>(() => composer.ComposeAsync(garbage, 32, 18, DesktopWallpaperPosition.Fill));
    }

    [Fact]
    public void Geometry_Fit_LetterboxesAndCenters()
    {
        // 4:3 sur 16:9 → hauteur pleine, bandes verticales.
        Assert.Equal((240.0, 0.0, 1440.0, 1080.0), WallpaperComposer.ComputeDestRect(800, 600, 1920, 1080, DesktopWallpaperPosition.Fit));
    }

    [Fact]
    public void Geometry_Fill_CoversAndOverflows()
    {
        // 4:3 sur 16:9 → largeur pleine, débord haut/bas découpé.
        Assert.Equal((0.0, -180.0, 1920.0, 1440.0), WallpaperComposer.ComputeDestRect(800, 600, 1920, 1080, DesktopWallpaperPosition.Fill));
    }

    [Fact]
    public void Geometry_Center_AndStretch()
    {
        Assert.Equal((560.0, 240.0, 800.0, 600.0), WallpaperComposer.ComputeDestRect(800, 600, 1920, 1080, DesktopWallpaperPosition.Center));
        Assert.Equal((0.0, 0.0, 1920.0, 1080.0), WallpaperComposer.ComputeDestRect(800, 600, 1920, 1080, DesktopWallpaperPosition.Stretch));
    }

    // ---- DPAPI (review s4) ----

    [Fact]
    public void Dpapi_RoundTrips_AndUpgradesLegacyBlobs()
    {
        var protector = new DpapiSecretProtector();
        Assert.Equal("key", protector.Unprotect(protector.Protect("key")));

        // Blob écrit par une version antérieure (sans entropie) : lisible, puis ré-chiffré.
        string legacy = Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes("old-key"), optionalEntropy: null, DataProtectionScope.CurrentUser));
        var config = new AppConfig { ApiKeys = { ["wallhaven"] = legacy } };
        Assert.Equal("old-key", protector.Unprotect(legacy));

        Assert.True(protector.UpgradeLegacyBlobs(config));
        Assert.NotEqual(legacy, config.ApiKeys["wallhaven"]);
        Assert.Equal("old-key", protector.Unprotect(config.ApiKeys["wallhaven"]));
        Assert.False(protector.UpgradeLegacyBlobs(config)); // déjà à jour
        Assert.Null(protector.Unprotect("not-a-blob"));
    }

    // ---- Textes de l'UI ----

    [Fact]
    public void FetchFailureText_IsLocalizedFromTheCause()
    {
        Assert.Contains("401", FetchFailureText.Describe(new FetchFailure(FetchFailureKind.Http, 401)));
        Assert.NotEqual(FetchFailureText.Describe(FetchFailure.Other), FetchFailureText.Describe(FetchFailure.InvalidImage));
    }

    // ---- « Appliquer » une fois dédocké (#42) ----

    [Fact]
    public void Apply_KeepsDisconnectedScreens_ButReplacesAdoptedOnes()
    {
        var merged = SettingsViewModel.MergeScreens(
            edited: [new ScreenConfig { DeviceId = "laptop" }, new ScreenConfig { DeviceId = "new-port" }],
            persisted: [new ScreenConfig { DeviceId = "docked" }, new ScreenConfig { DeviceId = "old-port" }, new ScreenConfig { DeviceId = "laptop" }],
            connected: new HashSet<string> { "laptop", "new-port" },
            adopted: new HashSet<string> { "old-port" });

        Assert.Equal(["laptop", "new-port", "docked"], merged.Select(s => s.DeviceId));
    }
}
