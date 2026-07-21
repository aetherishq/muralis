using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muralis.Models;
using Muralis.Services;
using Muralis.Services.Sources;

namespace Muralis.ViewModels;

/// <summary>
/// Page « Sources web » : liste des sources ajoutées par l'utilisateur (persistées dans
/// config.json), ajout depuis le catalogue de presets embarqué ou en source personnalisée
/// (URL + chemin JSON, sans recompilation — cf. AGENTS.md). Sauvegarde immédiate.
/// Maintient aussi <see cref="EditorSourceOptions"/>, la liste des choix de source offerte
/// aux éditeurs d'écran (« Dossier local » + noms des sources ajoutées).
/// </summary>
public partial class SourcesViewModel : ObservableObject
{
    private readonly ConfigService _configService;

    public SourcesViewModel(ConfigService configService)
    {
        _configService = configService;

        EditorSourceOptions.Add(ScreenSettingsViewModel.LocalFolderOption);
        foreach (var source in configService.Load().Sources)
        {
            Sources.Add(source);
            EditorSourceOptions.Add(source.Name);
        }
        RefreshAvailablePresets();

        Sources.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoSources));
    }

    /// <summary>Vrai quand aucune source n'est ajoutée (état vide de la page).</summary>
    public bool HasNoSources => Sources.Count == 0;

    /// <summary>Sources ajoutées (affichées sur la page, référencées par les diaporamas).</summary>
    public ObservableCollection<WallpaperSourceConfig> Sources { get; } = [];

    /// <summary>Choix proposés dans les éditeurs d'écran : « Dossier local » + sources ajoutées.</summary>
    public ObservableCollection<string> EditorSourceOptions { get; } = [];

    /// <summary>Presets du catalogue pas encore ajoutés.</summary>
    public ObservableCollection<SourcePreset> AvailablePresets { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresetNote), nameof(ShowPresetKey), nameof(ShowPresetNote))]
    private SourcePreset? selectedPreset;

    public string PresetNote => SelectedPreset?.Note ?? string.Empty;

    public bool ShowPresetNote => SelectedPreset is not null;

    public bool ShowPresetKey => SelectedPreset?.RequiresKey == true;

    [ObservableProperty]
    private string presetApiKey = string.Empty;

    [ObservableProperty]
    private string customName = string.Empty;

    [ObservableProperty]
    private string customUrl = string.Empty;

    [ObservableProperty]
    private string customJsonPath = string.Empty;

    [ObservableProperty]
    private string customApiKeyHeader = string.Empty;

    [ObservableProperty]
    private string customApiKey = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [RelayCommand]
    private void AddPreset()
    {
        if (SelectedPreset is null)
            return;
        if (SelectedPreset.RequiresKey && string.IsNullOrWhiteSpace(PresetApiKey))
        {
            StatusMessage = "Cette source nécessite une clé API.";
            return;
        }

        Add(SelectedPreset.ToConfig(PresetApiKey.Trim()));
        PresetApiKey = string.Empty;
        SelectedPreset = null;
        RefreshAvailablePresets();
    }

    [RelayCommand]
    private void AddCustom()
    {
        var source = new WallpaperSourceConfig
        {
            Name = CustomName.Trim(),
            RequestUrl = CustomUrl.Trim(),
            ImageUrlJsonPath = CustomJsonPath.Trim(),
            ApiKeyHeader = CustomApiKeyHeader.Trim(),
            ApiKey = CustomApiKey.Trim(),
        };

        if (!source.IsValid)
        {
            StatusMessage = "Nom, URL absolue et chemin JSON sont requis.";
            return;
        }
        if (IsNameTaken(source.Name))
        {
            StatusMessage = $"Une source « {source.Name} » existe déjà.";
            return;
        }

        Add(source);
        if (KeyWithoutHeader(source))
            StatusMessage = $"Source « {source.Name} » ajoutée — attention : sans « En-tête API », la clé n'est jamais envoyée (indiquez l'en-tête, ex. X-API-Key, ou mettez la clé dans l'URL).";
        CustomName = CustomUrl = CustomJsonPath = CustomApiKeyHeader = CustomApiKey = string.Empty;
    }

    /// <summary>
    /// Persiste les modifications faites sur une source depuis sa card (URL, chemin JSON, clé…).
    /// Le nom, identité référencée par les écrans, n'est pas éditable.
    /// </summary>
    [RelayCommand]
    private void Update(WallpaperSourceConfig source)
    {
        if (!source.IsValid)
        {
            StatusMessage = "URL absolue et chemin JSON requis.";
            return;
        }

        var config = _configService.Load();
        int persisted = config.Sources.FindIndex(s => s.Name == source.Name);
        if (persisted >= 0)
            config.Sources[persisted] = source;
        else
            config.Sources.Add(source);
        _configService.Save(config);

        // Le modèle n'implémente pas INotifyPropertyChanged : re-signaler l'élément pour
        // rafraîchir l'en-tête de sa card (URL affichée).
        int index = Sources.IndexOf(source);
        if (index >= 0)
            Sources[index] = source;

        StatusMessage = KeyWithoutHeader(source)
            ? $"Source « {source.Name} » enregistrée — attention : sans « En-tête API », la clé n'est jamais envoyée (indiquez l'en-tête, ex. X-API-Key, ou mettez la clé dans l'URL)."
            : $"Source « {source.Name} » enregistrée — réappliquez les écrans qui l'utilisent.";
    }

    /// <summary>Clé fournie mais pas d'en-tête : la clé ne partirait dans aucune requête.</summary>
    private static bool KeyWithoutHeader(WallpaperSourceConfig source) =>
        !string.IsNullOrWhiteSpace(source.ApiKey) && string.IsNullOrWhiteSpace(source.ApiKeyHeader);

    [RelayCommand]
    private void Remove(WallpaperSourceConfig source)
    {
        Sources.Remove(source);
        EditorSourceOptions.Remove(source.Name);

        var config = _configService.Load();
        config.Sources.RemoveAll(s => s.Name == source.Name);
        _configService.Save(config);

        RefreshAvailablePresets();
        StatusMessage = $"Source « {source.Name} » retirée.";
    }

    private void Add(WallpaperSourceConfig source)
    {
        if (IsNameTaken(source.Name))
        {
            StatusMessage = $"Une source « {source.Name} » existe déjà.";
            return;
        }

        Sources.Add(source);
        EditorSourceOptions.Add(source.Name);

        var config = _configService.Load();
        config.Sources.Add(source);
        _configService.Save(config);

        StatusMessage = $"Source « {source.Name} » ajoutée.";
    }

    private bool IsNameTaken(string name) =>
        name == ScreenSettingsViewModel.LocalFolderOption
        || Sources.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    private void RefreshAvailablePresets()
    {
        AvailablePresets.Clear();
        foreach (var preset in SourcePresets.All.Where(p => !IsNameTaken(p.Name)))
            AvailablePresets.Add(preset);
    }
}
