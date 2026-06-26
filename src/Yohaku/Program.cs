using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace Yohaku;

internal static class Program
{
    private const string SourceUrl = "https://github.com/DavidF-Dev/Yohaku";
    private const string ActivateEventName = "Yohaku.SingleInstance.Activate";

    private static AppBarManager? _appbars;
    private static Config _config = new();
    private static NotifyIcon? _tray;
    private static FileSystemWatcher? _watcher;
    private static EventWaitHandle? _activateEvent;

    // Hidden UI-thread window used as a reliable marshaling target for background-thread work (its handle always exists).
    private static Form? _syncRoot;
    private static System.Windows.Forms.Timer? _reloadTimer;

    [STAThread]
    private static void Main()
    {
        // Single-instance guard: two instances would double-reserve the margins.
        using var mutex = new Mutex(initiallyOwned: true, "Yohaku.SingleInstance", out bool isNew);
        if (!isNew)
        {
            Log.Info("Another instance is already running; notifying it and exiting.");
            NotifyRunningInstance();
            return;
        }

        ApplicationConfiguration.Initialize();

        // Log stray exceptions rather than let them silently kill the app and leak appbar reservations.
        Application.ThreadException += (_, e) => Log.Error($"UI thread exception: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error($"Unhandled exception: {e.ExceptionObject}");

        // Realise a hidden sync window before anything else needs to marshal.
        _syncRoot = new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, Opacity = 0 };
        _ = _syncRoot.Handle; // force handle creation without showing

        _config = Config.Load();
        Log.Info($"Loaded config from {Config.ConfigPath}");

        _appbars = new AppBarManager(_config);
        _appbars.Build();

        _reloadTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); ReloadConfig(); };

        SetupTray();
        SetupConfigWatcher();
        SetupActivationListener();
        Startup.SyncPathIfEnabled();

        // Reconcile appbars when monitors are added/removed or resolution/DPI changes.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        Application.ApplicationExit += OnExit;

        Application.Run(); // hidden message loop; drives appbar callbacks & timers
    }

    private static void OnExit(object? sender, EventArgs e)
    {
        // Critical: release every reserved margin, or the desktop work area stays shrunk until Explorer restarts.
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _appbars?.Dispose();
        _watcher?.Dispose();
        _reloadTimer?.Dispose();
        _tray?.Dispose();
        _syncRoot?.Dispose();
        Log.Info("Yohaku exited; appbars removed.");

        // A lingering non-background thread keeps the process alive after exit; hard-terminate now that reservations are released (Environment.Exit deadlocks on STA finalization here).
        Process.GetCurrentProcess().Kill();
    }

    private static void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Log.Info("Display settings changed; reconciling monitors.");
        _appbars?.ScheduleReconcile();
    }

    // ---- System tray ---------------------------------------------------

    private static void SetupTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Yohaku 余白", null, null).Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Edit config…", null, (_, _) => OpenConfig());
        menu.Items.Add("Reload config", null, (_, _) => ReloadConfig());
        // Plain click re-applies in place (smooth); Shift+click forces a full teardown rebuild.
        menu.Items.Add("Rebuild margins", null, (_, _) =>
        {
            if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift) _appbars?.Rebuild();
            else _appbars?.ReapplyMargins();
        });
        menu.Items.Add("Open log folder", null, (_, _) => OpenLogFolder());

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = Startup.IsEnabled(),
        };
        startupItem.Click += (_, _) => ToggleStartup(startupItem);
        menu.Items.Add(startupItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("About Yohaku…", null, (_, _) => ShowAbout());
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());

        _tray = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Yohaku 余白",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => OpenConfig();
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            var asm = typeof(Program).Assembly;
            using var stream = asm.GetManifestResourceStream("Yohaku.Yohaku.ico");
            if (stream != null)
                return new Icon(stream, SystemInformation.SmallIconSize);
        }
        catch (Exception ex) { Log.Warn($"Failed to load tray icon: {ex.Message}"); }
        return SystemIcons.Application;
    }

    private static void OpenConfig()
    {
        try
        {
            if (!File.Exists(Config.ConfigPath)) _config.Save();
            Process.Start(new ProcessStartInfo(Config.ConfigPath) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn($"Could not open config: {ex.Message}"); }
    }

    private static void OpenLogFolder()
    {
        try { Process.Start(new ProcessStartInfo(Config.ConfigDir) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn($"Could not open log folder: {ex.Message}"); }
    }

    private static void ToggleStartup(ToolStripMenuItem item)
    {
        if (item.Checked) Startup.Enable(); else Startup.Disable();
        // Re-sync the tick from the registry so a failed write doesn't leave it lying.
        item.Checked = Startup.IsEnabled();
    }

    private static void ShowAlreadyRunningBalloon()
    {
        Log.Info("Second instance pinged; showing 'already running' balloon.");
        _tray?.ShowBalloonTip(3000, "Yohaku 余白", "Yohaku is already running in the system tray.", ToolTipIcon.Info);
    }

    // Wait on the shared named event; a second instance Sets it to ask us to surface.
    private static void SetupActivationListener()
    {
        try
        {
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            ThreadPool.RegisterWaitForSingleObject(_activateEvent, (_, _) =>
            {
                // Fires on a thread-pool thread; marshal to the UI thread to touch the tray.
                if (_syncRoot is { IsHandleCreated: true })
                    _syncRoot.BeginInvoke(new Action(ShowAlreadyRunningBalloon));
            }, null, Timeout.Infinite, executeOnlyOnce: false);
        }
        catch (Exception ex) { Log.Warn($"Could not set up activation listener: {ex.Message}"); }
    }

    private static void NotifyRunningInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var ev))
            {
                ev.Set();
                ev.Dispose();
            }
        }
        catch (Exception ex) { Log.Warn($"Could not notify running instance: {ex.Message}"); }
    }

    private static void ShowAbout()
    {
        var asm = typeof(Program).Assembly;
        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? asm.GetName().Version?.ToString()
                      ?? "?";
        var copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

        var text = $"Yohaku 余白  v{version}\n" +
                   "Margin around maximised windows.\n\n" +
                   $"{copyright}\nMIT License\n\n" +
                   $"{SourceUrl}\n\nOpen the project page on GitHub?";

        if (MessageBox.Show(text, "About Yohaku", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
            == DialogResult.Yes)
        {
            OpenUrl(SourceUrl);
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn($"Could not open URL {url}: {ex.Message}"); }
    }

    private static void ReloadConfig()
    {
        _config = Config.Load();
        _appbars?.ApplyConfig(_config);
    }

    // ---- Hot reload ----------------------------------------------------

    private static void SetupConfigWatcher()
    {
        try
        {
            Directory.CreateDirectory(Config.ConfigDir);
            _watcher = new FileSystemWatcher(Config.ConfigDir, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            // Changed fires on a thread-pool thread: marshal to the UI thread via the sync window, debounced (editors emit several writes per save).
            _watcher.Changed += (_, _) =>
            {
                try
                {
                    if (_syncRoot is { IsHandleCreated: true })
                        _syncRoot.BeginInvoke(new Action(() => { _reloadTimer?.Stop(); _reloadTimer?.Start(); }));
                }
                catch (Exception ex) { Log.Warn($"Config-change marshal failed: {ex.Message}"); }
            };
        }
        catch (Exception ex)
        {
            Log.Warn($"Config watcher unavailable: {ex.Message}");
        }
    }
}
