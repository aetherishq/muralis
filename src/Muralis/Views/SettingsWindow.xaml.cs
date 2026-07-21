using System.ComponentModel;
using System.Windows;
using Muralis.ViewModels;
using Muralis.Views.Pages;
using Wpf.Ui.Controls;

namespace Muralis.Views;

/// <summary>
/// Fenêtre de paramètres : coquille NavigationView (pane gauche) hébergeant les trois pages.
/// Ne se ferme jamais : sur <c>Closing</c> elle se masque, pour que l'app continue de vivre
/// dans le tray (elle est ré-affichée depuis l'icône).
/// </summary>
public partial class SettingsWindow : FluentWindow
{
    private bool _navigated;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        RootNavigation.SetPageProviderService(new NavigationPageProvider(viewModel));
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Page initiale — une seule fois : la fenêtre se masque/ré-affiche (re-Loaded)
        // et l'utilisateur doit retrouver la page où il était.
        if (_navigated)
            return;
        _navigated = true;
        RootNavigation.Navigate(typeof(WallpapersPage));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Arrêt volontaire (« Quitter ») ou reconstruction de l'UI (changement de langue) :
        // laisser la fenêtre se fermer pour de bon.
        if (Application.Current is App app && (app.IsExiting || app.IsRecreatingUi))
        {
            base.OnClosing(e);
            return;
        }

        // Fermeture par l'utilisateur (bouton X) : masquer, l'app reste dans le tray.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
