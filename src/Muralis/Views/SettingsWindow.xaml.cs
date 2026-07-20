using System.ComponentModel;
using System.Windows;
using Muralis.ViewModels;
using Wpf.Ui.Controls;

namespace Muralis.Views;

/// <summary>
/// Fenêtre de paramètres. Ne se ferme jamais : sur <c>Closing</c> elle se masque, pour que
/// l'app continue de vivre dans le tray (elle est ré-affichée depuis l'icône).
/// </summary>
public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Ouvre le menu de navigation (le ContextMenu du bouton hamburger) au clic gauche.</summary>
    private void OnHamburgerClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } element)
        {
            menu.PlacementTarget = element;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Arrêt volontaire (menu « Quitter ») : laisser la fenêtre se fermer pour de bon.
        if (Application.Current is App { IsExiting: true })
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
