using System.Numerics;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.UI;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private BrushKind _brushKind = BrushKind.Oil;
    private string _textFontFamily = "Segoe UI";
    private float _textFontSize = 24;
    private bool _textBold;
    private bool _textItalic;
    private bool _textUnderline;
    private CanvasHorizontalAlignment _textAlignment = CanvasHorizontalAlignment.Left;

    private void BrushButton_Click(object sender, RoutedEventArgs e)
    {
        ToolButton_Click(sender, e);

        var flyout = new MenuFlyout();
        AddBrushMenuItem(flyout, BrushKind.Oil, "油彩ブラシ", "♠");
        AddBrushMenuItem(flyout, BrushKind.Watercolor, "水彩ブラシ", "☂");
        AddBrushMenuItem(flyout, BrushKind.Marker, "マーカー", "▰");
        AddBrushMenuItem(flyout, BrushKind.NaturalPencil, "鉛筆風ブラシ", "✎");
        AddBrushMenuItem(flyout, BrushKind.Airbrush, "エアブラシ", "✺");
        AddBrushMenuItem(flyout, BrushKind.Calligraphy, "カリグラフィー", "◢");
        flyout.ShowAt(BrushToolButton, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }

    private void AddBrushMenuItem(MenuFlyout flyout, BrushKind kind, string label, string glyph)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = label,
            Tag = kind,
            IsChecked = _brushKind == kind
        };
        item.Click += (_, _) => SelectBrush(kind, label, glyph);
        flyout.Items.Add(item);
    }

    private void SelectBrush(BrushKind kind, string label, string glyph)
    {
        _brushKind = kind;
        _tool = PaintTool.Brush;
        ClearShapeSelectionVisual();
        foreach (var button in _toolButtons)
        {
            button.IsChecked = ReferenceEquals(button, BrushToolButton);
        }

        BrushGlyph.Text = glyph;
        var shortName = label.Replace("ブラシ", "").Trim();
        BrushNameText.Text = shortName.Length > 0 ? shortName : "ブラシ";
        ToolTipService.SetToolTip(BrushToolButton, label);
    }

    private void DrawConfiguredStroke(Vector2 from, Vector2 to)
    {
        if (_surface is null)
        {
            return;
        }

        var width = (float)StrokeWidthSlider.Value;
        var color = _tool == PaintTool.Eraser ? _secondaryColor : WithCurrentOpacity(_primaryColor);
        using var session = _surface.CreateDrawingSession();

        if (_tool == PaintTool.Eraser)
        {
            DrawRoundStroke(session, from, to, color, width * 3f);
            return;
        }

        if (_tool != PaintTool.Brush)
        {
            DrawRoundStroke(session, from, to, color, width);
            return;
        }

        switch (_brushKind)
        {
            case BrushKind.Watercolor:
                DrawWatercolorStroke(session, from, to, color, width * 3.1f);
                break;
            case BrushKind.Marker:
                DrawMarkerStroke(session, from, to, color, width * 3.4f);
                break;
            case BrushKind.NaturalPencil:
                DrawNaturalPencilStroke(session, from, to, color, Math.Max(1f, width * 0.8f));
                break;
            case BrushKind.Airbrush:
                DrawAirbrushStroke(session, from, to, color, width * 3.2f);
                break;
            case BrushKind.Calligraphy:
                DrawCalligraphyStroke(session, from, to, color, width * 2.8f);
                break;
            default:
                DrawRoundStroke(session, from, to, color, width * 2.25f);
                break;
        }
    }

    private static void DrawRoundStroke(Microsoft.Graphics.Canvas.CanvasDrawingSession session, Vector2 from, Vector2 to, Color color, float width)
    {
        session.DrawLine(from, to, color, width);
        session.FillCircle(to, width / 2f, color);
    }

    private static void DrawWatercolorStroke(Microsoft.Graphics.Canvas.CanvasDrawingSession session, Vector2 from, Vector2 to, Color color, float width)
    {
        var wash = Color.FromArgb((byte)Math.Max(1, color.A * 0.18), color.R, color.G, color.B);
        for (var i = -2; i <= 2; i++)
        {
            var offset = new Vector2(i * width * 0.08f, -i * width * 0.05f);
            session.DrawLine(from + offset, to + offset, wash, width * (0.72f - Math.Abs(i) * 0.07f));
        }
        session.FillCircle(to, width * 0.34f, wash);
    }

    private static void DrawMarkerStroke(Microsoft.Graphics.Canvas.CanvasDrawingSession session, Vector2 from, Vector2 to, Color color, float width)
    {
        var marker = Color.FromArgb((byte)Math.Max(1, color.A * 0.55), color.R, color.G, color.B);
        using var style = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Square, EndCap = CanvasCapStyle.Square };
        session.DrawLine(from, to, marker, width, style);
    }

    private static void DrawNaturalPencilStroke(Microsoft.Graphics.Canvas.CanvasDrawingSession session, Vector2 from, Vector2 to, Color color, float width)
    {
        var grain = Color.FromArgb((byte)Math.Max(1, color.A * 0.72), color.R, color.G, color.B);
        session.DrawLine(from, to, grain, width);
        var length = Math.Max(1f, Vector2.Distance(from, to));
        var steps = Math.Max(1, (int)(length / 3f));
        for (var i = 0; i <= steps; i++)
        {
            var point = Vector2.Lerp(from, to, i / (float)steps);
            var jitter = new Vector2(MathF.Sin((point.X + point.Y) * 1.73f), MathF.Cos((point.X - point.Y) * 1.37f)) * width * 0.35f;
            session.FillCircle(point + jitter, Math.Max(0.45f, width * 0.22f), color);
        }
    }

    private static void DrawAirbrushStroke(Microsoft.Graphics.Canvas.CanvasDrawingSession session, Vector2 from, Vector2 to, Color color, float width)
    {
        var spray = Color.FromArgb((byte)Math.Max(1, color.A * 0.22), color.R, color.G, color.B);
        var length = Math.Max(1f, Vector2.Distance(from, to));
        var steps = Math.Max(1, (int)(length / 2f));
        for (var i = 0; i <= steps; i++)
        {
            var center = Vector2.Lerp(from, to, i / (float)steps);
            for (var dot = 0; dot < 8; dot++)
            {
                var seed = (center.X * 12.9898f) + (center.Y * 78.233f) + (dot * 37.719f);
                var angle = Fraction(MathF.Sin(seed) * 43758.5453f) * MathF.Tau;
                var radius = MathF.Sqrt(Fraction(MathF.Sin(seed + 19.19f) * 24634.6345f)) * width * 0.5f;
                var position = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                session.FillCircle(position, Math.Max(0.65f, width * 0.035f), spray);
            }
        }
    }

    private static float Fraction(float value) => value - MathF.Floor(value);

    private static void DrawCalligraphyStroke(Microsoft.Graphics.Canvas.CanvasDrawingSession session, Vector2 from, Vector2 to, Color color, float width)
    {
        var nib = new Vector2(width * 0.28f, -width * 0.28f);
        for (var i = -2; i <= 2; i++)
        {
            var offset = nib * (i / 2f);
            session.DrawLine(from + offset, to + offset, color, Math.Max(1f, width * 0.18f));
        }
    }

    private async Task ShowTextDialogAndInsertAsync(Vector2 point)
    {
        var textBox = new TextBox
        {
            PlaceholderText = "ここにテキストを入力してください。",
            MinWidth = 460,
            MinHeight = 120,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };

        var fontFamily = new ComboBox { Width = 180 };
        foreach (var name in new[] { "Segoe UI", "Arial", "Calibri", "Times New Roman", "Yu Gothic UI", "Meiryo", "Consolas" })
        {
            fontFamily.Items.Add(name);
        }
        fontFamily.SelectedItem = _textFontFamily;

        var fontSize = new NumberBox
        {
            Width = 88,
            Minimum = 1,
            Maximum = 300,
            Value = _textFontSize,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        var bold = new ToggleButton { Content = "B", FontWeight = FontWeights.Bold, IsChecked = _textBold, Width = 42 };
        var italic = new ToggleButton { Content = "I", FontStyle = Windows.UI.Text.FontStyle.Italic, IsChecked = _textItalic, Width = 42 };
        var underline = new ToggleButton { Content = "U̲", IsChecked = _textUnderline, Width = 42 };
        var alignment = new ComboBox { Width = 112 };
        alignment.Items.Add("左揃え");
        alignment.Items.Add("中央揃え");
        alignment.Items.Add("右揃え");
        alignment.SelectedIndex = _textAlignment switch
        {
            CanvasHorizontalAlignment.Center => 1,
            CanvasHorizontalAlignment.Right => 2,
            _ => 0
        };

        var formatRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        formatRow.Children.Add(fontFamily);
        formatRow.Children.Add(fontSize);
        formatRow.Children.Add(bold);
        formatRow.Children.Add(italic);
        formatRow.Children.Add(underline);
        formatRow.Children.Add(alignment);

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(formatRow);
        content.Children.Add(textBox);

        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot,
            Title = "テキスト",
            Content = content,
            PrimaryButtonText = "挿入",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrEmpty(textBox.Text) || _surface is null)
        {
            return;
        }

        _textFontFamily = fontFamily.SelectedItem?.ToString() ?? "Segoe UI";
        _textFontSize = (float)(double.IsNaN(fontSize.Value) ? 24 : fontSize.Value);
        _textBold = bold.IsChecked == true;
        _textItalic = italic.IsChecked == true;
        _textUnderline = underline.IsChecked == true;
        _textAlignment = alignment.SelectedIndex switch
        {
            1 => CanvasHorizontalAlignment.Center,
            2 => CanvasHorizontalAlignment.Right,
            _ => CanvasHorizontalAlignment.Left
        };

        CaptureUndo();
        using var session = _surface.CreateDrawingSession();
        using var format = new CanvasTextFormat
        {
            FontFamily = _textFontFamily,
            FontSize = _textFontSize,
            FontWeight = _textBold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = _textItalic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            HorizontalAlignment = _textAlignment,
            WordWrapping = CanvasWordWrapping.Wrap
        };
        using var layout = new CanvasTextLayout(session, textBox.Text, format,
            Math.Max(1f, _imageWidth - point.X), Math.Max(1f, _imageHeight - point.Y));
        if (_textUnderline)
        {
            layout.SetUnderline(0, textBox.Text.Length, true);
        }
        session.DrawTextLayout(layout, point, WithCurrentOpacity(_primaryColor));
        FinishEdit();
    }

    private enum BrushKind
    {
        Oil,
        Watercolor,
        Marker,
        NaturalPencil,
        Airbrush,
        Calligraphy
    }
}
