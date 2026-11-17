using System.Text.Json;
using Muralis.Models;
using Muralis.Services;
using Muralis.Services.Sources;

namespace Muralis.Core.Tests;

/// <summary>Issue #51 : logique pure extraite des services (diaporama, cache, sources web).</summary>
public class PureLogicTests
{
    // ---- Wallhaven ----

    private static MonitorInfo Monitor(int width, int height) =>
        new("M", new PixelRect { Left = 0, Top = 0, Right = width, Bottom = height });

    [Fact]
    public void Wallhaven_Tags_AreAllRequired_AndExclusionsKept()
    {
        Assert.Equal("%2Bnature%20%2Bsunset%20-anime", WallhavenUrlBuilder.BuildTagsQuery(" nature, sunset ,-anime,, "));
        Assert.Equal("", WallhavenUrlBuilder.BuildTagsQuery(""));
    }

    [Fact]
    public void Wallhaven_AutoFit_UsesTheTargetScreen()
    {
        var options = new WallhavenSourceOptions { AutoFitScreen = true };

        string portrait = WallhavenUrlBuilder.Build(options, Monitor(1080, 1920));

        Assert.StartsWith(WallhavenUrlBuilder.SearchBaseUrl + "?sorting=random&", portrait);
        Assert.Contains("atleast=1080x1920", portrait);
        Assert.Contains("ratios=portrait", portrait);
        Assert.DoesNotContain("atleast", WallhavenUrlBuilder.Build(options, monitor: null)); // mode unifié
    }

    [Fact]
    public void Wallhaven_EmptyFilters_FallBackToDefaults()
    {
        var options = new WallhavenSourceOptions
        {
            CategoryGeneral = false, CategoryAnime = false, CategoryPeople = false,
            PuritySfw = false, PuritySketchy = false, PurityNsfw = false,
        };

        string url = WallhavenUrlBuilder.Build(options, monitor: null);

        Assert.Contains("categories=101", url);
        Assert.Contains("purity=100", url);
    }

    [Fact]
    public void Wallhaven_ManualFilters_AreEscaped()
    {
        var options = new WallhavenSourceOptions { AutoFitScreen = false, AtLeast = " 2560x1440 ", Ratios = "16x9,21x9" };

        string url = WallhavenUrlBuilder.Build(options, Monitor(1920, 1080));

        Assert.Contains("atleast=2560x1440", url);
        Assert.Contains("ratios=16x9%2C21x9", url);
    }

    // ---- Chemin JSON des sources personnalisées ----

    [Theory]
    [InlineData("""{ "images": [ { "url": "/a.jpg" } ] }""", "images[0].url", "/a.jpg")]
    [InlineData("""[ { "file_url": "b.png" } ]""", "[0].file_url", "b.png")]
    [InlineData("""{ "urls": { "full": "c.webp" } }""", "urls.full", "c.webp")]
    [InlineData("""{ "a": [ [ "x", "d.jpg" ] ] }""", "a[0][1]", "d.jpg")]
    public void JsonPath_Navigates(string json, string path, string expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expected, HttpJsonSource.Navigate(document.RootElement, path).GetString());
    }

    [Theory]
    [InlineData("""{ "images": [] }""", "images[0].url")]
    [InlineData("""{ "images": [] }""", "missing")]
    [InlineData("""{ "images": [] }""", "images[x]")]
    public void JsonPath_ReportsInvalidPaths(string json, string path)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<InvalidOperationException>(() => HttpJsonSource.Navigate(document.RootElement, path));
    }

    // ---- Ordre du diaporama ----

    [Fact]
    public void Cycle_Alphabetical_ResumesAfterTheLastImage()
    {
        string[] files = [@"d\c.jpg", @"d\A.jpg", @"d\b.jpg"];

        Assert.Equal([@"d\A.jpg", @"d\b.jpg", @"d\c.jpg"], SlideshowService.BuildCycle(files, false, null, new Random(1)));
        Assert.Equal([@"d\c.jpg", @"d\A.jpg", @"d\b.jpg"], SlideshowService.BuildCycle(files, false, @"d\B.jpg", new Random(1)));
        Assert.Equal([@"d\A.jpg", @"d\b.jpg", @"d\c.jpg"], SlideshowService.BuildCycle(files, false, @"d\gone.jpg", new Random(1)));
    }

    [Fact]
    public void Cycle_Shuffle_IsAPermutation_NeverStartingWithTheLastImage()
    {
        var files = Enumerable.Range(0, 10).Select(i => $"{i}.jpg").ToList();

        for (int seed = 0; seed < 200; seed++)
        {
            var cycle = SlideshowService.BuildCycle(files, true, "3.jpg", new Random(seed));

            Assert.Equal(files.Order(), cycle.Order());
            Assert.NotEqual("3.jpg", cycle[0]);
        }
    }

    [Fact]
    public void Cycle_Shuffle_SingleImage_IsKept()
    {
        Assert.Equal(["only.jpg"], SlideshowService.BuildCycle(["only.jpg"], true, "only.jpg", new Random(1)));
    }

    // ---- Éviction du cache des composés ----

    private static CacheMaintenanceService.CachedFile File(string path, long size, int ageDays) =>
        new(path, size, DateTime.UtcNow.AddDays(-ageDays));

    [Fact]
    public void Eviction_RemovesOldestFirst_UntilUnderTheCap()
    {
        var files = new[] { File("new", 40, 1), File("old", 40, 5), File("mid", 40, 3) };

        Assert.Equal(["old", "mid"], CacheMaintenanceService.SelectEvictions(files, new HashSet<string>(), cap: 50));
        Assert.Empty(CacheMaintenanceService.SelectEvictions(files, new HashSet<string>(), cap: 120));
    }

    [Fact]
    public void Eviction_NeverRemovesAnAppliedWallpaper()
    {
        var files = new[] { File("applied-old", 100, 9), File("other", 10, 1) };

        var evicted = CacheMaintenanceService.SelectEvictions(files, new HashSet<string> { "applied-old" }, cap: 50);

        Assert.Equal(["other"], evicted); // plafond toujours dépassé, mais le fond posé reste
    }

}
