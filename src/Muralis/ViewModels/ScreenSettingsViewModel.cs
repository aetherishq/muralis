using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Muralis.Models;
using Muralis.Services;

namespace Muralis.ViewModels;

/// <summary>
/// Réglages d'une cible de fond d'écran : soit un moniteur précis (mode config par écran),
/// soit l'ensemble des écrans (mode unifié). Expose l'image et le mode d'affichage éditables.
/// </summary>
public partial class ScreenSettingsViewModel : ObservableObject
{
    private readonly MonitorInfo? _monitor;

    /// <summary>Cible = un moniteur précis (config séparée par écran).</summary>
    public ScreenSettingsViewModel(int index, MonitorInfo monitor, ScreenConfig config)
    {
        _monitor = monitor;
        DisplayLabel = $"Écran {index}";
        ResolutionLabel = monitor.ResolutionLabel;
        // Span n'a pas de sens sur un seul écran : réservé au mode unifié.
        AvailableModes = ModesExcept(DesktopWallpaperPosition.Span);
        imagePath = string.IsNullOrEmpty(config.SourcePath) ? null : config.SourcePath;
        displayMode = config.DisplayMode == DesktopWallpaperPosition.Span
            ? DesktopWallpaperPosition.Fill
            : config.DisplayMode;
    }

    /// <summary>Cible = tous les écrans (mode unifié). Span autorisé.</summary>
    public ScreenSettingsViewModel(ScreenConfig config)
    {
        _monitor = null;
        DisplayLabel = "Tous les écrans";
        ResolutionLabel = "Même fond sur tous les moniteurs";
        AvailableModes = Enum.GetValues<DesktopWallpaperPosition>();
        imagePath = string.IsNullOrEmpty(config.SourcePath) ? null : config.SourcePath;
        displayMode = config.DisplayMode;
    }

    /// <summary>Device path du moniteur ciblé (vide en mode unifié).</summary>
    public string DeviceId => _monitor?.DeviceId ?? string.Empty;

    public string DisplayLabel { get; }

    public string ResolutionLabel { get; }

    /// <summary>Modes proposés dans le ComboBox (dépend de la cible).</summary>
    public IReadOnlyList<DesktopWallpaperPosition> AvailableModes { get; }

    [ObservableProperty]
    private string? imagePath;

    [ObservableProperty]
    private DesktopWallpaperPosition displayMode;

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

    /// <summary>Projette l'état courant vers un <see cref="ScreenConfig"/> persistable.</summary>
    public ScreenConfig ToConfig() => new()
    {
        DeviceId = DeviceId,
        Mode = WallpaperMode.Fixed,
        SourceType = "LocalFile",
        SourcePath = ImagePath ?? string.Empty,
        DisplayMode = DisplayMode,
    };

    private static IReadOnlyList<DesktopWallpaperPosition> ModesExcept(DesktopWallpaperPosition excluded) =>
        Enum.GetValues<DesktopWallpaperPosition>().Where(m => m != excluded).ToArray();
}
