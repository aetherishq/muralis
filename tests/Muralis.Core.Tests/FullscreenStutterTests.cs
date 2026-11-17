using System.IO;
using System.Net.Http;
using Muralis.Models;
using Muralis.Services;
using Muralis.Services.Sources;

namespace Muralis.Core.Tests;

/// <summary>Issue #41 : report pendant un jeu plein écran, poses regroupées.</summary>
public sealed class FullscreenStutterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "muralis-tests-" + Guid.NewGuid().ToString("N"));

    public FullscreenStutterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static PixelRect Rect(int left, int top, int right, int bottom) => new() { Left = left, Top = top, Right = right, Bottom = bottom };

    [Fact]
    public async Task PosesWithinTheWindow_AreExecutedAsOneBatch()
    {
        var batches = new List<int>();
        var batcher = new PoseBatcher(batch =>
        {
            lock (batches)
                batches.Add(batch.Count);
            foreach (var request in batch)
                request.Succeed();
        }, TimeSpan.FromMilliseconds(200));

        await Task.WhenAll(
            batcher.Enqueue(new PoseRequest("A", "a.png", DesktopWallpaperPosition.Fill)),
            batcher.Enqueue(new PoseRequest("B", "b.png", DesktopWallpaperPosition.Fill)),
            batcher.Enqueue(new PoseRequest("C", "c.png", DesktopWallpaperPosition.Fill)));

        Assert.Equal([3], batches);
    }

    [Fact]
    public async Task OneFailedPose_DoesNotFailTheOthers()
    {
        var batcher = new PoseBatcher(batch =>
        {
            foreach (var request in batch)
            {
                if (request.DeviceId == "bad")
                    request.Fail(new InvalidOperationException("COM error"));
                else
                    request.Succeed();
            }
        }, TimeSpan.FromMilliseconds(50));

        var good = batcher.Enqueue(new PoseRequest("good", "a.png", DesktopWallpaperPosition.Fill));
        var bad = batcher.Enqueue(new PoseRequest("bad", "b.png", DesktopWallpaperPosition.Fill));

        await good;
        await Assert.ThrowsAsync<InvalidOperationException>(() => bad);
    }

    [Fact]
    public async Task SlideshowChange_WaitsForTheGameToEnd_WithoutStacking()
    {
        string folder = Path.Combine(_dir, "pics");
        Directory.CreateDirectory(folder);
        foreach (string name in new[] { "1.png", "2.png", "3.png" })
            File.WriteAllBytes(Path.Combine(folder, name), [0x89, 0x50, 0x4E, 0x47]);

        bool inGame = true;
        var wallpapers = new ApplyTests.FakeWallpaperService(_dir);
        var fetcher = new WebWallpaperFetcher(new HttpClient(), new ConfigService(_dir), Fakes.Secrets);
        var slideshow = new SlideshowService(wallpapers, fetcher, shouldDefer: () => inGame);
        var config = new AppConfig
        {
            Screens = [new ScreenConfig { DeviceId = "A", Mode = WallpaperMode.Slideshow, SourceType = "LocalFolder", SourcePath = folder }],
        };
        var monitor = new MonitorInfo("A", Rect(0, 0, 1920, 1080));

        slideshow.Restart(config, [monitor]); // première image prévue : différée
        slideshow.ResumeDeferred();            // toujours en jeu : rien
        await Task.Delay(200);
        Assert.Empty(wallpapers.Applied);

        inGame = false;
        slideshow.ResumeDeferred();            // jeu quitté : une seule pose
        await WaitUntil(() => wallpapers.Applied.Count == 1);
        await Task.Delay(200);
        Assert.Single(wallpapers.Applied);
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
