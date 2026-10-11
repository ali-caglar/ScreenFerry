using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace ScreenFerry.App;

/// <summary>A short-lived, click-through panel above the taskbar that shows a handoff's progress.</summary>
internal sealed class HudWindow : Window
{
    private readonly TextBlock _symbol = new() { FontSize = 26, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    private readonly TextBlock _detail = new() { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _hide = new();

    public HudWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 380 };
        text.Children.Add(_title);
        text.Children.Add(_detail);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_symbol);
        row.Children.Add(text);
        Content = new Border
        {
            Child = row,
            MinWidth = 260,
            Padding = new Thickness(20, 14, 20, 14),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20)),
        };
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            Hide();
        };
    }

    public enum Kind
    {
        Progress,
        Done,
        Failed,
    }

    /// <summary>Progress stays until replaced; the others hide after a moment.</summary>
    public void Show(string title, string? detail, Kind kind)
    {
        (_symbol.Text, _symbol.Foreground) = kind switch
        {
            Kind.Progress => ("⟳", Brushes.White),
            Kind.Done => ("✓", Brushes.LimeGreen),
            _ => ("✕", Brushes.OrangeRed),
        };
        _title.Text = title;
        _detail.Text = detail ?? "";
        _detail.Visibility = detail is null ? Visibility.Collapsed : Visibility.Visible;
        Show();
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 80;

        _hide.Stop();
        if (kind != Kind.Progress)
        {
            _hide.Interval = TimeSpan.FromSeconds(kind == Kind.Failed ? 5 : 2);
            _hide.Start();
        }
    }
}
