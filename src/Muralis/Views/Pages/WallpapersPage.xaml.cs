using System.Windows.Controls;
using Muralis.ViewModels;

namespace Muralis.Views.Pages;

/// <summary>Page « Fonds d'écran » : configuration des cibles (par écran ou unifiée).</summary>
public partial class WallpapersPage : Page
{
    public WallpapersPage()
    {
        InitializeComponent();

        // La barre « Appliquer » vit dans la fenêtre (toujours visible, hors du scroll de page) :
        // on lui signale quand cette page est celle affichée.
        IsVisibleChanged += (_, e) =>
        {
            if (DataContext is SettingsViewModel vm)
                vm.IsWallpapersPageActive = (bool)e.NewValue;
        };
    }
}
