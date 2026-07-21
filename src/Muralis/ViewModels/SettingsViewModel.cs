using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muralis.Models;
using Muralis.Services;

namespace Muralis.ViewModels;

/// <summary>
/// ViewModel de la fenêtre de paramètres. Deux modes exclusifs pilotés par <see cref="UnifiedMode"/> :
/// un même fond pour tous les écrans (<see cref="UnifiedScreen"/>), ou une config indépendante par
/// écran (<see cref="Screens"/>).
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly ScreenService _screenService;
    private readonly WallpaperService _wallpaperService;
    private readonly SlideshowService _slideshowService;

    private IReadOnlyList<MonitorInfo> _monitors = [];

    public SettingsViewModel(
        ConfigService configService,
        ScreenService screenService,
        WallpaperService wallpaperService,
        SlideshowService slideshowService,
        AppSettingsViewModel appSettings,
        SourcesViewModel sources)
    {
        _configService = configService;
        _screenService = screenService;
        _wallpaperService = wallpaperService;
        _slideshowService = slideshowService;
        AppSettings = appSettings;
        Sources = sources;
        Load();

        // Miniatures du sélecteur resynchronisées à chaque pose réelle d'un diaporama
        // (la première image d'une source web arrive après le Restart, en asynchrone).
        _slideshowService.WallpaperApplied += RefreshCurrentWallpapers;

        // Feedback visible quand une source web ne fournit pas d'image (URL/chemin JSON
        // erronés, API en panne…) — sinon l'échec serait silencieux.
        _slideshowService.WebFetchFailed += name =>
            StatusMessage = $"Source « {name} » : impossible de récupérer une image — fond actuel conservé.";
    }

    /// <summary>Paramètres d'application (page dédiée).</summary>
    public AppSettingsViewModel AppSettings { get; }

    /// <summary>Gestion des sources web (page dédiée).</summary>
    public SourcesViewModel Sources { get; }

    /// <summary>Boîte maximale du sélecteur d'écrans (la disposition réelle y est mise à l'échelle).</summary>
    private const double SelectorBoxWidth = 480;
    private const double SelectorBoxHeight = 150;

    /// <summary>Configs par écran (mode séparé).</summary>
    public ObservableCollection<ScreenSettingsViewModel> Screens { get; } = [];

    /// <summary>Écran mis en évidence dans le sélecteur et édité en dessous (mode séparé).</summary>
    [ObservableProperty]
    private ScreenSettingsViewModel? selectedScreen;

    /// <summary>Dimensions du canvas du sélecteur (union des moniteurs mise à l'échelle).</summary>
    [ObservableProperty]
    private double selectorCanvasWidth;

    [ObservableProperty]
    private double selectorCanvasHeight;

    /// <summary>Config commune (mode unifié).</summary>
    [ObservableProperty]
    private ScreenSettingsViewModel? unifiedScreen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUnified))]
    [NotifyPropertyChangedFor(nameof(ShowPerScreen))]
    private bool unifiedMode;

    public bool ShowUnified => UnifiedMode;

    public bool ShowPerScreen => !UnifiedMode;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    /// <summary>Vrai quand la page Fonds d'écran est affichée : pilote la barre « Appliquer »
    /// de la fenêtre (alimenté par la page elle-même via IsVisibleChanged).</summary>
    [ObservableProperty]
    private bool isWallpapersPageActive;

    /// <summary>(Re)charge écrans + config et reconstruit les ViewModels des deux modes.</summary>
    public void Load()
    {
        Screens.Clear();

        _monitors = _screenService.GetMonitors();
        var config = _configService.Load();

        UnifiedMode = config.Unified;

        // Géométrie du sélecteur : disposition réelle des moniteurs (bureau virtuel) mise à
        // l'échelle dans la boîte du sélecteur — un écran portrait est dessiné vertical.
        double scale = 0;
        double originX = 0, originY = 0;
        if (_monitors.Count > 0)
        {
            originX = _monitors.Min(m => (double)m.Bounds.Left);
            originY = _monitors.Min(m => (double)m.Bounds.Top);
            double unionWidth = _monitors.Max(m => (double)m.Bounds.Right) - originX;
            double unionHeight = _monitors.Max(m => (double)m.Bounds.Bottom) - originY;
            scale = Math.Min(SelectorBoxWidth / unionWidth, SelectorBoxHeight / unionHeight);
            SelectorCanvasWidth = unionWidth * scale;
            SelectorCanvasHeight = unionHeight * scale;
        }

        int index = 1;
        foreach (var monitor in _monitors)
        {
            var screenConfig = config.FindScreen(monitor.DeviceId) ?? new ScreenConfig { DeviceId = monitor.DeviceId };
            var screen = new ScreenSettingsViewModel(index++, monitor, screenConfig, Sources.EditorSourceOptions);
            screen.SetSelectorGeometry(
                (monitor.Bounds.Left - originX) * scale,
                (monitor.Bounds.Top - originY) * scale,
                monitor.Width * scale,
                monitor.Height * scale);
            screen.SetCurrentWallpaper(_wallpaperService.GetWallpaper(monitor.DeviceId));
            Screens.Add(screen);
        }

        SelectedScreen = Screens.FirstOrDefault();
        UnifiedScreen = new ScreenSettingsViewModel(config.UnifiedConfig, Sources.EditorSourceOptions);
    }

    [RelayCommand]
    private void Apply()
    {
        // Repart de la config persistée pour ne pas écraser les réglages hors page
        // wallpaper (thème notamment).
        var config = _configService.Load();
        config.Unified = UnifiedMode;
        config.UnifiedConfig = UnifiedScreen?.ToConfig() ?? new ScreenConfig();
        config.Screens = Screens.Select(s => s.ToConfig()).ToList();

        try
        {
            _configService.Save(config);
            _wallpaperService.Apply(config, _monitors);
            // Relance les diaporamas selon la nouvelle config (première image immédiate).
            _slideshowService.Restart(config, _monitors);
            RefreshCurrentWallpapers();
            StatusMessage = UnifiedMode
                ? $"Même fond appliqué à {_monitors.Count} écran(s)."
                : $"Appliqué à {_monitors.Count} écran(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Échec : {ex.Message}";
        }
    }

    /// <summary>Met à jour les miniatures « fond appliqué » du sélecteur après un Appliquer.</summary>
    private void RefreshCurrentWallpapers()
    {
        foreach (var monitor in _monitors)
        {
            Screens.FirstOrDefault(s => s.DeviceId == monitor.DeviceId)
                ?.SetCurrentWallpaper(_wallpaperService.GetWallpaper(monitor.DeviceId));
        }
    }
}
