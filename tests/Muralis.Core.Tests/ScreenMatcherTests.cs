using Muralis.Models;
using Muralis.Services;

namespace Muralis.Core.Tests;

/// <summary>Issue #42 : écrans branchés/débranchés pendant que l'app tourne.</summary>
public class ScreenMatcherTests
{
    private const string Guid = "{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private static string Path(string hardware, string uid) => $@"\?\DISPLAY#{hardware}#5&2b975683&0&UID{uid}#{Guid}";

    private static MonitorInfo Monitor(string id, int width = 1920, int height = 1080) =>
        new(id, new PixelRect { Left = 0, Top = 0, Right = width, Bottom = height });

    private static ScreenConfig Slideshow(string id) =>
        new() { DeviceId = id, Mode = WallpaperMode.Slideshow, SourceType = "LocalFolder", SourcePath = @"C:\Pics", Shuffle = false };

    [Fact]
    public void HardwareId_IsTheEdidModel()
    {
        Assert.Equal("DELA0B5", ScreenMatcher.HardwareId(Path("DELA0B5", "4352")));
        Assert.Null(ScreenMatcher.HardwareId(""));
        Assert.Null(ScreenMatcher.HardwareId("garbage"));
    }

    [Fact]
    public void SameScreenOnAnotherDockPort_ReusesItsConfiguration()
    {
        var config = new AppConfig { Screens = [Slideshow(Path("DELA0B5", "1"))] };
        var moved = Monitor(Path("DELA0B5", "2"));

        var added = ScreenMatcher.CompleteForRuntime(config, [moved], _ => "default.jpg");

        Assert.Equal([moved.DeviceId], added);
        var reused = config.FindScreen(moved.DeviceId)!;
        Assert.Equal(WallpaperMode.Slideshow, reused.Mode);
        Assert.Equal(@"C:\Pics", reused.SourcePath);
        Assert.False(reused.Shuffle);
        Assert.NotNull(config.FindScreen(Path("DELA0B5", "1"))); // l'original n'est pas touché
    }

    [Fact]
    public void TwoIdenticalScreens_AreAmbiguous_AndFallBack()
    {
        var config = new AppConfig { Screens = [Slideshow(Path("DELA0B5", "1")), Slideshow(Path("DELA0B5", "2"))] };
        var monitors = new[] { Monitor(Path("DELA0B5", "3")) };

        ScreenMatcher.CompleteForRuntime(config, monitors, _ => "default.jpg");

        var screen = config.FindScreen(monitors[0].DeviceId)!;
        Assert.Equal(WallpaperMode.Fixed, screen.Mode);
        Assert.Equal("default.jpg", screen.SourcePath);
    }

    [Fact]
    public void ConfigOfAConnectedScreen_IsNotCandidate()
    {
        // Deuxième écran du même modèle branché : la config du premier (toujours là) n'est pas reprise.
        var first = Path("DELA0B5", "1");
        var config = new AppConfig { Screens = [Slideshow(first)] };

        Assert.Null(ScreenMatcher.FindByHardware(config, Monitor(Path("DELA0B5", "2")), [Monitor(first), Monitor(Path("DELA0B5", "2"))]));
    }

    [Fact]
    public void UnknownScreen_WithoutFallback_IsLeftAlone()
    {
        var config = new AppConfig();

        Assert.Empty(ScreenMatcher.CompleteForRuntime(config, [Monitor(Path("ACR06F5", "1"))], _ => null));
        Assert.Empty(config.Screens);
    }

    [Fact]
    public void UnifiedMode_IsNotCompleted()
    {
        var config = new AppConfig { Unified = true };

        Assert.Empty(ScreenMatcher.CompleteForRuntime(config, [Monitor(Path("ACR06F5", "1"))], _ => "default.jpg"));
    }

    [Fact]
    public void SameLayout_IgnoresOrder_ButNotResolution()
    {
        var a = Monitor("A");
        var b = Monitor("B", 2560, 1440);

        Assert.True(ScreenMatcher.SameLayout([a, b], [b, a]));
        Assert.False(ScreenMatcher.SameLayout([a, b], [a, Monitor("B", 3840, 2160)]));
        Assert.False(ScreenMatcher.SameLayout([a], [a, b]));
    }

    [Fact]
    public void DefaultWallpaper_PicksTheClosestVariant()
    {
        string[] variants = [@"x\img0_1920x1200.jpg", @"x\img0_3840x2160.jpg", @"x\img0_1024x768.jpg", @"x\other.jpg"];

        Assert.Equal(@"x\img0_3840x2160.jpg", ScreenMatcher.PickDefaultWallpaper(3840, 2160, variants));
        Assert.Equal(@"x\img0_1920x1200.jpg", ScreenMatcher.PickDefaultWallpaper(1920, 1080, variants));
        Assert.Null(ScreenMatcher.PickDefaultWallpaper(1920, 1080, []));
    }

}
