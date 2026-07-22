using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Muralis.Models;
using Muralis.Resources;
using Muralis.Services;

namespace Muralis.ViewModels;

/// <summary>
/// Paramètres de base de l'application (page « Paramètres »).
/// Application immédiate : le thème et la langue sont appliqués + sauvés dans config.json
/// au changement (la langue reconstruit l'UI via le callback fourni par App), le démarrage
/// Windows est écrit directement dans le registre (jamais dans config.json, cf. AGENTS.md).
/// </summary>
public partial class AppSettingsViewModel : ObservableObject
{
    /// <summary>Choix de thème présentable dans un ComboBox.</summary>
    public sealed record ThemeOption(ThemePreference Value, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>Choix de langue (<c>null</c> = suivre Windows). Les noms de langues
    /// s'affichent chacun dans leur propre langue, convention d'usage.</summary>
    public sealed record LanguageOption(string? Code, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ConfigService _configService;
    private readonly ThemeService _themeService;
    private readonly Func<Window?> _windowAccessor;
    private readonly Action<string?> _applyLanguage;
    private readonly bool _initialized;

    public AppSettingsViewModel(
        ConfigService configService,
        ThemeService themeService,
        Func<Window?> windowAccessor,
        Action<string?> applyLanguage)
    {
        _configService = configService;
        _themeService = themeService;
        _windowAccessor = windowAccessor;
        _applyLanguage = applyLanguage;

        var config = configService.Load();
        selectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == config.Theme) ?? ThemeOptions[0];
        selectedLanguage = LanguageOptions.FirstOrDefault(o => o.Code == config.Language) ?? LanguageOptions[0];
        startWithWindows = ReadStartupState();
        wallhavenApiKey = LoadApiKey(config, WallhavenPresetId);
        saveDirectory = ResolveSaveDirectory(config);
        _initialized = true;
    }

    private const string WallhavenPresetId = "wallhaven";

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(ThemePreference.System, Strings.Option_System),
        new(ThemePreference.Dark, Strings.Theme_Dark),
        new(ThemePreference.Light, Strings.Theme_Light),
    ];

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new(null, Strings.Option_System),
        new("fr", "Français"),
        new("en", "English"),
    ];

    [ObservableProperty]
    private ThemeOption selectedTheme;

    [ObservableProperty]
    private LanguageOption selectedLanguage;

    [ObservableProperty]
    private bool startWithWindows;

    /// <summary>Clé API Wallhaven (partagée par toutes les instances Wallhaven).
    /// Persistée chiffrée DPAPI dans <see cref="AppConfig.ApiKeys"/>, sauvée au fil de la
    /// saisie ; utilisée à la prochaine requête (aucun redémarrage nécessaire).</summary>
    [ObservableProperty]
    private string wallhavenApiKey;

    partial void OnWallhavenApiKeyChanged(string value)
    {
        if (!_initialized)
            return;

        var config = _configService.Load();
        if (string.IsNullOrWhiteSpace(value))
            config.ApiKeys.Remove(WallhavenPresetId);
        else
            config.ApiKeys[WallhavenPresetId] = ApiKeyProtector.Protect(value.Trim());
        _configService.Save(config);
    }

    /// <summary>Dossier où « Enregistrer le fond actuel » copie les images (affiché dans la
    /// card ; défaut Images\Muralis).</summary>
    [ObservableProperty]
    private string saveDirectory;

    [RelayCommand]
    private void BrowseSaveDirectory()
    {
        var dialog = new OpenFolderDialog { Title = Strings.Settings_SaveDirTitle };
        if (dialog.ShowDialog() != true)
            return;

        var config = _configService.Load();
        config.SaveDirectory = dialog.FolderName;
        _configService.Save(config);
        SaveDirectory = dialog.FolderName;
    }

    [RelayCommand]
    private void OpenSaveDirectory()
    {
        try
        {
            Directory.CreateDirectory(SaveDirectory);
            Process.Start(new ProcessStartInfo(SaveDirectory) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Dossier inaccessible (lecteur débranché…) : ne pas faire planter la page.
        }
    }

    private static string ResolveSaveDirectory(AppConfig config) =>
        config.SaveDirectory is { Length: > 0 } dir ? dir : ImageSaveService.DefaultDirectory;

    private static string LoadApiKey(AppConfig config, string presetId) =>
        config.ApiKeys.TryGetValue(presetId, out string? blob)
            ? ApiKeyProtector.Unprotect(blob) ?? string.Empty
            : string.Empty;

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

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (!_initialized)
            return;

        // Persistance + bascule à chaud (App reconstruit fenêtre, tray et ViewModels —
        // ce ViewModel compris : ne rien faire d'autre après cet appel).
        _applyLanguage(value.Code);
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
