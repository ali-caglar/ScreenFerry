using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace ScreenFerry.App;

[SuppressMessage("Reliability", "CA1001", Justification = "WPF Application has no Dispose; the icon is disposed in OnExit.")]
public partial class App : Application
{
    private Forms.NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Quit ScreenFerry", null, (_, _) => Shutdown());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "ScreenFerry",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
