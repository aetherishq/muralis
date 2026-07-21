using System.Windows;
using Muralis.ViewModels;
using Wpf.Ui.Abstractions;

namespace Muralis.Views;

/// <summary>
/// Fournit les pages au <see cref="Wpf.Ui.Controls.NavigationView"/> (pas de conteneur DI, cf.
/// AGENTS.md) : une instance unique par type, avec le <see cref="SettingsViewModel"/> en
/// DataContext — l'état visuel (scroll, onglet actif) survit ainsi aux changements de page.
/// </summary>
public sealed class NavigationPageProvider(SettingsViewModel viewModel) : INavigationViewPageProvider
{
    private readonly Dictionary<Type, object> _pages = [];

    public object? GetPage(Type pageType)
    {
        if (!_pages.TryGetValue(pageType, out var page))
        {
            page = Activator.CreateInstance(pageType)
                ?? throw new InvalidOperationException($"Impossible de créer la page {pageType.Name}.");
            if (page is FrameworkElement element)
                element.DataContext = viewModel;
            _pages[pageType] = page;
        }

        return page;
    }
}
