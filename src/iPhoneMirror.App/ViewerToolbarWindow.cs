using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;

namespace iPhoneMirror.App;

public sealed class ViewerToolbarWindow : Window
{
    public event EventHandler<string>? ActionRequested;

    public ViewerToolbarWindow(Window owner)
    {
        Owner = owner;
        Width = 92;
        Height = 48;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Transparent;
        FlowDirection = FlowDirection.LeftToRight;
        IsHitTestVisible = true;
        owner.LocationChanged += (_, _) => RepositionIfVisible();
        owner.SizeChanged += (_, _) => RepositionIfVisible();
        owner.StateChanged += (_, _) =>
        {
            if (owner.WindowState == WindowState.Minimized) Hide();
            else RepositionIfVisible();
        };

        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 23, 26, 29)),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(6),
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        buttons.Children.Add(CreateButton("⌂", "Home Screen", "home"));
        buttons.Children.Add(CreateButton("▥", "App Switcher", "app_switcher"));
        panel.Child = buttons;
        Content = panel;
    }

    public void Reposition(FrameworkElement viewer)
    {
        var origin = viewer.PointToScreen(new Point(0, 0));
        Left = origin.X + (viewer.ActualWidth - Width) / 2;
        Top = origin.Y + 8;
    }

    private void RepositionIfVisible()
    {
        if (IsVisible && Owner is MainWindow owner && owner.WindowState != WindowState.Minimized && owner.IsLoaded)
            Reposition(owner.MpvHost);
    }

    private Button CreateButton(string glyph, string tooltip, string action)
    {
        var button = new Button
        {
            Content = glyph,
            Width = 34,
            Height = 36,
            Margin = new Thickness(0, 0, action == "home" ? 4 : 0, 0),
            ToolTip = tooltip,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = 20,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(51, 46, 42)),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => ActionRequested?.Invoke(this, action);
        return button;
    }
}
