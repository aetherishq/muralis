using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Muralis.Models;
using Muralis.Resources;
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
    /// <summary>Mode d'affichage présentable dans un ComboBox (libellé localisé).</summary>
    public sealed record DisplayModeOption(DesktopWallpaperPosition Value, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Libellé UI du choix « dossier local » dans la liste des sources de diaporama.
    /// Localisé — jamais persisté (la config stocke <see cref="SlideshowService.LocalFolderSourceType"/>) ;
    /// les comparaisons restent cohérentes car tout le graphe de ViewModels est reconstruit
    /// à chaque changement de langue.
    /// </summary>
    public static string LocalFolderOption => Strings.Screen_LocalFolderOption;

    /// <summary>Libellé UI du choix « fichier local » dans la card Image (mode fixe).</summary>
    public static string LocalFileOption => Strings.Screen_LocalFileOption;

    /// <summary>Largeur de décodage des miniatures : évite de charger les images pleine taille.</summary>
    private const int ThumbnailDecodeWidth = 480;

    /// <summary>Hauteur d'une cellule de la grille de miniatures du dossier local.</summary>
    private const double ThumbnailCellBoxHeight = 72;

    private readonly MonitorInfo? _monitor;

    /// <summary>Cible = un moniteur précis (config séparée par écran).</summary>
    public ScreenSettingsViewModel(
        int index,
        MonitorInfo monitor,
        ScreenConfig config,
        ObservableCollection<SourceOption> sourceOptions,
        ObservableCollection<SourceOption> fixedSourceOptions)
    {
        _monitor = monitor;
        Number = index;
        DisplayLabel = string.Format(Strings.Screen_LabelFormat, index);
        ResolutionLabel = monitor.ResolutionLabel;
        SourceOptions = sourceOptions;
        FixedSourceOptions = fixedSourceOptions;
        SourceOptions.CollectionChanged += (_, _) => EnsureSelectedSourceValid();
        FixedSourceOptions.CollectionChanged += (_, _) => EnsureSelectedFixedSourceValid();
        // Span n'a pas de sens sur un seul écran : réservé au mode unifié.
        AvailableModes = BuildModes(includeSpan: false);
        SetupThumbnailCell((double)monitor.Width / monitor.Height);
        LoadFrom(config);
    }

    /// <summary>Cible = tous les écrans (mode unifié). Span autorisé, miniatures en 16:9.</summary>
    public ScreenSettingsViewModel(
        ScreenConfig config,
        ObservableCollection<SourceOption> sourceOptions,
        ObservableCollection<SourceOption> fixedSourceOptions)
    {
        _monitor = null;
        Number = 0;
        DisplayLabel = Strings.Screen_AllScreens;
        ResolutionLabel = Strings.Screen_AllScreensDesc;
        SourceOptions = sourceOptions;
        FixedSourceOptions = fixedSourceOptions;
        SourceOptions.CollectionChanged += (_, _) => EnsureSelectedSourceValid();
        FixedSourceOptions.CollectionChanged += (_, _) => EnsureSelectedFixedSourceValid();
        AvailableModes = BuildModes(includeSpan: true);
        SetupThumbnailCell(16.0 / 9.0);
        LoadFrom(config);
    }

    /// <summary>Choix de source du diaporama : « Dossier local » + sources aléatoires
    /// (collection partagée, mise à jour en direct par la page Sources).</summary>
    public ObservableCollection<SourceOption> SourceOptions { get; }

    /// <summary>Choix de la card Image : « Fichier local » + sources quotidiennes
    /// (collection partagée, mise à jour en direct par la page Sources).</summary>
    public ObservableCollection<SourceOption> FixedSourceOptions { get; }

    /// <summary>Sentinelles toujours présentes en tête de leur liste (repli de sélection).</summary>
    private SourceOption LocalFolderFallback => SourceOptions.First(o => o.Id == SlideshowService.LocalFolderSourceType);

    private SourceOption LocalFileFallback => FixedSourceOptions.First(o => o.Id == ScreenConfig.LocalFileSourceType);

    /// <summary>Device path du moniteur ciblé (vide en mode unifié).</summary>
    public string DeviceId => _monitor?.DeviceId ?? string.Empty;

    /// <summary>Numéro affiché dans le rectangle du sélecteur d'écrans (0 en mode unifié).</summary>
    public int Number { get; }

    public string DisplayLabel { get; }

    public string ResolutionLabel { get; }

    /// <summary>Modes proposés dans le ComboBox (dépend de la cible), libellés localisés.</summary>
    public IReadOnlyList<DisplayModeOption> AvailableModes { get; }

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

    /// <summary>Vrai quand cet écran affiche une image web enregistrable : le bouton
    /// télécharger apparaît au survol de sa miniature. Alimenté par
    /// <see cref="SettingsViewModel"/> au fil des poses de fonds.</summary>
    [ObservableProperty]
    private bool canSaveCurrent;

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
    [NotifyPropertyChangedFor(nameof(FixedSourceDescription))]
    private string? imagePath;

    /// <summary>Source de la card Image : « Fichier local » ou une source web quotidienne.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalFile), nameof(FixedSourceDescription))]
    private SourceOption selectedFixedSource = null!; // assigné par LoadFrom (appelé des deux ctors)

    /// <summary>Vrai si l'image fixe vient d'un fichier choisi par l'utilisateur.</summary>
    public bool IsLocalFile => SelectedFixedSource is null || SelectedFixedSource.Id == ScreenConfig.LocalFileSourceType;

    /// <summary>Description de la card Image : chemin du fichier, ou mention de mise à jour
    /// quotidienne pour une source web.</summary>
    public string FixedSourceDescription => IsLocalFile
        ? (string.IsNullOrEmpty(ImagePath) ? Strings.Wallpapers_NoImageSelected : ImagePath)
        : Strings.Wallpapers_DailyAutoDesc;

    /// <summary>Même filet que pour le diaporama : le Selector pousse null quand l'option
    /// sélectionnée disparaît.</summary>
    partial void OnSelectedFixedSourceChanged(SourceOption? oldValue, SourceOption newValue)
    {
        if (newValue is null)
            SelectedFixedSource = LocalFileFallback;
    }

    /// <summary>Une source quotidienne retirée ne doit pas laisser la card Image sur une
    /// option fantôme : retombe sur « Fichier local ».</summary>
    private void EnsureSelectedFixedSourceValid()
    {
        if (SelectedFixedSource is null || !FixedSourceOptions.Contains(SelectedFixedSource))
            SelectedFixedSource = LocalFileFallback;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalSource), nameof(ShowSlideshowPreview))]
    private SourceOption selectedSource = null!; // assigné par LoadFrom (appelé des deux ctors)

    /// <summary>Vrai si le diaporama puise dans un dossier local (sinon : source web).</summary>
    public bool IsLocalSource => SelectedSource is null || SelectedSource.Id == SlideshowService.LocalFolderSourceType;

    /// <summary>
    /// Le Selector WPF pousse <c>null</c> quand l'option sélectionnée est retirée de
    /// <see cref="SourceOptions"/> : retomber sur « Dossier local » plutôt que de laisser
    /// l'éditeur dans un état invalide (cards masquées, ComboBox vide). Ré-entrance sûre :
    /// le setter généré ne re-notifie que sur changement réel.
    /// </summary>
    partial void OnSelectedSourceChanged(SourceOption? oldValue, SourceOption newValue)
    {
        if (newValue is null)
            SelectedSource = LocalFolderFallback;
    }

    /// <summary>Une source retirée du catalogue ne doit pas laisser un écran pointer sur une
    /// option fantôme : retombe sur « Dossier local » (issue #2).</summary>
    private void EnsureSelectedSourceValid()
    {
        if (SelectedSource is null || !SourceOptions.Contains(SelectedSource))
            SelectedSource = LocalFolderFallback;
    }

    [ObservableProperty]
    private string? folderPath;

    /// <summary>Unités d'intervalle : localisées, jamais persistées (la config stocke un TimeSpan).</summary>
    public static string UnitMinutes => Strings.Screen_UnitMinutes;

    public static string UnitSeconds => Strings.Screen_UnitSeconds;

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
    private DisplayModeOption selectedMode = null!; // assigné par LoadFrom (appelé des deux ctors)

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
            Title = string.Format(Strings.Dialog_ChooseImageFormat, DisplayLabel),
            Filter = $"{Strings.Dialog_ImagesFilter}|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|{Strings.Dialog_AllFilesFilter}|*.*",
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
            Title = string.Format(Strings.Dialog_ChooseFolderFormat, DisplayLabel),
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
            // L'Id d'une option est directement la valeur persistée (sentinelle ou Id de source).
            SourceType = IsSlideshow ? SelectedSource.Id : SelectedFixedSource.Id,
            SourcePath = (IsSlideshow
                ? (IsLocalSource ? FolderPath : null)
                : (IsLocalFile ? ImagePath : null)) ?? string.Empty,
            SlideshowInterval = interval,
            Shuffle = Shuffle,
            DisplayMode = SelectedMode.Value,
        };
    }

    private void LoadFrom(ScreenConfig config)
    {
        IsSlideshow = config.Mode == WallpaperMode.Slideshow;
        // Une référence inconnue (source supprimée) retombe sur la sentinelle locale.
        SelectedSource = SourceOptions.FirstOrDefault(o => o.Id == config.SourceType) ?? LocalFolderFallback;
        SelectedFixedSource = FixedSourceOptions.FirstOrDefault(o => o.Id == config.SourceType) ?? LocalFileFallback;
        if (IsSlideshow)
        {
            if (IsLocalSource)
                FolderPath = string.IsNullOrEmpty(config.SourcePath) ? null : config.SourcePath;
        }
        else if (IsLocalFile)
        {
            ImagePath = string.IsNullOrEmpty(config.SourcePath) ? null : config.SourcePath;
        }
        var interval = config.SlideshowInterval > TimeSpan.Zero ? config.SlideshowInterval : TimeSpan.FromMinutes(30);
        // Affiche en minutes quand l'intervalle tombe rond, en secondes sinon.
        bool wholeMinutes = interval >= TimeSpan.FromMinutes(1) && interval.TotalSeconds % 60 == 0;
        IntervalUnit = wholeMinutes ? UnitMinutes : UnitSeconds;
        IntervalValue = wholeMinutes ? interval.TotalMinutes : interval.TotalSeconds;
        Shuffle = config.Shuffle;
        // Fill (premier de la liste) en repli — couvre aussi un Span persisté sur une
        // cible mono-écran, où ce mode n'est pas proposé.
        SelectedMode = AvailableModes.FirstOrDefault(m => m.Value == config.DisplayMode) ?? AvailableModes[0];
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

    /// <summary>Fill en tête (mode de repli), Span en dernier et réservé au mode unifié.</summary>
    private static IReadOnlyList<DisplayModeOption> BuildModes(bool includeSpan)
    {
        var modes = new List<DisplayModeOption>
        {
            new(DesktopWallpaperPosition.Fill, Strings.DisplayMode_Fill),
            new(DesktopWallpaperPosition.Fit, Strings.DisplayMode_Fit),
            new(DesktopWallpaperPosition.Stretch, Strings.DisplayMode_Stretch),
            new(DesktopWallpaperPosition.Center, Strings.DisplayMode_Center),
            new(DesktopWallpaperPosition.Tile, Strings.DisplayMode_Tile),
        };
        if (includeSpan)
            modes.Add(new(DesktopWallpaperPosition.Span, Strings.DisplayMode_Span));
        return modes;
    }
}
