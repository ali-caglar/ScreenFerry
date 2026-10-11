using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ScreenFerry.Core;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace ScreenFerry.App;

/// <summary>The window behind the tray icon, rebuilt from the agent's state on every change.</summary>
public sealed class MainWindow : Window
{
    private readonly Agent? _agent;
    private readonly Func<string?> _error;
    private readonly StackPanel _content = new() { Margin = new Thickness(16) };

    public MainWindow(Agent? agent, Func<string?> error)
    {
        _agent = agent;
        _error = error;
        Title = "ScreenFerry";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = _content;
        Render();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    public void Render()
    {
        _content.Children.Clear();
        Add(new TextBlock { Text = "ScreenFerry", FontSize = 16, FontWeight = FontWeights.SemiBold });
        if (_agent is null)
        {
            Add(Caption($"ScreenFerry couldn't start: {_error()}", Brushes.Firebrick));
            return;
        }
        Add(Caption(_agent.Name));

        if (_agent.PairingPrompt is { } prompt)
        {
            RenderPrompt(prompt);
        }

        var paired = _agent.Peers.Where(p => p.IsPaired).ToList();
        if (paired.Count > 0)
        {
            Add(Heading("Computers"));
            foreach (var peer in paired)
            {
                RenderPeer(peer);
            }
        }

        if (_agent.IsPairingMode)
        {
            RenderPairingMode();
        }
        else if (_agent.PairingPrompt is null)
        {
            Add(Button("Add Computer…", () => _agent.SetPairingMode(true)));
        }

        if (_agent.PairingResult is { } result)
        {
            Add(Caption(result));
        }
        if (_error() is { } error)
        {
            Add(Caption($"Network: {error}", Brushes.Firebrick));
        }
    }

    private void RenderPrompt(PairingPrompt prompt)
    {
        var panel = new StackPanel();
        panel.Children.Add(Heading($"Pair with {prompt.PeerName}?"));
        panel.Children.Add(Caption($"Check that {prompt.PeerName} shows the same code."));
        panel.Children.Add(new TextBlock
        {
            Text = $"{prompt.Code[..3]} {prompt.Code[3..]}",
            FontSize = 30,
            FontFamily = new FontFamily("Consolas"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8),
        });
        var buttons = new DockPanel();
        var differ = Button("Codes Differ", () => _agent!.ConfirmPairing(false));
        DockPanel.SetDock(differ, Dock.Left);
        var match = Button("Codes Match", () => _agent!.ConfirmPairing(true));
        match.IsDefault = true;
        match.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Children.Add(differ);
        buttons.Children.Add(match);
        panel.Children.Add(buttons);
        Add(new Border
        {
            Child = panel,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 10, 0, 0),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0x78, 0xD4)),
        });
    }

    private void RenderPeer(AgentPeer peer)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0), Fill = peer.IsConnected ? Brushes.SeaGreen : Brushes.LightGray };
        DockPanel.SetDock(dot, Dock.Left);
        var status = Caption(peer.IsConnected ? "Connected" : "Offline");
        DockPanel.SetDock(status, Dock.Right);
        row.Children.Add(dot);
        row.Children.Add(status);
        row.Children.Add(new TextBlock { Text = peer.Name });
        var forget = new MenuItem { Header = $"Forget {peer.Name}" };
        forget.Click += (_, _) => _agent!.Unpair(peer.Id);
        row.ContextMenu = new ContextMenu { Items = { forget } };
        Add(row);
        foreach (var monitor in peer.Monitors)
        {
            var line = new DockPanel { Margin = new Thickness(14, 2, 0, 0) };
            var state = Caption(monitor.Attached ? "in use" : "released");
            DockPanel.SetDock(state, Dock.Right);
            line.Children.Add(state);
            line.Children.Add(new TextBlock { Text = monitor.Name ?? monitor.Identity, FontSize = 12 });
            Add(line);
        }
    }

    private void RenderPairingMode()
    {
        Add(Heading("Add Computer"));
        Add(Caption("Open “Add Computer” in ScreenFerry on the other computer too."));
        var pairable = _agent!.Peers.Where(p => !p.IsPaired && p.IsPairable).ToList();
        if (pairable.Count == 0)
        {
            Add(Caption("Looking for computers…"));
        }
        foreach (var peer in pairable)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var pair = Button("Pair", () => _ = _agent.PairAsync(peer.Id));
            DockPanel.SetDock(pair, Dock.Right);
            row.Children.Add(pair);
            row.Children.Add(new TextBlock { Text = peer.Name, VerticalAlignment = VerticalAlignment.Center });
            Add(row);
        }
        Add(Button("Cancel", () => _agent.SetPairingMode(false)));
    }

    private void Add(UIElement element) => _content.Children.Add(element);

    private static TextBlock Heading(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 2) };

    private static TextBlock Caption(string text, Brush? brush = null) =>
        new() { Text = text, FontSize = 11, Foreground = brush ?? Brushes.Gray, TextWrapping = TextWrapping.Wrap };

    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => action();
        return button;
    }
}
