//------------------------------------------------------------------------------
// Généré par tools/gen-strings.ps1 depuis Strings.resx — NE PAS ÉDITER À LA MAIN.
//------------------------------------------------------------------------------
using System.Resources;

namespace Muralis.Resources;

/// <summary>Accès typé aux chaînes localisées (Strings.resx = anglais neutre, satellites par culture).</summary>
public static class Strings
{
    /// <summary>ResourceManager partagé (culture résolue via CurrentUICulture).</summary>
    public static ResourceManager ResourceManager { get; } = new("Muralis.Resources.Strings", typeof(Strings).Assembly);

    private static string Get(string name) => ResourceManager.GetString(name) ?? name;
    public static string Common_Add => Get(nameof(Common_Add));
    public static string Common_Apply => Get(nameof(Common_Apply));
    public static string Common_Browse => Get(nameof(Common_Browse));
    public static string Dialog_AllFilesFilter => Get(nameof(Dialog_AllFilesFilter));
    public static string Dialog_ChooseFolderFormat => Get(nameof(Dialog_ChooseFolderFormat));
    public static string Dialog_ChooseImageFormat => Get(nameof(Dialog_ChooseImageFormat));
    public static string Dialog_ImagesFilter => Get(nameof(Dialog_ImagesFilter));
    public static string DisplayMode_Center => Get(nameof(DisplayMode_Center));
    public static string DisplayMode_Fill => Get(nameof(DisplayMode_Fill));
    public static string DisplayMode_Fit => Get(nameof(DisplayMode_Fit));
    public static string DisplayMode_Span => Get(nameof(DisplayMode_Span));
    public static string DisplayMode_Stretch => Get(nameof(DisplayMode_Stretch));
    public static string DisplayMode_Tile => Get(nameof(DisplayMode_Tile));
    public static string Nav_Settings => Get(nameof(Nav_Settings));
    public static string Nav_Wallpapers => Get(nameof(Nav_Wallpapers));
    public static string Nav_WebSources => Get(nameof(Nav_WebSources));
    public static string Option_System => Get(nameof(Option_System));
    public static string Preset_BingNote => Get(nameof(Preset_BingNote));
    public static string Preset_WallhavenNote => Get(nameof(Preset_WallhavenNote));
    public static string Screen_AllScreens => Get(nameof(Screen_AllScreens));
    public static string Screen_AllScreensDesc => Get(nameof(Screen_AllScreensDesc));
    public static string Screen_LabelFormat => Get(nameof(Screen_LabelFormat));
    public static string Screen_LocalFileOption => Get(nameof(Screen_LocalFileOption));
    public static string Screen_LocalFolderOption => Get(nameof(Screen_LocalFolderOption));
    public static string Screen_UnitMinutes => Get(nameof(Screen_UnitMinutes));
    public static string Screen_UnitSeconds => Get(nameof(Screen_UnitSeconds));
    public static string Settings_LanguageDesc => Get(nameof(Settings_LanguageDesc));
    public static string Settings_LanguageTitle => Get(nameof(Settings_LanguageTitle));
    public static string Settings_StartupDesc => Get(nameof(Settings_StartupDesc));
    public static string Settings_StartupTitle => Get(nameof(Settings_StartupTitle));
    public static string Settings_ThemeDesc => Get(nameof(Settings_ThemeDesc));
    public static string Settings_ThemeTitle => Get(nameof(Settings_ThemeTitle));
    public static string Settings_Title => Get(nameof(Settings_Title));
    public static string SourceKind_Daily => Get(nameof(SourceKind_Daily));
    public static string SourceKind_Random => Get(nameof(SourceKind_Random));
    public static string Sources_AddCustom => Get(nameof(Sources_AddCustom));
    public static string Sources_ApiKeyDesc => Get(nameof(Sources_ApiKeyDesc));
    public static string Sources_ApiKeyTitle => Get(nameof(Sources_ApiKeyTitle));
    public static string Sources_CatalogDesc => Get(nameof(Sources_CatalogDesc));
    public static string Sources_CatalogTitle => Get(nameof(Sources_CatalogTitle));
    public static string Sources_CustomDesc => Get(nameof(Sources_CustomDesc));
    public static string Sources_CustomTitle => Get(nameof(Sources_CustomTitle));
    public static string Sources_Duplicate => Get(nameof(Sources_Duplicate));
    public static string Sources_EmptyState => Get(nameof(Sources_EmptyState));
    public static string Sources_KindCaption => Get(nameof(Sources_KindCaption));
    public static string Sources_PlaceholderHeader => Get(nameof(Sources_PlaceholderHeader));
    public static string Sources_PlaceholderJsonPath => Get(nameof(Sources_PlaceholderJsonPath));
    public static string Sources_PlaceholderKey => Get(nameof(Sources_PlaceholderKey));
    public static string Sources_PlaceholderName => Get(nameof(Sources_PlaceholderName));
    public static string Sources_PlaceholderUrl => Get(nameof(Sources_PlaceholderUrl));
    public static string Sources_Remove => Get(nameof(Sources_Remove));
    public static string Sources_SaveChanges => Get(nameof(Sources_SaveChanges));
    public static string Sources_SectionAdd => Get(nameof(Sources_SectionAdd));
    public static string Sources_SectionAdded => Get(nameof(Sources_SectionAdded));
    public static string Sources_Title => Get(nameof(Sources_Title));
    public static string Status_AddedFormat => Get(nameof(Status_AddedFormat));
    public static string Status_AddedNoHeaderFormat => Get(nameof(Status_AddedNoHeaderFormat));
    public static string Status_AppliedFormat => Get(nameof(Status_AppliedFormat));
    public static string Status_AppliedUnifiedFormat => Get(nameof(Status_AppliedUnifiedFormat));
    public static string Status_CustomInvalid => Get(nameof(Status_CustomInvalid));
    public static string Status_FailedFormat => Get(nameof(Status_FailedFormat));
    public static string Status_FetchFailedFormat => Get(nameof(Status_FetchFailedFormat));
    public static string Status_KeyRequired => Get(nameof(Status_KeyRequired));
    public static string Status_NameTakenFormat => Get(nameof(Status_NameTakenFormat));
    public static string Status_RemovedFormat => Get(nameof(Status_RemovedFormat));
    public static string Status_SavedFormat => Get(nameof(Status_SavedFormat));
    public static string Status_SavedNoHeaderFormat => Get(nameof(Status_SavedNoHeaderFormat));
    public static string Status_UpdateInvalid => Get(nameof(Status_UpdateInvalid));
    public static string Theme_Dark => Get(nameof(Theme_Dark));
    public static string Theme_Light => Get(nameof(Theme_Light));
    public static string Tray_NextImage => Get(nameof(Tray_NextImage));
    public static string Tray_Quit => Get(nameof(Tray_Quit));
    public static string Tray_Settings => Get(nameof(Tray_Settings));
    public static string Wallpapers_DailyAutoDesc => Get(nameof(Wallpapers_DailyAutoDesc));
    public static string Wallpapers_DisplayModeDesc => Get(nameof(Wallpapers_DisplayModeDesc));
    public static string Wallpapers_DisplayModeTitle => Get(nameof(Wallpapers_DisplayModeTitle));
    public static string Wallpapers_FolderTitle => Get(nameof(Wallpapers_FolderTitle));
    public static string Wallpapers_ImageSourceDesc => Get(nameof(Wallpapers_ImageSourceDesc));
    public static string Wallpapers_ImageSourceTitle => Get(nameof(Wallpapers_ImageSourceTitle));
    public static string Wallpapers_ImageTitle => Get(nameof(Wallpapers_ImageTitle));
    public static string Wallpapers_IntervalDesc => Get(nameof(Wallpapers_IntervalDesc));
    public static string Wallpapers_IntervalTitle => Get(nameof(Wallpapers_IntervalTitle));
    public static string Wallpapers_NoFolderSelected => Get(nameof(Wallpapers_NoFolderSelected));
    public static string Wallpapers_NoImageSelected => Get(nameof(Wallpapers_NoImageSelected));
    public static string Wallpapers_SectionDisplay => Get(nameof(Wallpapers_SectionDisplay));
    public static string Wallpapers_SectionSource => Get(nameof(Wallpapers_SectionSource));
    public static string Wallpapers_ShuffleDesc => Get(nameof(Wallpapers_ShuffleDesc));
    public static string Wallpapers_ShuffleTitle => Get(nameof(Wallpapers_ShuffleTitle));
    public static string Wallpapers_SlideshowDesc => Get(nameof(Wallpapers_SlideshowDesc));
    public static string Wallpapers_SlideshowTitle => Get(nameof(Wallpapers_SlideshowTitle));
    public static string Wallpapers_Title => Get(nameof(Wallpapers_Title));
    public static string Wallpapers_UnifiedDesc => Get(nameof(Wallpapers_UnifiedDesc));
    public static string Wallpapers_UnifiedTitle => Get(nameof(Wallpapers_UnifiedTitle));
}
