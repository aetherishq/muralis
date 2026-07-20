using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Muralis.Models;
using Muralis.Services;

namespace Muralis.ViewModels;

/// <summary>
/// Réglages d'une cible de fond d'écran : soit un moniteur précis (mode config par écran),
/// soit l'ensemble des écrans (mode unifié). Deux alimentations possibles : image fixe
/// (<see cref="ImagePath"/>) ou diaporama d'un dossier local (<see cref="FolderPath"/> +
/// <see cref="IntervalMinutes"/> + <see cref="Shuffle"/>), basculées par <see cref="IsSlideshow"/>.
/// Un aperçu au ratio de l'écran est affiché : l'image choisie en fixe, une mosaïque de
/// 4 miniatures du dossier en diaporama.
/// </summary>
public partial class ScreenSettingsViewModel : ObservableObject
{
    /// <summary>Largeur de décodage des miniatures : évite de charger les images pleine taille.</summary>
    private const int ThumbnailDecodeWidth = 480;

    /// <summary>Hauteur commune de tous les aperçus : garde les cartes alignées entre elles.</summary>
    private const double PreviewBoxHeight = 150;

    private readonly MonitorInfo? _monitor;

    /// <summary>Cible = un moniteur précis (config séparée par écran).</summary>
    public ScreenSettingsViewModel(int index, MonitorInfo monitor, ScreenConfig config)
    {
        _monitor = monitor;
        DisplayLabel = $"Écran {index}";
        ResolutionLabel = monitor.ResolutionLabel;
        // Span n'a pas de sens sur un seul écran : réservé au mode unifié.
        AvailableModes = ModesExcept(DesktopWallpaperPosition.Span);
        SetupPreview((double)monitor.Width / monitor.Height);
        LoadFrom(config);
        if (DisplayMode == DesktopWallpaperPosition.Span)
            DisplayMode = DesktopWallpaperPosition.Fill;
    }

    /// <summary>Cible = tous les écrans (mode unifié). Span autorisé, aperçu en 16:9.</summary>
    public ScreenSettingsViewModel(ScreenConfig config)
    {
        _monitor = null;
        DisplayLabel = "Tous les écrans";
        ResolutionLabel = "Même fond sur tous les moniteurs";
        AvailableModes = Enum.GetValues<DesktopWallpaperPosition>();
        SetupPreview(16.0 / 9.0);
        LoadFrom(config);
    }

    /// <summary>Device path du moniteur ciblé (vide en mode unifié).</summary>
    public string DeviceId => _monitor?.DeviceId ?? string.Empty;

    public string DisplayLabel { get; }

    public string ResolutionLabel { get; }

    /// <summary>Modes proposés dans le ComboBox (dépend de la cible).</summary>
    public IReadOnlyList<DesktopWallpaperPosition> AvailableModes { get; }

    /// <summary>Dimensions de l'aperçu fixe, au ratio de l'écran ciblé (hauteur commune à toutes les cartes).</summary>
    public double PreviewWidth { get; private set; }

    public double PreviewHeight { get; private set; }

    /// <summary>
    /// Largeur de la mosaïque du diaporama. Écran paysage : même boîte que l'aperçu fixe (grille 2×2).
    /// Écran portrait : 4 cases au ratio de l'écran alignées en une rangée, pour que la carte garde
    /// un encombrement comparable aux écrans paysage.
    /// </summary>
    public double SlideshowPreviewWidth { get; private set; }

    public int MosaicColumns { get; private set; }

