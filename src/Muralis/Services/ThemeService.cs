using System.Windows;
using Muralis.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Muralis.Services;

/// <summary>
/// Applique la préférence de thème : <see cref="ThemePreference.System"/> suit le réglage
/// clair/sombre de Windows en direct (via <see cref="SystemThemeWatcher"/> sur la fenêtre),
/// sinon le thème est forcé. Le backdrop Mica est conservé dans tous les cas.
/// </summary>
public class ThemeService
{
    /// <summary>Fenêtre actuellement branchée sur le watcher système (null : aucune).</summary>
    private Window? _watchedWindow;

    /// <summary>
    /// Applique <paramref name="preference"/>. <paramref name="window"/> est la fenêtre à
    /// (dé)brancher sur le watcher système — <c>null</c> tant qu'aucune fenêtre n'existe
    /// (démarrage tray seul), le watch sera posé à sa création.
    /// </summary>
    public void Apply(ThemePreference preference, Window? window)
    {
        var target = window ?? _watchedWindow;

        if (preference == ThemePreference.System)
        {
            ApplicationThemeManager.ApplySystemTheme(updateAccent: true);
            if (target is not null && !ReferenceEquals(target, _watchedWindow))
            {
                // Watch accepte une fenêtre pas encore chargée (hook posé à son Loaded).
                SystemThemeWatcher.Watch(target, WindowBackdropType.Mica, updateAccents: true);
                _watchedWindow = target;
            }
            return;
        }

        // Thème forcé : débrancher le watcher éventuel. UnWatch exige une fenêtre chargée
        // (WPF-UI lève InvalidOperationException sinon — crash au démarrage avec un thème
        // forcé persisté) ; une fenêtre jamais chargée n'a de toute façon aucun hook actif.
        if (_watchedWindow is { IsLoaded: true })
            SystemThemeWatcher.UnWatch(_watchedWindow);
        _watchedWindow = null;

        var theme = preference == ThemePreference.Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: true);
    }
}
