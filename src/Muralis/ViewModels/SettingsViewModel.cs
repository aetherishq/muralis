using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muralis.Models;
using Muralis.Resources;
using Muralis.Services;

namespace Muralis.ViewModels;

/// <summary>
/// ViewModel de la fenêtre de paramètres. Deux modes exclusifs pilotés par <see cref="UnifiedMode"/> :
/// un même fond pour tous les écrans (<see cref="UnifiedScreen"/>), ou une config indépendante par
/// écran (<see cref="Screens"/>).
/// </summary>
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ConfigService _configService;
    private readonly ScreenService _screenService;
    private readonly WallpaperService _wallpaperService;
    private readonly SlideshowService _slideshowService;
    private readonly ImageSaveService _imageSaveService;

    private IReadOnlyList<MonitorInfo> _monitors = [];

    /// <summary>Device paths dont la config a été reprise par un écran branché sur un autre
    /// port (même identifiant matériel) : remplacés, pas conservés, au prochain « Appliquer ».</summary>
    private readonly HashSet<string> _adoptedDeviceIds = [];

    public SettingsViewModel(
        ConfigService configService,
        ScreenService screenService,
        WallpaperService wallpaperService,
        SlideshowService slideshowService,
        ImageSaveService imageSaveService,
        AppSettingsViewModel appSettings,
        SourcesViewModel sources)
    {
        _configService = configService;
        _screenService = screenService;
        _wallpaperService = wallpaperService;
        _slideshowService = slideshowService;
        _imageSaveService = imageSaveService;
        AppSettings = appSettings;
        Sources = sources;
        Load();

        // Miniatures du sélecteur resynchronisées à chaque pose réelle d'un diaporama
        // (la première image d'une source web arrive après le Restart, en asynchrone).
        _slideshowService.WallpaperApplied += OnWallpaperApplied;

        // Feedback visible quand une source web ne fournit pas d'image (URL/chemin JSON
        // erronés, API en panne…) — sinon l'échec serait silencieux.
        _slideshowService.WebFetchFailed += OnWebFetchFailed;

        // Source modifiée/retirée : seuls ses diaporamas suivent, tout de suite (#47).
        Sources.SourceChanged += OnSourceChanged;
    }

    /// <summary>Les services survivent à ce ViewModel (reconstruit à chaque bascule de
    /// langue) : sans désabonnement, chaque ancien VM continuerait de recharger ses
    /// miniatures à chaque pose (#49).</summary>
    public void Dispose()
    {
        _slideshowService.WallpaperApplied -= OnWallpaperApplied;
        _slideshowService.WebFetchFailed -= OnWebFetchFailed;
        Sources.SourceChanged -= OnSourceChanged;
        AppSettings.Dispose();
    }

    private void OnWebFetchFailed(string name, string reason) =>
        StatusMessage = string.Format(Strings.Status_FetchFailedReasonFormat, name, reason);

    private void OnSourceChanged(string id) => _slideshowService.RefreshSource(_configService.Load(), id);

    /// <summary>Vrai quand une pose a eu lieu fenêtre masquée : l'aperçu sera rafraîchi
    /// au prochain affichage de la page plutôt qu'à chaque pose (#49).</summary>
    private bool _previewStale;

    private void OnWallpaperApplied()
    {
        if (IsWallpapersPageActive)
            RefreshCurrentWallpapers();
        else
            _previewStale = true;
    }

    partial void OnIsWallpapersPageActiveChanged(bool value)
    {
        if (value && _previewStale)
        {
            _previewStale = false;
            RefreshCurrentWallpapers();
        }
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
        _adoptedDeviceIds.Clear();

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
            // Écran vu par un autre port de la dock : sa config est reprise (et sera
            // enregistrée sous le nouveau device path au prochain « Appliquer », #42).
            var screenConfig = config.FindScreen(monitor.DeviceId)
                ?? Adopt(ScreenMatcher.FindByHardware(config, monitor, _monitors), monitor)
                ?? new ScreenConfig { DeviceId = monitor.DeviceId };
            var screen = new ScreenSettingsViewModel(index++, monitor, screenConfig, Sources.EditorRandomOptions, Sources.EditorDailyOptions);
            screen.SetSelectorGeometry(
                (monitor.Bounds.Left - originX) * scale,
                (monitor.Bounds.Top - originY) * scale,
                monitor.Width * scale,
                monitor.Height * scale);
            screen.SetCurrentWallpaper(_wallpaperService.GetWallpaper(monitor.DeviceId));
            Screens.Add(screen);
        }

        SelectedScreen = Screens.FirstOrDefault();
        UnifiedScreen = new ScreenSettingsViewModel(config.UnifiedConfig, Sources.EditorRandomOptions, Sources.EditorDailyOptions);
    }

    private ScreenConfig? Adopt(ScreenConfig? match, MonitorInfo monitor)
    {
        if (match is null)
            return null;
        _adoptedDeviceIds.Add(match.DeviceId);
        return ScreenMatcher.CloneFor(match, monitor.DeviceId);
    }

    /// <summary>Configs des écrans branchés (éditées ici) + celles des écrans débranchés,
    /// conservées : appliquer une fois dédocké n'efface plus les réglages des écrans de la
    /// dock (#42).</summary>
    internal static List<ScreenConfig> MergeScreens(
        IEnumerable<ScreenConfig> edited,
        IEnumerable<ScreenConfig> persisted,
        IReadOnlySet<string> connected,
        IReadOnlySet<string> adopted) =>
        edited
            .Concat(persisted.Where(s => !connected.Contains(s.DeviceId) && !adopted.Contains(s.DeviceId)))
            .ToList();

    [RelayCommand]
    private async Task Apply()
    {
        // Repart de la config persistée pour ne pas écraser les réglages hors page
        // wallpaper (thème notamment).
        var config = _configService.Load();
        config.Unified = UnifiedMode;
        config.UnifiedConfig = UnifiedScreen?.ToConfig() ?? new ScreenConfig();
        config.Screens = MergeScreens(
            Screens.Select(s => s.ToConfig()),
            config.Screens,
            _monitors.Select(m => m.DeviceId).ToHashSet(),
            _adoptedDeviceIds);

        try
        {
            _configService.Save(config);
        }
        catch (Exception ex)
        {
            // Rien n'est appliqué si la config n'a pas pu être persistée : l'état affiché
            // et l'état au prochain démarrage divergeraient.
            Log.Error("Apply: saving the configuration failed", ex);
            StatusMessage = Strings.Status_ApplyFailed;
            return;
        }

        // Écrans isolés (Apply ne lève pas) et diaporamas TOUJOURS relancés : une image
        // fixe illisible sur un écran ne laisse plus tourner l'ancien diaporama d'un autre
        // jusqu'au prochain démarrage (#47).
        var report = await _wallpaperService.ApplyAsync(config, _monitors);
        _slideshowService.Restart(config, _monitors);
        RefreshCurrentWallpapers();

        StatusMessage = report.Failed > 0
            ? string.Format(Strings.Status_AppliedPartialFormat, report.Failed)
            : string.Format(UnifiedMode ? Strings.Status_AppliedUnifiedFormat : Strings.Status_AppliedFormat, _monitors.Count);
    }

    /// <summary>Enregistre l'image web affichée sur un écran (bouton au survol de sa
    /// miniature) et rend compte dans la barre de statut.</summary>
    [RelayCommand]
    private void SaveCurrent(ScreenSettingsViewModel screen)
    {
        try
        {
            StatusMessage = _imageSaveService.SaveCurrent(screen.DeviceId) switch
            {
                (SaveOutcome.Saved, var name) => string.Format(Strings.Status_ImageSavedFormat, name),
                (SaveOutcome.AlreadySaved, var name) => string.Format(Strings.Status_ImageAlreadySavedFormat, name),
                _ => Strings.Status_NothingToSave,
            };
        }
        catch (Exception ex)
        {
            Log.Warn($"Save current wallpaper failed (screen '{screen.DeviceId}')", ex);
            StatusMessage = string.Format(Strings.Status_FailedFormat, ex.Message);
        }
    }

    /// <summary>Met à jour les miniatures « fond appliqué » du sélecteur (et l'état
    /// « enregistrable » de chaque écran) après chaque pose.</summary>
    private void RefreshCurrentWallpapers()
    {
        foreach (var monitor in _monitors)
        {
            var screen = Screens.FirstOrDefault(s => s.DeviceId == monitor.DeviceId);
            if (screen is null)
                continue;
            screen.SetCurrentWallpaper(_wallpaperService.GetWallpaper(monitor.DeviceId));
            screen.CanSaveCurrent = _slideshowService.CurrentWebImage(monitor.DeviceId) is not null;
        }
    }
}
