using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Muralis.Resources;
using Muralis.Services;
using Muralis.Services.Sources;
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
    private SlideshowService? _slideshowService;
    private ConfigService? _configService;
    private ThemeService? _themeService;
    private ScreenService? _screenService;
    private WallpaperService? _wallpaperService;

    /// <summary>
    /// Vrai pendant l'arrêt volontaire (menu « Quitter »). Permet à la fenêtre de settings de se
    /// fermer réellement au lieu de se masquer, pour que le process se termine.
    /// </summary>
    public bool IsExiting { get; private set; }

    /// <summary>Vrai pendant la reconstruction de l'UI (changement de langue) : la fenêtre de
    /// settings doit alors se fermer réellement, comme pour <see cref="IsExiting"/>.</summary>
    public bool IsRecreatingUi { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Injection manuelle des services (pas de conteneur DI pour une V1, cf. AGENTS.md).
        var configService = new ConfigService();
        _configService = configService;
        _screenService = new ScreenService();
        var composer = new WallpaperComposer(configService);
        _wallpaperService = new WallpaperService(composer);

        // Client HTTP partagé des sources web. User-Agent explicite : certaines API
        // (e621 notamment) rejettent les clients sans identification.
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Muralis/1.0");
        var fetcher = new WebWallpaperFetcher(http, configService);

        _slideshowService = new SlideshowService(_wallpaperService, fetcher);
        _themeService = new ThemeService();

        // Langue AVANT toute construction d'UI (les libellés sont capturés à la création).
        // Premier lancement : la langue choisie dans l'installeur amorce la config.
        var config = configService.Load();
        if (config.Language is null && LocalizationService.ReadInstallerSeed() is { } seeded)
        {
            config.Language = seeded;
            configService.Save(config);
        }
        LocalizationService.Apply(config.Language);

        // Thème depuis la config (le watcher système sera branché à la création de la fenêtre).
        _themeService.Apply(config.Theme, window: null);

        BuildViewModels();
        _tray = CreateTrayIcon(configService.DataDirectory);

        // Reprend les diaporamas persistés (les fonds fixes, eux, sont conservés par Windows).
        _slideshowService.Restart(configService.Load(), _screenService.GetMonitors());

        // Sans --minimized (double-clic sur le raccourci) : ouvrir directement les paramètres.
        bool minimized = e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        if (!minimized)
            ShowSettings();
    }

    /// <summary>(Re)construit le graphe de ViewModels — leurs libellés capturent la culture courante.</summary>
    private void BuildViewModels()
    {
        var appSettings = new AppSettingsViewModel(_configService!, _themeService!, () => _settingsWindow, ApplyLanguage);
        var sources = new SourcesViewModel(_configService!);
        _settingsViewModel = new SettingsViewModel(_configService!, _screenService!, _wallpaperService!, _slideshowService!, appSettings, sources);
    }

    /// <summary>
    /// Bascule de langue à chaud : persiste le choix, applique la culture puis reconstruit
    /// fenêtre, tray et ViewModels. Les services (diaporamas compris) ne sont pas touchés.
    /// Reconstruction différée au Dispatcher : on est appelé depuis un ComboBox de la
    /// fenêtre qui va être fermée.
    /// </summary>
    private void ApplyLanguage(string? code)
    {
        var config = _configService!.Load();
        config.Language = code;
        _configService.Save(config);
        LocalizationService.Apply(code);

        Dispatcher.BeginInvoke(() =>
        {
            if (_settingsWindow is not null)
            {
                IsRecreatingUi = true;
                _settingsWindow.Close();
                IsRecreatingUi = false;
                _settingsWindow = null;
            }

            _tray?.Dispose();
            BuildViewModels();
            _tray = CreateTrayIcon(_configService!.DataDirectory);
            ShowSettings();
        });
    }

    private TaskbarIcon CreateTrayIcon(string dataDirectory)
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = Strings.Tray_Settings };
        openItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(openItem);

        var nextItem = new System.Windows.Controls.MenuItem { Header = Strings.Tray_NextImage };
        nextItem.Click += (_, _) => _slideshowService?.AdvanceAll();
        menu.Items.Add(nextItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = Strings.Tray_Quit };
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
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settingsViewModel!);
            // Ré-applique le thème une fois la fenêtre CHARGÉE (handle Win32 existant) :
            // appliqué avant, WPF-UI ne met pas à jour le chrome/backdrop de la fenêtre —
            // premier affichage incohérent (ex. ressources claires sur Mica sombre) quand le
            // thème persisté ne suit pas Windows. Branche aussi le watcher si « Système ».
            _settingsWindow.Loaded += (_, _) =>
                _themeService?.Apply(_configService!.Load().Theme, _settingsWindow);
        }
        _settingsWindow.Show();
        _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    /// <summary>Arrêt volontaire : dispose le tray puis ferme réellement l'app.</summary>
    private void ExitApp()
    {
        IsExiting = true;
        _slideshowService?.Stop();
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
