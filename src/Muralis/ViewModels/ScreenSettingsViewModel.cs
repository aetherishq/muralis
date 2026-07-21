using System.Collections.ObjectModel;
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
/// (<see cref="ImagePath"/>) ou diaporama (<see cref="FolderPath"/> ou source web +
/// <see cref="IntervalValue"/> + <see cref="Shuffle"/>), basculées par <see cref="IsSlideshow"/>.
/// Porte aussi la géométrie de son rectangle dans le sélecteur d'écrans (proportionnel à la
/// disposition réelle des moniteurs, cf. DESIGN.md) et la miniature du fond actuellement appliqué.
/// </summary>
public partial class ScreenSettingsViewModel : ObservableObject
{
    /// <summary>Libellé UI du choix « dossier local » dans la liste des sources de diaporama.</summary>
    public const string LocalFolderOption = "Dossier local";

    /// <summary>Largeur de décodage des miniatures : évite de charger les images pleine taille.</summary>
    private const int ThumbnailDecodeWidth = 480;

    /// <summary>Hauteur d'une cellule de la grille de miniatures du dossier local.</summary>
    private const double ThumbnailCellBoxHeight = 72;

    private readonly MonitorInfo? _monitor;

    /// <summary>Cible = un moniteur précis (config séparée par écran).</summary>
    public ScreenSettingsViewModel(int index, MonitorInfo monitor, ScreenConfig config, ObservableCollection<string> sourceOptions)
    {
        _monitor = monitor;
        Number = index;
        DisplayLabel = $"Écran {index}";
        ResolutionLabel = monitor.ResolutionLabel;
        SourceOptions = sourceOptions;
        // Span n'a pas de sens sur un seul écran : réservé au mode unifié.
        AvailableModes = ModesExcept(DesktopWallpaperPosition.Span);
        SetupThumbnailCell((double)monitor.Width / monitor.Height);
        LoadFrom(config);
        if (DisplayMode == DesktopWallpaperPosition.Span)
            DisplayMode = DesktopWallpaperPosition.Fill;
    }

    /// <summary>Cible = tous les écrans (mode unifié). Span autorisé, miniatures en 16:9.</summary>
    public ScreenSettingsViewModel(ScreenConfig config, ObservableCollection<string> sourceOptions)
    {
        _monitor = null;
        Number = 0;
        DisplayLabel = "Tous les écrans";
        ResolutionLabel = "Même fond sur tous les moniteurs";
        SourceOptions = sourceOptions;
        AvailableModes = Enum.GetValues<DesktopWallpaperPosition>();
        SetupThumbnailCell(16.0 / 9.0);
        LoadFrom(config);
    }

    /// <summary>Choix de source du diaporama : « Dossier local » + sources web ajoutées
    /// (collection partagée, mise à jour en direct par la page Sources).</summary>
    public ObservableCollection<string> SourceOptions { get; }

    /// <summary>Device path du moniteur ciblé (vide en mode unifié).</summary>
    public string DeviceId => _monitor?.DeviceId ?? string.Empty;

    /// <summary>Numéro affiché dans le rectangle du sélecteur d'écrans (0 en mode unifié).</summary>
    public int Number { get; }

    public string DisplayLabel { get; }

    public string ResolutionLabel { get; }

    /// <summary>Modes proposés dans le ComboBox (dépend de la cible).</summary>
    public IReadOnlyList<DesktopWallpaperPosition> AvailableModes { get; }

    /// <summary>Position/taille du rectangle dans le sélecteur d'écrans, en pixels du canvas
    /// (géométrie réelle des moniteurs mise à l'échelle par <see cref="SettingsViewModel.Load"/>).</summary>
    public double SelectorLeft { get; private set; }

    public double SelectorTop { get; private set; }

    public double SelectorWidth { get; private set; }

    public double SelectorHeight { get; private set; }

