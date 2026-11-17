using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Muralis.Models;
using Muralis.Services;
using Muralis.Services.Interop;
using Muralis.Services.Sources;

namespace Muralis.Tests;

/// <summary>Issue #47 : un écran en échec interrompait « Appliquer » (diaporamas non relancés),
/// et une source retirée/modifiée continuait d'être interrogée avec ses anciens réglages.</summary>
public sealed class ApplyTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "muralis-tests-" + Guid.NewGuid().ToString("N"));

    public ApplyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static MonitorInfo Monitor(string id, int left) =>
        new(id, new RECT { Left = left, Top = 0, Right = left + 1920, Bottom = 1080 });

    private string Image(string name)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, Png);
        return path;
    }

    [Fact]
    public void Apply_OneFailingScreen_DoesNotBlockTheOthers()
    {
        var monitors = new[] { Monitor("A", 0), Monitor("B", 1920), Monitor("C", 3840) };
        var config = new AppConfig
        {
            Screens = monitors.Select(m => new ScreenConfig { DeviceId = m.DeviceId, SourcePath = Image(m.DeviceId + ".png") }).ToList(),
        };
        var service = new FakeWallpaperService(_dir) { FailOn = "A" };

        var report = service.Apply(config, monitors);

        Assert.Equal(new ApplyReport(Applied: 2, Failed: 1), report);
        Assert.Equal(["B", "C"], service.Applied.Select(a => a.DeviceId).Order());
    }

    [Fact]
    public void Apply_NeverThrows_InUnifiedMode()
    {
        var config = new AppConfig { Unified = true, UnifiedConfig = new ScreenConfig { SourcePath = Image("all.png") } };
        var service = new FakeWallpaperService(_dir) { FailOn = "*" };

        Assert.Equal(new ApplyReport(Applied: 0, Failed: 1), service.Apply(config, [Monitor("A", 0)]));
    }

    [Fact]
    public async Task RemovedSource_IsNoLongerUsed_EvenWithAFetchInFlight()
    {
        var (slideshow, wallpapers, server, config) = WebSlideshow(imageDelay: TimeSpan.FromMilliseconds(400));
        slideshow.Restart(config, [Monitor("A", 0)]); // premier fetch en vol

        config.Sources.Clear();                        // retrait de la source (page Sources)
        slideshow.RefreshSource(config, "web");

        await Task.Delay(1000);
        Assert.Empty(wallpapers.Applied);              // l'image arrivée après coup n'est pas posée
        Assert.Empty(slideshow.CurrentWebImages());
        Assert.Equal(1, server.ImageRequests);         // et la source n'est plus interrogée
    }

    [Fact]
    public async Task EditedSource_RestartsItsTargets_WithTheNewSettings()
    {
        var (slideshow, wallpapers, server, config) = WebSlideshow(imageDelay: TimeSpan.Zero);
        slideshow.Restart(config, [Monitor("A", 0)]);
        await WaitUntil(() => wallpapers.Applied.Count == 1);

        server.ImageUrl = "https://img.example.invalid/second.png"; // réglages modifiés → autre image
        slideshow.RefreshSource(config, "web");

        await WaitUntil(() => wallpapers.Applied.Count == 2);
        Assert.Single(slideshow.CurrentWebImages());
    }

    [Fact]
    public void RefreshSource_LeavesOtherTargetsAlone()
    {
        var (slideshow, _, _, config) = WebSlideshow(imageDelay: TimeSpan.FromSeconds(5));
        slideshow.Restart(config, [Monitor("A", 0)]);

        slideshow.RefreshSource(config, "some-other-source"); // aucune cible concernée : no-op
        Assert.Empty(slideshow.CurrentWebImages());           // (rien posé, rien cassé)
    }

    private (SlideshowService, FakeWallpaperService, FakeServer, AppConfig) WebSlideshow(TimeSpan imageDelay)
    {
        var configService = new ConfigService(_dir);
        var wallpapers = new FakeWallpaperService(_dir);
        var server = new FakeServer(imageDelay);
        var fetcher = new WebWallpaperFetcher(new HttpClient(server), configService);
        var config = new AppConfig
        {
            Sources =
            [
                new WallpaperSourceConfig
                {
                    Id = "web", Name = "Web", Kind = SourceKind.Random,
                    RequestUrl = "https://api.example.invalid/random", ImageUrlJsonPath = "url",
                },
            ],
            Screens = [new ScreenConfig { DeviceId = "A", Mode = WallpaperMode.Slideshow, SourceType = "web" }],
        };
        return (new SlideshowService(wallpapers, fetcher), wallpapers, server, config);
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

    /// <summary>Enregistre les poses au lieu d'appeler IDesktopWallpaper.</summary>
    private sealed class FakeWallpaperService(string dir) : WallpaperService(new WallpaperComposer(new ConfigService(dir)))
    {
        public string? FailOn { get; init; }

        public ConcurrentQueue<(string DeviceId, string Path)> Applied { get; } = new();

        public override void ApplyMonitor(MonitorInfo monitor, string imagePath, DesktopWallpaperPosition displayMode)
        {
            if (FailOn == monitor.DeviceId || FailOn == "*")
                throw new NotSupportedException("No imaging component suitable to complete this operation.");
            Applied.Enqueue((monitor.DeviceId, imagePath));
        }

        public override void ApplyAllMonitors(string imagePath, DesktopWallpaperPosition displayMode, IReadOnlyList<MonitorInfo> monitors)
        {
            if (FailOn == "*")
                throw new NotSupportedException("No imaging component suitable to complete this operation.");
            Applied.Enqueue((string.Empty, imagePath));
        }
    }

    private sealed class FakeServer(TimeSpan imageDelay) : HttpMessageHandler
    {
        private int _imageRequests;

        public string ImageUrl { get; set; } = "https://img.example.invalid/first.png";

        public int ImageRequests => _imageRequests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host == "api.example.invalid")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{ "url": "{{ImageUrl}}" }""") };

            Interlocked.Increment(ref _imageRequests);
            if (imageDelay > TimeSpan.Zero)
                await Task.Delay(imageDelay, ct);
            var content = new ByteArrayContent(Png);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}
