using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Muralis.Services;
using Muralis.ViewModels;
using Muralis.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Muralis;

/// <summary>
/// Point d'entrée de l'application. App tray-first : aucune fenêtre principale n'est montrée au
/// démarrage par défaut. Le process ne se termine que sur « Quitter » (ShutdownMode=OnExplicitShutdown).
/// </summary>
public partial class App : Application
{
    private TaskbarIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private SettingsViewModel? _settingsViewModel;

    /// <summary>
    /// Vrai pendant l'arrêt volontaire (menu « Quitter »). Permet à la fenêtre de settings de se
    /// fermer réellement au lieu de se masquer, pour que le process se termine.
    /// </summary>
    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Thème Fluent (Mica, sombre).
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica);

        // Injection manuelle des services (pas de conteneur DI pour une V1, cf. AGENTS.md).
        var configService = new ConfigService();
        var screenService = new ScreenService();
        var composer = new WallpaperComposer(configService);
        var wallpaperService = new WallpaperService(composer);
        _settingsViewModel = new SettingsViewModel(configService, screenService, wallpaperService);

        _tray = CreateTrayIcon(configService.DataDirectory);

        // Sans --minimized (double-clic sur le raccourci) : ouvrir directement les paramètres.
        bool minimized = e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        if (!minimized)
            ShowSettings();
    }

    private TaskbarIcon CreateTrayIcon(string dataDirectory)
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = "Paramètres…" };
        openItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(openItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Quitter" };
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        var tray = new TaskbarIcon
        {
            ToolTipText = "Muralis",
            IconSource = CreateTrayIconSource(dataDirectory),
            ContextMenu = menu,
        };
        tray.TrayMouseDoubleClick += (_, _) => ShowSettings();
        tray.ForceCreate();
        return tray;
    }

    private void ShowSettings()
    {
        _settingsWindow ??= new SettingsWindow(_settingsViewModel!);
        _settingsWindow.Show();
        _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    /// <summary>Arrêt volontaire : dispose le tray puis ferme réellement l'app.</summary>
    private void ExitApp()
    {
        IsExiting = true;
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Icône de tray générée à la volée (carré accentué + « M »), écrite en PNG dans le dossier de
    /// données puis chargée par URI. H.NotifyIcon ne convertit pas un <see cref="RenderTargetBitmap"/>
    /// directement : il faut un <see cref="BitmapImage"/> adossé à un fichier. Remplaçable par une
    /// vraie icône plus tard.
    /// </summary>
    private static ImageSource CreateTrayIconSource(string dataDirectory)
    {
        const int size = 32;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var background = new SolidColorBrush(Color.FromRgb(0x2C, 0x7E, 0xF8));
            dc.DrawRoundedRectangle(background, null, new Rect(0, 0, size, size), 6, 6);

            var text = new FormattedText(
                "M",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                20,
                Brushes.White,
                1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();

        // Encoder le rendu en PNG…
        byte[] pngBytes;
        using (var ms = new MemoryStream())
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            encoder.Save(ms);
            pngBytes = ms.ToArray();
        }

        // …puis l'emballer dans un vrai fichier .ico (PNG encapsulé, format Vista+),
        // car H.NotifyIcon reconstruit une System.Drawing.Icon à partir des octets du fichier.
        Directory.CreateDirectory(dataDirectory);
        string iconPath = Path.Combine(dataDirectory, "tray.ico");
        using (var fs = new FileStream(iconPath, FileMode.Create, FileAccess.Write))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0);            // ICONDIR.reserved
            w.Write((short)1);            // ICONDIR.type = icône
            w.Write((short)1);            // ICONDIR.count
            w.Write((byte)size);          // largeur
            w.Write((byte)size);          // hauteur
            w.Write((byte)0);             // nb couleurs (0 = >256)
            w.Write((byte)0);             // réservé
            w.Write((short)1);            // plans
            w.Write((short)32);           // bits/pixel
            w.Write(pngBytes.Length);     // taille des données image
            w.Write(6 + 16);              // offset des données (en-tête + 1 entrée)
            w.Write(pngBytes);
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(iconPath, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
