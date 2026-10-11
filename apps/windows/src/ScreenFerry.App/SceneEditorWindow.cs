using System.Windows;
using System.Windows.Controls;
using ScreenFerry.Core;

namespace ScreenFerry.App;

/// <summary>Names a scene and picks an owner for each shared monitor.</summary>
internal sealed class SceneEditorWindow : Window
{
    private readonly TextBox _name = new() { Margin = new Thickness(0, 4, 0, 8) };
    private readonly Dictionary<string, ComboBox> _owners = [];
    private readonly Button _save = new() { Content = "Save", IsDefault = true, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0) };

    public SceneEditorWindow(IReadOnlyList<KnownMonitor> monitors, IReadOnlyList<Computer> computers)
    {
        Title = "New Scene";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Scene name" });
        panel.Children.Add(_name);
        var shared = monitors.Where(m => m.SeenBy.Count > 1).ToList();
        if (shared.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "No monitor is shared with a connected computer yet.", TextWrapping = TextWrapping.Wrap });
        }
        foreach (var monitor in shared)
        {
            panel.Children.Add(new TextBlock { Text = monitor.Name, Margin = new Thickness(0, 6, 0, 2) });
            var choice = new ComboBox { DisplayMemberPath = nameof(Computer.Name), SelectedIndex = 0 };
            choice.Items.Add(new Computer("", "Leave as is"));
            foreach (var computer in computers.Where(c => monitor.SeenBy.Contains(c.Id)))
            {
                choice.Items.Add(computer);
            }
            choice.SelectedIndex = 0;
            choice.SelectionChanged += (_, _) => Validate();
            _owners[monitor.Identity] = choice;
            panel.Children.Add(choice);
        }

        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 3, 12, 3) };
        _save.Click += (_, _) => DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_save);
        panel.Children.Add(buttons);
        Content = panel;

        _name.TextChanged += (_, _) => Validate();
        Loaded += (_, _) => _name.Focus();
        Validate();
    }

    public string SceneName => _name.Text.Trim();

    public Dictionary<string, string> Owners => _owners
        .Where(o => o.Value.SelectedItem is Computer { Id.Length: > 0 })
        .ToDictionary(o => o.Key, o => ((Computer)o.Value.SelectedItem).Id);

    private void Validate() => _save.IsEnabled = SceneName.Length > 0 && Owners.Count > 0;
}
