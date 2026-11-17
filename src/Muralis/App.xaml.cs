using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Muralis.Models;
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
    private WindowsDesktopBackend? _desktop;
    private DpapiSecretProtector? _secrets;
    private WallpaperService? _wallpaperService;
    private ImageSaveService? _imageSaveService;

    /// <summary>Disposition d'écrans sur laquelle tournent les fonds (#42).</summary>
    private IReadOnlyList<MonitorInfo> _monitors = [];

    /// <summary>Écrans présents au démarrage : seuls ceux branchés ensuite reçoivent le fond
    /// Windows par défaut s'ils sont inconnus (au démarrage, Windows garde leur fond).</summary>
    private HashSet<string> _startupDeviceIds = [];

    private DispatcherTimer? _displayDebounce;

    /// <summary>Une dock émet une rafale d'événements pendant que Windows réorganise les
    /// écrans : on attend le calme avant de ré-énumérer.</summary>
    private static readonly TimeSpan DisplayDebounceDelay = TimeSpan.FromSeconds(2);

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

        // Journal + filets globaux en premier : tout ce qui suit peut lever (issue #46).
        Log.Initialize(Path.Combine(configService.DataDirectory, "logs"));
        Log.Info($"Muralis {typeof(App).Assembly.GetName().Version} starting (args: {string.Join(' ', e.Args)})");
        RegisterGlobalExceptionHandlers();
        // Implémentations Windows des abstractions de Muralis.Core (#52).
        _desktop = new WindowsDesktopBackend();
        var secrets = new DpapiSecretProtector();
        _secrets = secrets;
        var composer = new WallpaperComposer(configService);
        _wallpaperService = new WallpaperService(composer, _desktop);

        // Client HTTP partagé des sources web. User-Agent explicite : certaines API
        // (e621 notamment) rejettent les clients sans identification.
        // Version réelle + URL du projet (review s3). Connexions renouvelées toutes les 15 min :
        // une app qui tourne des semaines suit les changements DNS des API (review s6).
        var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        var version = typeof(App).Assembly.GetName().Version;
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Muralis/{version?.ToString(3) ?? "1.0"} (+https://github.com/aetherishq/muralis)");
        var fetcher = new WebWallpaperFetcher(http, configService, secrets);

        // Réglage lu à chaque pose prévue : pris en compte à chaud (#41). Ticks des
        // diaporamas postés sur l'UI thread (appels COM, événements vers l'UI).
        _slideshowService = new SlideshowService(_wallpaperService, fetcher,
            shouldDefer: () => configService.Load().PauseDuringFullscreen && FullscreenDetector.IsFullscreenAppInForeground(),
            TimeProvider.System,
            new DispatcherSynchronizationContext(Dispatcher));
        _imageSaveService = new ImageSaveService(configService, _slideshowService, fetcher);
        var cacheMaintenance = new CacheMaintenanceService(configService, _wallpaperService);
        _themeService = new ThemeService();

        // Migrations de config (une fois, avant toute lecture par les VMs/services).
        var config = configService.Load();
        bool migrated = ConfigMigrations.Apply(config, secrets);
        if (secrets.UpgradeLegacyBlobs(config) | migrated)
            configService.Save(config);

        // Langue AVANT toute construction d'UI (les libellés sont capturés à la création).
        // Premier lancement : la langue choisie dans l'installeur amorce la config.
        if (config.Language is null && LocalizationService.ReadInstallerSeed() is { } seeded)
        {
            config.Language = seeded;
            configService.Save(config);
        }
        LocalizationService.Apply(config.Language);

        // Thème depuis la config (le watcher système sera branché à la création de la fenêtre).
        _themeService.Apply(config.Theme, window: null);

        // Entretien des caches : reliquats d'anciennes versions + plafond au démarrage,
        // puis passe de plafond throttlée après chaque pose de fond.
        var startupMonitors = _desktop.GetMonitors();
        cacheMaintenance.CleanupAtStartup(configService.Load(), startupMonitors);
        _slideshowService.WallpaperApplied += () =>
            cacheMaintenance.PruneComposedIfDue(_desktop!.GetMonitors);

        BuildViewModels();
        _tray = CreateTrayIcon();

        // Reprend les diaporamas persistés (les fonds fixes, eux, sont conservés par Windows,
        // sauf pour un écran repris par identifiant matériel : posé ici).
        _monitors = startupMonitors;
        _startupDeviceIds = startupMonitors.Select(m => m.DeviceId).ToHashSet();
        var runtimeConfig = configService.Load();
        var matched = ScreenMatcher.CompleteForRuntime(runtimeConfig, startupMonitors, _ => null).ToHashSet();
        if (matched.Count > 0)
            _ = _wallpaperService.ApplyAsync(runtimeConfig, startupMonitors.Where(m => matched.Contains(m.DeviceId)).ToList());
        _slideshowService.Restart(runtimeConfig, startupMonitors);

        // Dock/undock, écran ajouté/retiré, résolution, sortie de veille (#42). Événements
        // statiques : désabonnés à la sortie.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Sans --minimized (double-clic sur le raccourci) : ouvrir directement les paramètres.
        bool minimized = e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        if (!minimized)
            ShowSettings();
    }

    /// <summary>
    /// Filets de sécurité : toute exception non gérée est journalisée. Sur l'UI thread, elle
    /// est marquée gérée — une app tray qui meurt sans bruit (le fond cesse simplement de
    /// changer) est pire qu'une action ratée ; la cause est dans le journal.
    /// </summary>
    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled exception on the UI thread", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error($"Unhandled exception (terminating: {args.IsTerminating})", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    /// <summary>(Re)construit le graphe de ViewModels — leurs libellés capturent la culture courante.</summary>
    private void BuildViewModels()
    {
        // L'ancien graphe (bascule de langue) se désabonne des services, qui survivent (#49).
        _settingsViewModel?.Dispose();
        var appSettings = new AppSettingsViewModel(_configService!, _themeService!, _secrets!, () => _settingsWindow, ApplyLanguage);
        var sources = new SourcesViewModel(_configService!);
        _settingsViewModel = new SettingsViewModel(_configService!, _desktop!, _wallpaperService!, _slideshowService!, _imageSaveService!, appSettings, sources);
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
            _tray = CreateTrayIcon();
            ShowSettings();
        });
    }

    private TaskbarIcon CreateTrayIcon()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = Strings.Tray_Settings };
        openItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(openItem);

        var nextItem = new System.Windows.Controls.MenuItem { Header = Strings.Tray_NextImage };
        nextItem.Click += (_, _) => _slideshowService?.AdvanceAll();
        menu.Items.Add(nextItem);

        // Sous-menu reconstruit à chaque ouverture : les cibles web (et leurs images
        // posées) changent au fil des Appliquer. Grisé quand rien n'est enregistrable.
        var saveItem = new System.Windows.Controls.MenuItem { Header = Strings.Tray_SaveCurrent };
        menu.Items.Add(saveItem);
        menu.Opened += (_, _) => RebuildSaveMenu(saveItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = Strings.Tray_Quit };
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        var tray = new TaskbarIcon
        {
            ToolTipText = "Muralis",
            // Icône embarquée en ressource WPF : H.NotifyIcon relit les octets du .ico via
            // l'URI pack et en tire une System.Drawing.Icon à la taille voulue.
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/muralis.ico")),
            ContextMenu = menu,
        };
        tray.TrayMouseDoubleClick += (_, _) => ShowSettings();
        tray.ForceCreate();
        return tray;
    }

    /// <summary>Sous-menu « Enregistrer le fond actuel » : une entrée par écran affichant
    /// une image web (« Tous les écrans » pour la cible unifiée).</summary>
    private void RebuildSaveMenu(System.Windows.Controls.MenuItem saveItem)
    {
        saveItem.Items.Clear();
        var targets = _slideshowService?.CurrentWebImages() ?? [];
        saveItem.IsEnabled = targets.Count > 0;

        var monitors = _desktop?.GetMonitors() ?? [];
        foreach (var (deviceId, _) in targets)
        {
            int index = -1;
            for (int i = 0; i < monitors.Count; i++)
            {
                if (monitors[i].DeviceId == deviceId)
                {
                    index = i;
                    break;
                }
            }

            var item = new System.Windows.Controls.MenuItem
            {
                Header = deviceId.Length == 0 || index < 0
                    ? Strings.Screen_AllScreens
                    : string.Format(Strings.Screen_LabelFormat, index + 1),
            };
            string captured = deviceId;
            item.Click += (_, _) => SaveCurrentWallpaper(captured);
            saveItem.Items.Add(item);
        }
    }

    /// <summary>Enregistre le fond web d'un écran et le signale par notification tray.</summary>
    private void SaveCurrentWallpaper(string deviceId)
    {
        string message;
        try
        {
            message = _imageSaveService!.SaveCurrent(deviceId) switch
            {
                (SaveOutcome.Saved, var name) => string.Format(Strings.Status_ImageSavedFormat, name),
                (SaveOutcome.AlreadySaved, var name) => string.Format(Strings.Status_ImageAlreadySavedFormat, name),
                _ => Strings.Status_NothingToSave,
            };
        }
        catch (Exception ex)
        {
            Log.Warn($"Save current wallpaper failed (screen '{deviceId}')", ex);
            message = string.Format(Strings.Status_FailedFormat, ex.Message);
        }
        _tray?.ShowNotification("Muralis", message);
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
        Log.Info("Exiting (tray menu)");
        IsExiting = true;
        _slideshowService?.Stop();
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => ScheduleDisplayCheck();

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // Le laptop est souvent dédocké pendant la veille.
        if (e.Mode == PowerModes.Resume)
            ScheduleDisplayCheck();
    }

    /// <summary>(Re)lance le debounce. SystemEvents lève sur son propre thread.</summary>
    private void ScheduleDisplayCheck() => Dispatcher.BeginInvoke(() =>
    {
        if (_displayDebounce is null)
        {
            _displayDebounce = new DispatcherTimer { Interval = DisplayDebounceDelay };
            _displayDebounce.Tick += (_, _) =>
            {
                _displayDebounce.Stop();
                _ = OnDisplayLayoutMaybeChangedAsync();
            };
        }
        _displayDebounce.Stop();
        _displayDebounce.Start();
    });

    /// <summary>
    /// Ré-énumère les écrans ; si la disposition a changé, re-pose les fonds fixes (composés
    /// à la nouvelle résolution) et relance les diaporamas sur la nouvelle liste.
    /// </summary>
    private async Task OnDisplayLayoutMaybeChangedAsync()
    {
        try
        {
            var monitors = _desktop!.GetMonitors();
            if (monitors.Count == 0)
            {
                Log.Warn("Display change: no active monitor reported, ignored");
                return;
            }
            if (ScreenMatcher.SameLayout(monitors, _monitors))
                return;

            Log.Info($"Display layout changed: {_monitors.Count} -> {monitors.Count} monitor(s)");
            _monitors = monitors;

            var config = _configService!.Load();
            ScreenMatcher.CompleteForRuntime(config, monitors,
                m => _startupDeviceIds.Contains(m.DeviceId) ? null : ScreenMatcher.WindowsDefaultWallpaper(m));
            var report = await _wallpaperService!.ApplyAsync(config, monitors);
            _slideshowService!.Restart(config, monitors);
            if (report.Failed > 0)
                Log.Warn($"Display change: {report.Failed} wallpaper(s) could not be applied");

            // Le sélecteur d'écrans et « Appliquer » doivent viser la nouvelle disposition.
            _settingsViewModel?.Load();
        }
        catch (Exception ex)
        {
            Log.Error("Display change handling failed", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _tray?.Dispose();
        base.OnExit(e);
    }
}
