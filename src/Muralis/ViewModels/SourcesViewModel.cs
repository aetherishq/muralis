using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muralis.Models;
using Muralis.Resources;
using Muralis.Services;
using Muralis.Services.Sources;

namespace Muralis.ViewModels;

/// <summary>
/// Page « Sources web » : liste des sources ajoutées par l'utilisateur (persistées dans
/// config.json), ajout depuis le catalogue de presets embarqué ou en source personnalisée
/// (URL + chemin JSON, sans recompilation — cf. AGENTS.md). Sauvegarde immédiate.
/// Maintient aussi les deux listes de choix offertes aux éditeurs d'écran, routées par
/// <see cref="SourceKind"/> : <see cref="EditorRandomOptions"/> (diaporama) et
/// <see cref="EditorDailyOptions"/> (card Image, sources quotidiennes).
/// </summary>
public partial class SourcesViewModel : ObservableObject
{
    /// <summary>Choix de type d'une source personnalisée.</summary>
    public sealed record KindOption(SourceKind Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ConfigService _configService;

    public SourcesViewModel(ConfigService configService)
    {
        _configService = configService;

        EditorRandomOptions.Add(ScreenSettingsViewModel.LocalFolderOption);
        EditorDailyOptions.Add(ScreenSettingsViewModel.LocalFileOption);
        foreach (var source in configService.Load().Sources)
        {
            Sources.Add(source);
            OptionsFor(source.Kind).Add(source.Name);
        }
        RefreshAvailablePresets();
        selectedKind = KindOptions[0];

        Sources.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoSources));
    }

    /// <summary>Vrai quand aucune source n'est ajoutée (état vide de la page).</summary>
    public bool HasNoSources => Sources.Count == 0;

    /// <summary>Sources ajoutées (affichées sur la page, référencées par les écrans).</summary>
    public ObservableCollection<WallpaperSourceConfig> Sources { get; } = [];

    /// <summary>Choix du diaporama : « Dossier local » + sources aléatoires.</summary>
    public ObservableCollection<string> EditorRandomOptions { get; } = [];

    /// <summary>Choix de la card Image : « Fichier local » + sources quotidiennes.</summary>
    public ObservableCollection<string> EditorDailyOptions { get; } = [];

    public IReadOnlyList<KindOption> KindOptions { get; } =
    [
        new(SourceKind.Random, Strings.SourceKind_Random),
        new(SourceKind.Daily, Strings.SourceKind_Daily),
    ];

    /// <summary>Type choisi pour la prochaine source personnalisée.</summary>
    [ObservableProperty]
    private KindOption selectedKind;

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
            StatusMessage = Strings.Status_KeyRequired;
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
            Kind = SelectedKind.Value,
            RequestUrl = CustomUrl.Trim(),
            ImageUrlJsonPath = CustomJsonPath.Trim(),
            ApiKeyHeader = CustomApiKeyHeader.Trim(),
            ApiKey = CustomApiKey.Trim(),
        };

        if (!source.IsValid)
        {
            StatusMessage = Strings.Status_CustomInvalid;
            return;
        }
        if (IsNameTaken(source.Name))
        {
            StatusMessage = string.Format(Strings.Status_NameTakenFormat, source.Name);
            return;
        }

        Add(source);
        if (KeyWithoutHeader(source))
            StatusMessage = string.Format(Strings.Status_AddedNoHeaderFormat, source.Name);
        CustomName = CustomUrl = CustomJsonPath = CustomApiKeyHeader = CustomApiKey = string.Empty;
        SelectedKind = KindOptions[0];
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
            StatusMessage = Strings.Status_UpdateInvalid;
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

        StatusMessage = string.Format(
            KeyWithoutHeader(source) ? Strings.Status_SavedNoHeaderFormat : Strings.Status_SavedFormat,
            source.Name);
    }

    /// <summary>Clé fournie mais pas d'en-tête : la clé ne partirait dans aucune requête.</summary>
    private static bool KeyWithoutHeader(WallpaperSourceConfig source) =>
        !string.IsNullOrWhiteSpace(source.ApiKey) && string.IsNullOrWhiteSpace(source.ApiKeyHeader);

    [RelayCommand]
    private void Remove(WallpaperSourceConfig source)
    {
        Sources.Remove(source);
        OptionsFor(source.Kind).Remove(source.Name);

        var config = _configService.Load();
        config.Sources.RemoveAll(s => s.Name == source.Name);
        _configService.Save(config);

        RefreshAvailablePresets();
        StatusMessage = string.Format(Strings.Status_RemovedFormat, source.Name);
    }

    private void Add(WallpaperSourceConfig source)
    {
        if (IsNameTaken(source.Name))
        {
            StatusMessage = string.Format(Strings.Status_NameTakenFormat, source.Name);
            return;
        }

        Sources.Add(source);
        OptionsFor(source.Kind).Add(source.Name);

        var config = _configService.Load();
        config.Sources.Add(source);
        _configService.Save(config);

        StatusMessage = string.Format(Strings.Status_AddedFormat, source.Name);
    }

    /// <summary>Collection d'options d'éditeur correspondant au type de source.</summary>
    private ObservableCollection<string> OptionsFor(SourceKind kind) =>
        kind == SourceKind.Daily ? EditorDailyOptions : EditorRandomOptions;

    private bool IsNameTaken(string name) =>
        name == ScreenSettingsViewModel.LocalFolderOption
        || name == ScreenSettingsViewModel.LocalFileOption
        || Sources.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    private void RefreshAvailablePresets()
    {
        AvailablePresets.Clear();
        foreach (var preset in SourcePresets.All.Where(p => !IsNameTaken(p.Name)))
            AvailablePresets.Add(preset);

        // Présélectionner le premier preset restant : un ComboBox vide fait « creux » et
        // coûte un clic pour découvrir le catalogue (issue #6).
        if (SelectedPreset is null || !AvailablePresets.Contains(SelectedPreset))
            SelectedPreset = AvailablePresets.FirstOrDefault();
    }
}
