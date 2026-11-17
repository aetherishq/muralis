using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Muralis.Models;
using Muralis.Services;
using Muralis.ViewModels;

namespace Muralis.Tests;

/// <summary>Issue #49 : aperçus décodés hors UI thread, sans résultat périmé, et énumération
/// COM des moniteurs seulement quand le nettoyage du cache est dû.</summary>
public sealed class ThumbnailLoadingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "muralis-tests-" + Guid.NewGuid().ToString("N"));

    public ThumbnailLoadingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string Folder(string name, int images)
    {
        string folder = Path.Combine(_dir, name);
        Directory.CreateDirectory(folder);
        for (int i = 0; i < images; i++)
        {
            var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Gray8, null, new byte[16], 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(folder, $"{i}.png"));
            encoder.Save(stream);
        }
        return folder;
    }

    private ScreenSettingsViewModel NewEditor()
    {
        var sources = new SourcesViewModel(new ConfigService(Path.Combine(_dir, "data")));
        return new ScreenSettingsViewModel(new ScreenConfig(), sources.EditorRandomOptions, sources.EditorDailyOptions);
    }

    [Fact]
    public async Task FolderPreview_KeepsOnlyTheLastSelectedFolder()
    {
        string first = Folder("first", images: 3);
        string second = Folder("second", images: 1);
        var vm = NewEditor();

        vm.FolderPath = first;
        var firstLoading = vm.PreviewLoading;
        vm.FolderPath = second;
        await Task.WhenAll(firstLoading, vm.PreviewLoading);

        Assert.Single(vm.PreviewThumbnails);
        Assert.True(vm.PreviewThumbnails[0].IsFrozen);
    }

    [Fact]
    public async Task CurrentWallpaper_IsDecodedAndFrozen()
    {
        string image = Path.Combine(Folder("current", images: 1), "0.png");
        var vm = NewEditor();

        await vm.SetCurrentWallpaper(image);

        Assert.NotNull(vm.CurrentWallpaper);
        Assert.True(vm.CurrentWallpaper!.IsFrozen);
    }

    [Fact]
    public void ComposedPrune_EnumeratesMonitors_OnlyWhenDue()
    {
        var config = new ConfigService(_dir);
        var maintenance = new CacheMaintenanceService(config, new WallpaperService(new WallpaperComposer(config), new WindowsDesktopBackend()));
        int calls = 0;

        maintenance.PruneComposedIfDue(() => { calls++; return []; });
        maintenance.PruneComposedIfDue(() => { calls++; return []; });

        Assert.Equal(1, calls);
    }
}
