using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Prism.Commands;

namespace Prism.Controls;

/// <summary>Compact illustrated alternatives for direction and result choices.</summary>
public sealed class CommandChoiceView : UserControl
{
    ChoiceParam? _parameter;
    readonly UniformGrid _grid = new() { Columns = 2 };
    ToggleButton[] _buttons = [];

    public CommandChoiceView()
    {
        Content = _grid;
        DataContextChanged += (_, _) => Attach();
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
    }

    void Detach() { if (_parameter != null) _parameter.PropertyChanged -= OnChanged; _parameter = null; }
    void Attach()
    {
        Detach(); _grid.Children.Clear();
        if (DataContext is not ChoiceParam { IsIllustrated: true } parameter) return;
        _parameter = parameter;
        _parameter.PropertyChanged += OnChanged;
        _buttons = new ToggleButton[parameter.Options.Count];
        for (int i = 0; i < parameter.Options.Count; i++)
        {
            int index = i;
            string option = parameter.Options[i];
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*"), ColumnSpacing = 5 };
            Control glyph = parameter.IsResult
                ? new IconView { Icon = option switch { "Join" => "union", "Cut" => "subtract", "Intersect" => "intersect", _ => "box" } }
                : new OptionGlyph(option, parameter.IsAngular);
            glyph.Width = 22; glyph.Height = 22; glyph.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            content.Children.Add(glyph);
            var label = new TextBlock { Text = option, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            Grid.SetColumn(label, 1); content.Children.Add(label);
            var button = new ToggleButton
            {
                Content = content, MinHeight = 34, Padding = new Thickness(6, 4), Margin = new Thickness(0, 0, 4, 4),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(4),
            };
            button.Classes.Add("operationOption");
            ToolTip.SetTip(button, option);
            button.Click += (_, _) => { parameter.Index = index; Refresh(); };
            _buttons[i] = button; _grid.Children.Add(button);
        }
        Refresh();
    }
    void OnChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(ChoiceParam.Index)) Refresh(); }
    void Refresh()
    {
        for (int i = 0; i < _buttons.Length; i++) _buttons[i].IsChecked = _parameter?.Index == i;
    }

    sealed class OptionGlyph : Control
    {
        readonly string _option;
        readonly bool _angular;
        public OptionGlyph(string option, bool angular)
        {
            _option = option; _angular = angular;
            ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        }
        static readonly Geometry Plane = Geometry.Parse("M3 14 L11 10 L21 14 L13 18 Z");
        static readonly Geometry Up = Geometry.Parse("M12 13 V3 M8 7 L12 3 L16 7");
        static readonly Geometry Down = Geometry.Parse("M12 14 V24 M8 20 L12 24 L16 20");
        static readonly Geometry Arc = Geometry.Parse("M5 16 A8 8 0 1 1 19 17 M19 11 V17 H13");
        static readonly Geometry SymmetricArc = Geometry.Parse("M3 11 A9 9 0 0 1 21 11 M3 6 V11 H8 M16 11 H21 V6 M12 4 V21");
        static readonly Geometry TwoArcs = Geometry.Parse("M3 12 A9 9 0 1 1 20 16 M20 11 V16 H15 M7 13 A5 5 0 0 0 16 17 M16 13 V17 H12");

        public override void Render(DrawingContext context)
        {
            var option = _option;
            var foreground = this.FindResource("InkSoft") as IBrush ?? Brushes.Gray;
            var accent = this.FindResource("Accent") as IBrush ?? Brushes.DodgerBlue;
            var pen = new Pen(accent, 1.5);
            using var transform = context.PushTransform(Matrix.CreateScale(Bounds.Width / 24, Bounds.Height / 26));
            if (_angular)
            {
                context.DrawGeometry(null, pen, option switch { "Symmetric" => SymmetricArc, "Two sides" => TwoArcs, _ => Arc });
                if (option == "Full 360°") context.DrawEllipse(null, new Pen(foreground, 1), new Point(12, 12), 4, 4);
            }
            else
            {
                context.DrawGeometry(null, new Pen(foreground, 1), Plane);
                context.DrawGeometry(null, pen, Up);
                if (option.Contains("Two") || option.Contains("Symmetric") || option.Contains("both")) context.DrawGeometry(null, pen, Down);
                if (option == "To object") context.DrawLine(new Pen(foreground, 1.5), new Point(3, 2), new Point(21, 2));
                if (option.Contains("Through")) context.DrawLine(new Pen(foreground, 1.5), new Point(3, 8), new Point(21, 8));
            }
        }
    }
}
