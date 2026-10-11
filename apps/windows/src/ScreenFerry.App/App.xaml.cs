using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using ScreenFerry.Core;
using ScreenFerry.Windows;
using Forms = System.Windows.Forms;

namespace ScreenFerry.App;

[SuppressMessage("Reliability", "CA1001", Justification = "WPF Application has no Dispose; everything is disposed in OnExit.")]
public partial class App : Application
{
    private Forms.NotifyIcon? _trayIcon;
    private Agent? _agent;
    private Controller? _controller;
    private int _hotkeyCount = -1;
    private HudWindow? _hud;
    private HandoffStatus? _shownHandoff;
    private Timer? _monitorTimer;
    private MainWindow? _window;
    private bool _promptShown;
    private Updater? _updater;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var updatedTo = AfterUpdate(e.Args);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open ScreenFerry", null, (_, _) => ShowWindow());
        var startWithWindows = new Forms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        menu.Opening += (_, _) => startWithWindows.Checked = Environment.ProcessPath is { } path && Autostart.IsEnabled(path);
        startWithWindows.Click += (_, _) => SetAutostart(startWithWindows.Checked);
        menu.Items.Add(startWithWindows);
        menu.Items.Add("Check for Updates", null, (_, _) => _ = _updater?.CheckAsync(manual: true));
        menu.Items.Add("Quit ScreenFerry", null, (_, _) => Shutdown());
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "ScreenFerry",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                ShowWindow();
            }
        };

        string? startError = null;
        try
        {
            var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var discovery = new DnsSdDiscovery();
            _agent = new Agent(WindowsAgentIdentity.LoadOrCreate(), Environment.MachineName, version.Split('+')[0],
                PairedPeerStore.Standard(), discovery);
            _controller = new Controller(_agent);
            _agent.Start();
            _agent.Changed += () => Dispatcher.BeginInvoke(OnAgentChanged);
            _controller.Changed += () => Dispatcher.BeginInvoke(OnAgentChanged);
            _monitorTimer = new Timer(_ => _controller.Refresh(), null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
            ComponentDispatcher.ThreadFilterMessage += OnThreadMessage;
            _window = new MainWindow(_agent, _controller, () => discovery.Error);
            var agent = _agent;
            _updater = new Updater(
                () => agent.CurrentHandoff is { IsFinished: false },
                message => Dispatcher.BeginInvoke(() => _trayIcon?.ShowBalloonTip(5000, "ScreenFerry", message, Forms.ToolTipIcon.Info)),
                () => Dispatcher.BeginInvoke(() => Shutdown()));
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or System.Net.Sockets.SocketException or System.IO.IOException or UnauthorizedAccessException)
        {
            startError = ex.Message;
        }
        _window ??= new MainWindow(null, null, () => startError);
        if (updatedTo is not null)
        {
            _trayIcon.ShowBalloonTip(5000, "ScreenFerry", $"Updated to {updatedTo}.", Forms.ToolTipIcon.Info);
        }
    }

    /// <summary>After a self-update: waits for the old process to exit and removes its exe. Returns the new version.</summary>
    private static string? AfterUpdate(string[] args)
    {
        var updated = Array.IndexOf(args, "--updated") is var u and >= 0 && u + 1 < args.Length ? args[u + 1] : null;
        if (Array.IndexOf(args, "--after") is var a and >= 0 && a + 1 < args.Length && int.TryParse(args[a + 1], out var pid))
        {
            try
            {
                using var old = System.Diagnostics.Process.GetProcessById(pid);
                old.WaitForExit(TimeSpan.FromSeconds(15));
            }
            catch (ArgumentException)
            {
            }
        }
        Updater.CleanUp();
        return updated;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        GlobalHotKeys.Unregister();
        _updater?.Dispose();
        _monitorTimer?.Dispose();
        _agent?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private void SetAutostart(bool enabled)
    {
        if (Environment.ProcessPath is not { } path)
        {
            return;
        }
        try
        {
            Autostart.Set(enabled, path);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            _trayIcon?.ShowBalloonTip(5000, "ScreenFerry", $"Couldn't change Start with Windows: {e.Message}", Forms.ToolTipIcon.Warning);
        }
    }

    private void ShowWindow()
    {
        _window?.Render();
        _window?.Show();
        _window?.Activate();
    }

    /// <summary>Shows a handoff's start, progress and outcome in the HUD, on every computer involved.</summary>
    private void ShowProgress()
    {
        if (_agent?.CurrentHandoff is not { } status || _controller is null || Same(status, _shownHandoff))
        {
            if (_controller?.TakeHotkeyError() is { } error)
            {
                Hud().Show(error.Scene, error.Message, HudWindow.Kind.Failed);
            }
            return;
        }
        var previous = _shownHandoff?.Seq == status.Seq ? _shownHandoff : null;
        _shownHandoff = status;
        if (previous?.IsFinished == true)
        {
            return;
        }
        var computers = _controller.Computers.ToDictionary(c => c.Id, c => c.Name);
        var title = string.Join(", ", status.Owners.OrderBy(o => o.Key, StringComparer.Ordinal)
            .Select(o => $"{_controller.MonitorName(o.Key)} → {computers.GetValueOrDefault(o.Value, "?")}"));
        if (status.Failures.Count > 0)
        {
            Hud().Show("Couldn't move monitors", string.Join("\n", status.Failures.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Value)), HudWindow.Kind.Failed);
        }
        else if (status.Pending.Count == 0)
        {
            Hud().Show(previous is null ? "Already in place" : title, previous is null ? title : null, HudWindow.Kind.Done);
        }
        else if (status.IsExpired)
        {
            Hud().Show("Didn't finish", title, HudWindow.Kind.Failed);
        }
        else if (previous is null)
        {
            Hud().Show(title, "Moving… a monitor without DDC/CI may need up to 25 s.", HudWindow.Kind.Progress);
        }
    }

    private static bool Same(HandoffStatus a, HandoffStatus? b) =>
        b is not null && a.Seq == b.Seq && a.IsExpired == b.IsExpired
        && a.Pending.SetEquals(b.Pending) && a.Failures.Count == b.Failures.Count;

    private HudWindow Hud() => _hud ??= new HudWindow();

    private void OnThreadMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message == GlobalHotKeys.WmHotKey && _controller is not null)
        {
            _controller.ActivateHotkey((int)msg.wParam);
            handled = true;
        }
    }

    private void OnAgentChanged()
    {
        _window?.Render();
        ShowProgress();
        var hotkeys = _controller?.Scenes().Count ?? 0;
        if (hotkeys != _hotkeyCount)
        {
            GlobalHotKeys.Register(hotkeys);
            _hotkeyCount = hotkeys;
        }
        var prompt = _agent?.PairingPrompt;
        if (prompt is not null && !_promptShown)
        {
            ShowWindow();
            _trayIcon?.ShowBalloonTip(5000, "ScreenFerry", $"Pair with {prompt.PeerName}? Compare the codes.", Forms.ToolTipIcon.Info);
        }
        _promptShown = prompt is not null;
    }
}
