using System.Windows;
using System.Windows.Controls;
using Muralis.ViewModels;

namespace Muralis.Views.Pages;

/// <summary>Page « Paramètres » : réglages de l'application (démarrage Windows, thème).</summary>
public partial class AppSettingsPage : Page
{
    public AppSettingsPage()
    {
        InitializeComponent();
    }

    /// <summary>L'état « démarrage Windows » peut changer hors de l'app : relire le registre
    /// à chaque affichage de la page.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.AppSettings.Refresh();
    }
}