    public int MosaicRows { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFixed), nameof(ShowSlideshow), nameof(ShowFixedPreview), nameof(ShowSlideshowPreview))]
    private bool isSlideshow;

    public bool ShowFixed => !IsSlideshow;

    public bool ShowSlideshow => IsSlideshow;

    [ObservableProperty]
    private string? imagePath;

    [ObservableProperty]
    private string? folderPath;

    public const string UnitMinutes = "minutes";
    public const string UnitSeconds = "secondes";

    /// <summary>Intervalle minimal du diaporama (une composition d'image a un coût).</summary>
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(5);

    public IReadOnlyList<string> IntervalUnits { get; } = [UnitMinutes, UnitSeconds];

    [ObservableProperty]
    private double intervalValue = 30;

    [ObservableProperty]
    private string intervalUnit = UnitMinutes;

    /// <summary>À la bascule d'unité, convertit la valeur affichée (30 min ↔ 1800 s).</summary>
    partial void OnIntervalUnitChanged(string? oldValue, string newValue)
    {
        if (oldValue is null || oldValue == newValue)
            return;

        IntervalValue = newValue == UnitSeconds
            ? Math.Round(IntervalValue * 60)
            : Math.Round(IntervalValue / 60, 2);
    }

    [ObservableProperty]
    private bool shuffle = true;

    [ObservableProperty]
    private DesktopWallpaperPosition displayMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFixedPreview))]
    private ImageSource? previewImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSlideshowPreview))]
    private IReadOnlyList<ImageSource> previewThumbnails = [];

    public bool ShowFixedPreview => ShowFixed && PreviewImage is not null;

    public bool ShowSlideshowPreview => ShowSlideshow && PreviewThumbnails.Count > 0;

    partial void OnImagePathChanged(string? value) => PreviewImage = LoadThumbnail(value);

    partial void OnFolderPathChanged(string? value) => PreviewThumbnails = LoadFolderThumbnails(value);

    [RelayCommand]
    private void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Choisir une image — {DisplayLabel}",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|Tous les fichiers|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == true)
            ImagePath = dialog.FileName;
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = $"Choisir un dossier d'images — {DisplayLabel}",
        };

        if (dialog.ShowDialog() == true)
            FolderPath = dialog.FolderName;
    }

    /// <summary>Projette l'état courant vers un <see cref="ScreenConfig"/> persistable.</summary>
    public ScreenConfig ToConfig()
    {
        var interval = IntervalUnit == UnitSeconds
            ? TimeSpan.FromSeconds(IntervalValue)
            : TimeSpan.FromMinutes(IntervalValue);
        if (interval < MinInterval)
            interval = MinInterval;

        return new ScreenConfig
        {
            DeviceId = DeviceId,
            Mode = IsSlideshow ? WallpaperMode.Slideshow : WallpaperMode.Fixed,
            SourceType = IsSlideshow ? "LocalFolder" : "LocalFile",
            SourcePath = (IsSlideshow ? FolderPath : ImagePath) ?? string.Empty,
            SlideshowInterval = interval,
            Shuffle = Shuffle,
            DisplayMode = DisplayMode,
        };
    }

    private void LoadFrom(ScreenConfig config)
    {
        IsSlideshow = config.Mode == WallpaperMode.Slideshow;
        if (IsSlideshow)
            FolderPath = string.IsNullOrEmpty(config.SourcePath) ? null : config.SourcePath;
        else
            ImagePath = string.IsNullOrEmpty(config.SourcePath) ? null : config.SourcePath;
        var interval = config.SlideshowInterval > TimeSpan.Zero ? config.SlideshowInterval : TimeSpan.FromMinutes(30);
        // Affiche en minutes quand l'intervalle tombe rond, en secondes sinon.
        bool wholeMinutes = interval >= TimeSpan.FromMinutes(1) && interval.TotalSeconds % 60 == 0;
        IntervalUnit = wholeMinutes ? UnitMinutes : UnitSeconds;
        IntervalValue = wholeMinutes ? interval.TotalMinutes : interval.TotalSeconds;
        Shuffle = config.Shuffle;
        DisplayMode = config.DisplayMode;
    }

    private void SetupPreview(double aspectRatio)
    {
        PreviewHeight = PreviewBoxHeight;
        PreviewWidth = PreviewBoxHeight * aspectRatio;

        bool portrait = aspectRatio < 1;
        MosaicColumns = portrait ? 4 : 2;
        MosaicRows = portrait ? 1 : 2;
        SlideshowPreviewWidth = portrait ? 4 * PreviewWidth : PreviewWidth;
    }

    private static ImageSource? LoadThumbnail(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = ThumbnailDecodeWidth;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null; // Image illisible : pas d'aperçu, sans casser l'UI.
        }
    }

    private static IReadOnlyList<ImageSource> LoadFolderThumbnails(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return [];

        try
        {
            return ImageFiles.Enumerate(folder)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .Select(LoadThumbnail)
                .OfType<ImageSource>()
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static IReadOnlyList<DesktopWallpaperPosition> ModesExcept(DesktopWallpaperPosition excluded) =>
        Enum.GetValues<DesktopWallpaperPosition>().Where(m => m != excluded).ToArray();
}
