using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Time.Testing;
using Muralis.Models;
using Muralis.Services;
using Muralis.Services.Sources;

namespace Muralis.Core.Tests;

/// <summary>Issue #52 : le cœur tourne sans Windows — temps simulé (TimeProvider), bureau
/// et compositeur abstraits.</summary>
public sealed class CorePlatformTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "muralis-tests-" + Guid.NewGuid().ToString("N"));

    public CorePlatformTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static MonitorInfo Monitor(string id, int left = 0) => new(id, new PixelRect(left, 0, left + 1920, 1080));

    [Fact]
    public void RecurringTimer_TicksEveryInterval_AndStops()
    {
        var time = new FakeTimeProvider();
        int ticks = 0;
        using var timer = new RecurringTimer(time, context: null, TimeSpan.FromMinutes(5), () => ticks++);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(0, ticks);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, ticks);
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(3, ticks);

        timer.Stop();
        time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(3, ticks);
    }

    [Fact]
    public void RecurringTimer_Restart_WaitsAFullInterval()
    {
        var time = new FakeTimeProvider();
        int ticks = 0;
        using var timer = new RecurringTimer(time, context: null, TimeSpan.FromMinutes(5), () => ticks++);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(4));
        timer.Start(); // « Image suivante » : le compteur repart
        time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(0, ticks);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, ticks);
    }

    private (SlideshowService Slideshow, ApplyTests.FakeWallpaperService Wallpapers, AppConfig Config) LocalSlideshow(
        FakeTimeProvider time, Func<bool>? shouldDefer = null)
    {
        string folder = Path.Combine(_dir, "pics");
        Directory.CreateDirectory(folder);
        foreach (string name in new[] { "1.png", "2.png", "3.png" })
            File.WriteAllBytes(Path.Combine(folder, name), [0x89, 0x50, 0x4E, 0x47]);

        var wallpapers = new ApplyTests.FakeWallpaperService(_dir);
        var fetcher = new WebWallpaperFetcher(new HttpClient(), new ConfigService(_dir), Fakes.Secrets);
        var slideshow = new SlideshowService(wallpapers, fetcher, shouldDefer, time, context: null);
        var config = new AppConfig
        {
            Screens =
            [
                new ScreenConfig
                {
                    DeviceId = "A", Mode = WallpaperMode.Slideshow, SourceType = "LocalFolder", SourcePath = folder,
                    SlideshowInterval = TimeSpan.FromMinutes(10), Shuffle = false,
                },
            ],
        };
        return (slideshow, wallpapers, config);
    }

    [Fact]
    public async Task Slideshow_AdvancesOnSimulatedTime()
    {
        var time = new FakeTimeProvider();
        var (slideshow, wallpapers, config) = LocalSlideshow(time);

        slideshow.Restart(config, [Monitor("A")]);
        await WaitUntil(() => wallpapers.Applied.Count == 1);

        time.Advance(TimeSpan.FromMinutes(10));
        await WaitUntil(() => wallpapers.Applied.Count == 2);

        Assert.Equal(["1.png", "2.png"], wallpapers.Applied.Select(a => Path.GetFileName(a.Path)));
        slideshow.Stop();
    }

    [Fact]
    public async Task DeferredChange_IsAppliedAtTheNextRecheck_AfterTheGame()
    {
        var time = new FakeTimeProvider();
        bool inGame = true;
        var (slideshow, wallpapers, config) = LocalSlideshow(time, () => inGame);

        slideshow.Restart(config, [Monitor("A")]);
        time.Advance(TimeSpan.FromMinutes(30)); // ticks du diaporama et re-vérifications : toujours en jeu
        await Task.Delay(100);
        Assert.Empty(wallpapers.Applied);

        inGame = false;
        time.Advance(SlideshowService.DeferRecheckInterval);
        await WaitUntil(() => wallpapers.Applied.Count == 1);
        await Task.Delay(100);
        Assert.Single(wallpapers.Applied); // une seule pose, pas d'empilement
        slideshow.Stop();
    }

    [Fact]
    public async Task Apply_PosesAllScreensInOneBatch()
    {
        var desktop = new Fakes.Desktop();
        var service = new WallpaperService(new Fakes.Composer(), desktop, TimeSpan.FromMilliseconds(50));
        string image = Path.Combine(_dir, "fixed.png");
        File.WriteAllBytes(image, [0x89, 0x50, 0x4E, 0x47]);
        var monitors = new[] { Monitor("A"), Monitor("B", 1920) };
        var config = new AppConfig
        {
            Screens = monitors.Select(m => new ScreenConfig { DeviceId = m.DeviceId, SourcePath = image }).ToList(),
        };

        var report = await service.ApplyAsync(config, monitors);

        Assert.Equal(new ApplyReport(Applied: 2, Failed: 0), report);
        Assert.Single(desktop.Batches);
        Assert.Equal(image, service.GetWallpaper("B"));
    }

    [Fact]
    public void UndecodableImage_IsRecognizedByTheComposerContract()
    {
        Assert.True(SlideshowService.IsUndecodableImage(new InvalidDataException("bad image")));
        Assert.False(SlideshowService.IsUndecodableImage(new InvalidOperationException("COM error")));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met in time.");
            await Task.Delay(20);
        }
    }
}
