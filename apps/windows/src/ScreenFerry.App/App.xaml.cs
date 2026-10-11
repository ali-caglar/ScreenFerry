using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Reflection;
using System.Windows;
using ScreenFerry.Core;
using ScreenFerry.Windows;
using Forms = System.Windows.Forms;

namespace ScreenFerry.App;

[SuppressMessage("Reliability", "CA1001", Justification = "WPF Application has no Dispose; everything is disposed in OnExit.")]
public partial class App : Application
{
    private Forms.NotifyIcon? _trayIcon;
    private Agent? _agent;
    private Timer? _monitorTimer;
    private MainWindow? _window;
    private bool _promptShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open ScreenFerry", null, (_, _) => ShowWindow());
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
            _agent.Start();
            _agent.Changed += () => Dispatcher.BeginInvoke(OnAgentChanged);
            _monitorTimer = new Timer(_ => _agent.SetLocalMonitors(LocalMonitors.Current()), null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
            _window = new MainWindow(_agent, () => discovery.Error);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or System.Net.Sockets.SocketException or System.IO.IOException or UnauthorizedAccessException)
        {
            startError = ex.Message;
        }
        _window ??= new MainWindow(null, () => startError);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitorTimer?.Dispose();
        _agent?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private void ShowWindow()
    {
        _window?.Render();
        _window?.Show();
        _window?.Activate();
    }

    private void OnAgentChanged()
    {
        _window?.Render();
        var prompt = _agent?.PairingPrompt;
        if (prompt is not null && !_promptShown)
        {
            ShowWindow();
            _trayIcon?.ShowBalloonTip(5000, "ScreenFerry", $"Pair with {prompt.PeerName}? Compare the codes.", Forms.ToolTipIcon.Info);
        }
        _promptShown = prompt is not null;
    }
}
