using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Muralis.Models;
using Muralis.Services;

namespace Muralis.ViewModels;

/// <summary>
/// Paramètres de base de l'application (page « Paramètres » du menu hamburger).
/// Application immédiate : le thème est appliqué + sauvé dans config.json au changement,
/// le démarrage Windows est écrit directement dans le registre (jamais dans config.json,
/// cf. AGENTS.md — l'état est toujours relu depuis le registre).
/// </summary>
public partial class AppSettingsViewModel : ObservableObject
{
    /// <summary>Choix de thème présentable dans un ComboBox.</summary>
    public sealed record ThemeOption(ThemePreference Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ConfigService _configService;
    private readonly ThemeService _themeService;
    private readonly Func<Window?> _windowAccessor;
    private readonly bool _initialized;

    public AppSettingsViewModel(ConfigService configService, ThemeService themeService, Func<Window?> windowAccessor)
    {
        _configService = configService;
        _themeService = themeService;
        _windowAccessor = windowAccessor;

        var theme = configService.Load().Theme;
        selectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == theme) ?? ThemeOptions[0];
        startWithWindows = ReadStartupState();
        _initialized = true;
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(ThemePreference.System, "Système (Windows)"),
        new(ThemePreference.Dark, "Sombre"),
        new(ThemePreference.Light, "Clair"),
    ];

    [ObservableProperty]
    private ThemeOption selectedTheme;

    [ObservableProperty]
    private bool startWithWindows;

    /// <summary>Relit l'état réel du registre (appelé quand la page redevient visible).</summary>
    public void Refresh() => StartWithWindows = ReadStartupState();

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        if (!_initialized)
            return;

        _themeService.Apply(value.Value, _windowAccessor());
        var config = _configService.Load();
        config.Theme = value.Value;
        _configService.Save(config);
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!_initialized)
            return;

        try
        {
            StartupService.SetStartup(value);
        }
        catch (Exception)
        {
            // Registre inaccessible : l'état réel sera relu au prochain affichage de la page.
        }
    }

    private static bool ReadStartupState()
    {
        try
        {
            return StartupService.IsEnabled();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
