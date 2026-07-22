using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muralis.Models;
using Muralis.Resources;
using Muralis.Services;
using Muralis.Services.Sources;

namespace Muralis.ViewModels;

/// <summary>
/// Page « Sources web » : instances de sources ajoutées par l'utilisateur (persistées dans
/// config.json), créées depuis le catalogue de presets embarqué ou en source personnalisée
/// (URL + chemin JSON, sans recompilation — cf. AGENTS.md). Un même preset peut être
/// instancié plusieurs fois : l'identité est <c>WallpaperSourceConfig.Id</c>, le nom est un
/// libellé éditable. Sauvegarde immédiate. Maintient aussi les deux listes de choix offertes
/// aux éditeurs d'écran, routées par <see cref="SourceKind"/> :
/// <see cref="EditorRandomOptions"/> (diaporama) et <see cref="EditorDailyOptions"/>
/// (card Image, sources quotidiennes).
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

        EditorRandomOptions.Add(new SourceOption(SlideshowService.LocalFolderSourceType, ScreenSettingsViewModel.LocalFolderOption));
        EditorDailyOptions.Add(new SourceOption(ScreenConfig.LocalFileSourceType, ScreenSettingsViewModel.LocalFileOption));
        foreach (var source in configService.Load().Sources)
        {
            Sources.Add(source);
            OptionsFor(source.Kind).Add(new SourceOption(source.Id, source.Name));
        }
        SelectedPreset = AvailablePresets.FirstOrDefault();
        selectedKind = KindOptions[0];

        Sources.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoSources));
    }

    /// <summary>Vrai quand aucune source n'est ajoutée (état vide de la page).</summary>
    public bool HasNoSources => Sources.Count == 0;

    /// <summary>Instances ajoutées (affichées sur la page, référencées par les écrans via leur Id).</summary>
    public ObservableCollection<WallpaperSourceConfig> Sources { get; } = [];

    /// <summary>Choix du diaporama : « Dossier local » + sources aléatoires.</summary>
    public ObservableCollection<SourceOption> EditorRandomOptions { get; } = [];

    /// <summary>Choix de la card Image : « Fichier local » + sources quotidiennes.</summary>
    public ObservableCollection<SourceOption> EditorDailyOptions { get; } = [];

    public IReadOnlyList<KindOption> KindOptions { get; } =
    [
        new(SourceKind.Random, Strings.SourceKind_Random),
        new(SourceKind.Daily, Strings.SourceKind_Daily),
    ];

    /// <summary>Type choisi pour la prochaine source personnalisée.</summary>
    [ObservableProperty]
    private KindOption selectedKind;

    /// <summary>Catalogue complet : un preset reste proposé même déjà instancié
    /// (instances multiples, issue #12).</summary>
    public IReadOnlyList<SourcePreset> AvailablePresets => SourcePresets.All;

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

        var source = SelectedPreset.ToConfig(PresetApiKey.Trim());
        source.Name = UniqueName(source.Name);
        Add(source);
        PresetApiKey = string.Empty;
    }

    [RelayCommand]
    private void AddCustom()
    {
        var source = new WallpaperSourceConfig
        {
            Id = WallpaperSourceConfig.NewId(),
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
    /// Persiste les modifications faites sur une source depuis sa card (nom, URL, chemin
    /// JSON, clé…). Renommer est sans risque : les écrans référencent l'Id, et l'entrée
    /// correspondante des listes d'éditeur est mise à jour en place.
    /// </summary>
    [RelayCommand]
    private void Update(WallpaperSourceConfig source)
    {
        source.Name = source.Name.Trim();
        if (!source.IsValid)
        {
            StatusMessage = Strings.Status_UpdateInvalid;
            return;
        }
        if (IsNameTaken(source.Name, exceptId: source.Id))
        {
            StatusMessage = string.Format(Strings.Status_NameTakenFormat, source.Name);
            return;
        }

        var config = _configService.Load();
        int persisted = config.Sources.FindIndex(s => s.Id == source.Id);
        if (persisted >= 0)
            config.Sources[persisted] = source;
        else
            config.Sources.Add(source);
        _configService.Save(config);

        var option = OptionsFor(source.Kind).FirstOrDefault(o => o.Id == source.Id);
        if (option is not null)
            option.Label = source.Name;

        // Le modèle n'implémente pas INotifyPropertyChanged : re-signaler l'élément pour
        // rafraîchir l'en-tête de sa card (nom, URL affichée).
        int index = Sources.IndexOf(source);
        if (index >= 0)
            Sources[index] = source;

        StatusMessage = string.Format(
            KeyWithoutHeader(source) ? Strings.Status_SavedNoHeaderFormat : Strings.Status_SavedFormat,
            source.Name);
    }

    /// <summary>Crée une nouvelle instance à partir d'une existante (mêmes paramètres,
    /// identité et nom propres) — évite de tout resaisir pour varier une recherche.</summary>
    [RelayCommand]
    private void Duplicate(WallpaperSourceConfig source)
    {
        Add(new WallpaperSourceConfig
        {
            Id = WallpaperSourceConfig.NewId(),
            PresetId = source.PresetId,
            Name = UniqueName(source.Name),
            Kind = source.Kind,
            RequestUrl = source.RequestUrl,
            ImageUrlJsonPath = source.ImageUrlJsonPath,
            ApiKeyHeader = source.ApiKeyHeader,
            ApiKey = source.ApiKey,
        });
    }

    /// <summary>Clé fournie mais pas d'en-tête : la clé ne partirait dans aucune requête.</summary>
    private static bool KeyWithoutHeader(WallpaperSourceConfig source) =>
        !string.IsNullOrWhiteSpace(source.ApiKey) && string.IsNullOrWhiteSpace(source.ApiKeyHeader);

    [RelayCommand]
    private void Remove(WallpaperSourceConfig source)
    {
        Sources.Remove(source);
        var options = OptionsFor(source.Kind);
        if (options.FirstOrDefault(o => o.Id == source.Id) is { } option)
            options.Remove(option);

        var config = _configService.Load();
        config.Sources.RemoveAll(s => s.Id == source.Id);
        _configService.Save(config);

        StatusMessage = string.Format(Strings.Status_RemovedFormat, source.Name);
    }

    private void Add(WallpaperSourceConfig source)
    {
        Sources.Add(source);
        OptionsFor(source.Kind).Add(new SourceOption(source.Id, source.Name));

        var config = _configService.Load();
        config.Sources.Add(source);
        _configService.Save(config);

        StatusMessage = string.Format(Strings.Status_AddedFormat, source.Name);
    }

    /// <summary>Collection d'options d'éditeur correspondant au type de source.</summary>
    private ObservableCollection<SourceOption> OptionsFor(SourceKind kind) =>
        kind == SourceKind.Daily ? EditorDailyOptions : EditorRandomOptions;

    private bool IsNameTaken(string name, string? exceptId = null) =>
        name == ScreenSettingsViewModel.LocalFolderOption
        || name == ScreenSettingsViewModel.LocalFileOption
        || Sources.Any(s => s.Id != exceptId && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Nom d'affichage disponible : le nom de base, ou « base (2) », « base (3) »…
    /// Un éventuel suffixe existant est retiré d'abord (dupliquer « X (2) » donne « X (3) »).</summary>
    private string UniqueName(string baseName)
    {
        baseName = Regex.Replace(baseName, @" \(\d+\)$", string.Empty);
        if (!IsNameTaken(baseName))
            return baseName;

        for (int n = 2; ; n++)
        {
            string candidate = $"{baseName} ({n})";
            if (!IsNameTaken(candidate))
                return candidate;
        }
    }
}
