using System.Collections.ObjectModel;
using System.Windows.Threading;
using Muralis.Models;
using Muralis.Services;
using Muralis.ViewModels;

namespace Muralis.Tests;

/// <summary>Bornes de l'intervalle de diaporama (issue #44 : un intervalle au-delà de
/// ~24,8 jours faisait lever DispatcherTimer et planter l'app à chaque démarrage).</summary>
public class SlideshowIntervalTests
{
    [Fact]
    public void MaxInterval_IsAcceptedByDispatcherTimer()
    {
        // Le plafond doit rester sous la limite Int32.MaxValue ms de DispatcherTimer.
        var timer = new DispatcherTimer { Interval = ScreenConfig.MaxSlideshowInterval };
        Assert.Equal(ScreenConfig.MaxSlideshowInterval, timer.Interval);
    }

    [Theory]
    [InlineData(40000, false)]      // 40 000 min ≈ 27,8 jours : le cas du bug
    [InlineData(86400, false)]      // ancien Maximum de la NumberBox, en minutes (60 jours)
    [InlineData(1e300, false)]      // TimeSpan.FromMinutes lèverait OverflowException
    [InlineData(double.PositiveInfinity, true)]
    public void ToInterval_ClampsToMax(double value, bool inSeconds)
    {
        Assert.Equal(ScreenConfig.MaxSlideshowInterval, ScreenSettingsViewModel.ToInterval(value, inSeconds));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-10, true)]
    [InlineData(double.NaN, false)] // TimeSpan.FromMinutes(NaN) lèverait ArgumentException
    [InlineData(2, true)]
    public void ToInterval_ClampsToMin(double value, bool inSeconds)
    {
        Assert.Equal(ScreenConfig.MinSlideshowInterval, ScreenSettingsViewModel.ToInterval(value, inSeconds));
    }

    [Theory]
    [InlineData(30, false, 30 * 60)]
    [InlineData(90, true, 90)]
    [InlineData(34560, false, 24 * 24 * 3600)] // pile le plafond, en minutes
    public void ToInterval_KeepsValuesInRange(double value, bool inSeconds, double expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), ScreenSettingsViewModel.ToInterval(value, inSeconds));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EffectiveInterval_ClampsHandEditedConfig(bool isWeb)
    {
        // Config éditée à la main / persistée avant le correctif : 60 jours.
        Assert.Equal(ScreenConfig.MaxSlideshowInterval, SlideshowService.EffectiveInterval(TimeSpan.FromDays(60), isWeb));
    }

    [Fact]
    public void EffectiveInterval_KeepsFloors()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), SlideshowService.EffectiveInterval(TimeSpan.FromSeconds(1), isWeb: false));
        Assert.Equal(TimeSpan.FromMinutes(1), SlideshowService.EffectiveInterval(TimeSpan.FromSeconds(10), isWeb: true));
        Assert.Equal(TimeSpan.FromHours(2), SlideshowService.EffectiveInterval(TimeSpan.FromHours(2), isWeb: true));
    }

    [Fact]
    public void ViewModel_LoadsOversizedConfigClampedAndPersistsItClamped()
    {
        var vm = CreateViewModel(new ScreenConfig
        {
            DeviceId = "screen",
            Mode = WallpaperMode.Slideshow,
            SourceType = SlideshowService.LocalFolderSourceType,
            SlideshowInterval = TimeSpan.FromDays(60),
        });

        Assert.Equal(ScreenConfig.MaxSlideshowInterval, vm.ToConfig().SlideshowInterval);
        Assert.True(vm.IntervalValue <= vm.IntervalMaximum);
    }

    [Fact]
    public void ViewModel_UserTypingHugeValue_IsPersistedClamped()
    {
        var vm = CreateViewModel(new ScreenConfig { DeviceId = "screen", Mode = WallpaperMode.Slideshow });
        vm.SelectedIntervalUnit = ScreenSettingsViewModel.UnitMinutes;
        vm.IntervalValue = 40000;

        Assert.Equal(ScreenConfig.MaxSlideshowInterval, vm.ToConfig().SlideshowInterval);
    }

    [Fact]
    public void ViewModel_IntervalMaximum_FollowsUnit()
    {
        var vm = CreateViewModel(new ScreenConfig { DeviceId = "screen" });

        vm.SelectedIntervalUnit = ScreenSettingsViewModel.UnitMinutes;
        Assert.Equal(ScreenConfig.MaxSlideshowInterval.TotalMinutes, vm.IntervalMaximum);

        vm.SelectedIntervalUnit = ScreenSettingsViewModel.UnitSeconds;
        Assert.Equal(ScreenConfig.MaxSlideshowInterval.TotalSeconds, vm.IntervalMaximum);
    }

    private static ScreenSettingsViewModel CreateViewModel(ScreenConfig config)
    {
        var random = new ObservableCollection<SourceOption>
        {
            new(SlideshowService.LocalFolderSourceType, ScreenSettingsViewModel.LocalFolderOption),
        };
        var daily = new ObservableCollection<SourceOption>
        {
            new(ScreenConfig.LocalFileSourceType, ScreenSettingsViewModel.LocalFileOption),
        };
        var monitor = new MonitorInfo("screen", new PixelRect(0, 0, 1920, 1080));
        return new ScreenSettingsViewModel(1, monitor, config, random, daily);
    }
}
