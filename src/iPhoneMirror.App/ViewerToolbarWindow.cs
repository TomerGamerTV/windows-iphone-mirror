using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
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
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(6),
        };
        panel.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        buttons.Children.Add(CreateButton(CreateHomeIcon(), "Home Screen", "home"));
        buttons.Children.Add(CreateButton(CreateAppSwitcherIcon(), "App Switcher", "app_switcher"));
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

    private Button CreateButton(UIElement icon, string tooltip, string action)
    {
        var button = new Button
        {
            Content = icon,
            Width = 34,
            Height = 36,
            Margin = new Thickness(0, 0, action == "home" ? 4 : 0, 0),
            ToolTip = tooltip,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        button.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        button.SetResourceReference(Control.BackgroundProperty, "SurfaceAltBrush");
        AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => ActionRequested?.Invoke(this, action);
        return button;
    }

    private static Path CreateHomeIcon()
    {
        var path = new Path
        {
            Width = 18,
            Height = 18,
            Stretch = Stretch.None,
            Data = Geometry.Parse("M 1,8 L 9,1 L 17,8 M 2,7 L 2,17 L 16,17 L 16,7 M 7,17 L 7,11 L 11,11 L 11,17"),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
        };
        path.SetResourceReference(Shape.StrokeProperty, "TextBrush");
        return path;
    }

    private static Canvas CreateAppSwitcherIcon()
    {
        var canvas = new Canvas { Width = 18, Height = 18 };
        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 2; column++)
            {
                var cell = new Rectangle { Width = 7, Height = 7, RadiusX = 1, RadiusY = 1 };
                cell.SetResourceReference(Shape.FillProperty, "TextBrush");
                Canvas.SetLeft(cell, column * 11);
                Canvas.SetTop(cell, row * 11);
                canvas.Children.Add(cell);
            }
        }
        return canvas;
    }
}

internal static class ViewerIconExtensions
{
    public static Path WithResourceFill(this Path path, string resourceKey)
    {
        path.SetResourceReference(Shape.FillProperty, resourceKey);
        return path;
    }
}
