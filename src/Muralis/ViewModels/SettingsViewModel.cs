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

    private IReadOnlyList<MonitorInfo> _monitors = [];

    public SettingsViewModel(
        ConfigService configService,
        ScreenService screenService,
        WallpaperService wallpaperService)
    {
        _configService = configService;
        _screenService = screenService;
        _wallpaperService = wallpaperService;
        Load();
    }

    /// <summary>Configs par écran (mode séparé).</summary>
    public ObservableCollection<ScreenSettingsViewModel> Screens { get; } = [];

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

    /// <summary>(Re)charge écrans + config et reconstruit les ViewModels des deux modes.</summary>
    public void Load()
    {
        Screens.Clear();

        _monitors = _screenService.GetMonitors();
        var config = _configService.Load();

        UnifiedMode = config.Unified;

        int index = 1;
        foreach (var monitor in _monitors)
        {
            var screenConfig = config.FindScreen(monitor.DeviceId) ?? new ScreenConfig { DeviceId = monitor.DeviceId };
            Screens.Add(new ScreenSettingsViewModel(index++, monitor, screenConfig));
        }

        UnifiedScreen = new ScreenSettingsViewModel(config.UnifiedConfig);
    }

    [RelayCommand]
    private void Apply()
    {
        var config = new AppConfig
        {
            Unified = UnifiedMode,
            UnifiedConfig = UnifiedScreen?.ToConfig() ?? new ScreenConfig(),
            Screens = Screens.Select(s => s.ToConfig()).ToList(),
        };

        try
        {
            _configService.Save(config);
            _wallpaperService.Apply(config, _monitors);
            StatusMessage = UnifiedMode
                ? $"Même fond appliqué à {_monitors.Count} écran(s)."
                : $"Appliqué à {_monitors.Count} écran(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Échec : {ex.Message}";
        }
    }
}