    /// <summary>Dimensions d'une cellule de la grille de miniatures, au ratio de l'écran.</summary>
    public double ThumbnailCellWidth { get; private set; }

    public double ThumbnailCellHeight { get; private set; }

    /// <summary>Miniature du fond actuellement appliqué sur ce moniteur (affichée dans le sélecteur).</summary>
    [ObservableProperty]
    private ImageSource? currentWallpaper;

    /// <summary>Fixe la géométrie du rectangle du sélecteur (appelé avant insertion dans la liste bindée).</summary>
    public void SetSelectorGeometry(double left, double top, double width, double height)
    {
        SelectorLeft = left;
        SelectorTop = top;
        SelectorWidth = width;
        SelectorHeight = height;
    }

    /// <summary>Recharge la miniature du fond appliqué depuis son chemin (null : pas d'aperçu).</summary>
    public void SetCurrentWallpaper(string? path) => CurrentWallpaper = LoadThumbnail(path);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFixed), nameof(ShowSlideshow), nameof(ShowSlideshowPreview))]
    private bool isSlideshow;

    public bool ShowFixed => !IsSlideshow;

    public bool ShowSlideshow => IsSlideshow;

    [ObservableProperty]
    private string? imagePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalSource), nameof(ShowSlideshowPreview))]
    private string selectedSource = LocalFolderOption;

    /// <summary>Vrai si le diaporama puise dans un dossier local (sinon : source web).</summary>
    public bool IsLocalSource => SelectedSource == LocalFolderOption;

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
    [NotifyPropertyChangedFor(nameof(ShowSlideshowPreview))]
    private IReadOnlyList<ImageSource> previewThumbnails = [];

    public bool ShowSlideshowPreview => ShowSlideshow && IsLocalSource && PreviewThumbnails.Count > 0;

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
            SourceType = !IsSlideshow ? "LocalFile"
                : IsLocalSource ? SlideshowService.LocalFolderSourceType
                : SelectedSource,
            SourcePath = (IsSlideshow ? (IsLocalSource ? FolderPath : null) : ImagePath) ?? string.Empty,
            SlideshowInterval = interval,
            Shuffle = Shuffle,
            DisplayMode = DisplayMode,
        };
    }

    private void LoadFrom(ScreenConfig config)
    {
        IsSlideshow = config.Mode == WallpaperMode.Slideshow;
        if (IsSlideshow)
        {
            bool isWeb = !string.IsNullOrEmpty(config.SourceType)
                && config.SourceType != SlideshowService.LocalFolderSourceType
                && SourceOptions.Contains(config.SourceType);
            SelectedSource = isWeb ? config.SourceType : LocalFolderOption;
            if (!isWeb)
                FolderPath = string.IsNullOrEmpty(config.SourcePath) ? null : config.SourcePath;
        }
        else
        {
            ImagePath = string.IsNullOrEmpty(config.SourcePath) ? null : config.SourcePath;
        }
        var interval = config.SlideshowInterval > TimeSpan.Zero ? config.SlideshowInterval : TimeSpan.FromMinutes(30);
        // Affiche en minutes quand l'intervalle tombe rond, en secondes sinon.
        bool wholeMinutes = interval >= TimeSpan.FromMinutes(1) && interval.TotalSeconds % 60 == 0;
        IntervalUnit = wholeMinutes ? UnitMinutes : UnitSeconds;
        IntervalValue = wholeMinutes ? interval.TotalMinutes : interval.TotalSeconds;
        Shuffle = config.Shuffle;
        DisplayMode = config.DisplayMode;
    }

    private void SetupThumbnailCell(double aspectRatio)
    {
        ThumbnailCellHeight = ThumbnailCellBoxHeight;
        ThumbnailCellWidth = Math.Round(ThumbnailCellBoxHeight * aspectRatio);
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
            // 2 rangées de 4 au maximum (grille de miniatures, cf. DESIGN.md).
            return ImageFiles.Enumerate(folder)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Take(8)
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
