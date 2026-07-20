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

        // Plafonne la hauteur auto (SizeToContent) à la zone de travail : au-delà (beaucoup
        // d'écrans), la fenêtre ne déborde pas et le ScrollViewer reprend le relais.
        MaxHeight = SystemParameters.WorkArea.Height;
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
