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
    private Window? _watchedWindow;

    /// <summary>
    /// Applique <paramref name="preference"/>. <paramref name="window"/> est la fenêtre à
    /// (dé)brancher sur le watcher système — <c>null</c> tant qu'aucune fenêtre n'existe
    /// (démarrage tray seul), le watch sera posé à sa création.
    /// </summary>
    public void Apply(ThemePreference preference, Window? window)
    {
        if (window is not null && !ReferenceEquals(window, _watchedWindow))
        {
            // Fenêtre nouvellement créée : on repart d'un état non observé.
            _watchedWindow = window;
        }

        if (preference == ThemePreference.System)
        {
            ApplicationThemeManager.ApplySystemTheme(updateAccent: true);
            if (_watchedWindow is not null)
                SystemThemeWatcher.Watch(_watchedWindow, WindowBackdropType.Mica, updateAccents: true);
            return;
        }

        if (_watchedWindow is not null)
            SystemThemeWatcher.UnWatch(_watchedWindow);

        var theme = preference == ThemePreference.Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: true);
    }
}
