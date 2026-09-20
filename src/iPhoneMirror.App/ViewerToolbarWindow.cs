using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Automation;

namespace iPhoneMirror.App;

public sealed class ViewerToolbarWindow : Window
{
    public event EventHandler<string>? ActionRequested;

    public ViewerToolbarWindow(Window owner)
    {
        Owner = owner;
        Width = 104;
        Height = 52;
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

        // Floating Liquid Glass pill with rim border and soft shadow
        var panel = new Border
        {
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(6, 4, 6, 4),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(4),
            Effect = new DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 3,
                Direction = 270,
                Opacity = 0.38,
                Color = Colors.Black,
            },
        };
        panel.SetResourceReference(Border.BackgroundProperty, "ToolbarGlassBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "GlassBorderBrush");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        buttons.Children.Add(CreateButton(CreateHomeIcon(), "Home Screen", "home"));
        buttons.Children.Add(CreateButton(CreateAppSwitcherIcon(), "App Switcher", "app_switcher"));
        panel.Child = buttons;
        Content = panel;
    }

    public void Reposition(FrameworkElement viewer)
    {
        var origin = viewer.PointToScreen(new Point(0, 0));
        Left = origin.X + (viewer.ActualWidth - Width) / 2;
        Top = origin.Y + 12;
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
            Width = 38,
            Height = 36,
            Margin = new Thickness(0, 0, action == "home" ? 6 : 0, 0),
            ToolTip = tooltip,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
        };

        // Smooth glass button template with rounded hover and press highlights
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "ButtonChrome";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), "ButtonChrome"));
        template.Triggers.Add(hoverTrigger);

        var pressedTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), "ButtonChrome"));
        template.Triggers.Add(pressedTrigger);

        button.Template = template;
        button.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => ActionRequested?.Invoke(this, action);
        return button;
    }

    /// <summary>
    /// Authentic Apple iPhone Mirroring Home Screen Icon:
    /// A 3x3 grid of 9 rounded squares representing the iOS home screen app layout.
    /// </summary>
    private static Canvas CreateHomeIcon()
    {
        var canvas = new Canvas { Width = 18, Height = 18 };
        const double cellSize = 4.0;
        const double radius = 1.0;
        double[] offsets = [1.0, 7.0, 13.0];

        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                var cell = new Rectangle
                {
                    Width = cellSize,
                    Height = cellSize,
                    RadiusX = radius,
                    RadiusY = radius,
                };
                cell.SetResourceReference(Shape.FillProperty, "TextBrush");
                Canvas.SetLeft(cell, offsets[col]);
                Canvas.SetTop(cell, offsets[row]);
                canvas.Children.Add(cell);
            }
        }
        return canvas;
    }

    /// <summary>
    /// Authentic Apple iPhone Mirroring App Switcher Icon:
    /// Two overlapping portrait cards representing the iOS multitasking app switcher.
    /// </summary>
    private static Path CreateAppSwitcherIcon()
    {
        var path = new Path
        {
            Width = 18,
            Height = 18,
            Stretch = Stretch.None,
            Data = Geometry.Parse("M 7.5,1.5 H 4 A 2.5,2.5 0 0 0 1.5,4 V 14 A 2.5,2.5 0 0 0 4,16.5 H 7.5 " +
                                  "M 10,1.5 H 14 A 2.5,2.5 0 0 1 16.5,4 V 14 A 2.5,2.5 0 0 1 14,16.5 H 10 A 2.5,2.5 0 0 1 7.5,14 V 4 A 2.5,2.5 0 0 1 10,1.5 Z"),
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        path.SetResourceReference(Shape.StrokeProperty, "TextBrush");
        return path;
    }
}
