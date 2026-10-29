using System.Numerics;
using PPPaint.Core;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.UI;

namespace PPPaint;

public sealed partial class MainWindow
{
    private BrushKind _brushKind = BrushKind.Oil;
    private string _textFontFamily = "Segoe UI";
    private float _textFontSize = 24;
    private bool _textBold;
    private bool _textItalic;
    private bool _textUnderline;
    private CanvasHorizontalAlignment _textAlignment = CanvasHorizontalAlignment.Left;

    private void PencilButton_Click(object sender, RoutedEventArgs e)
    {
        ToolButton_Click(sender, e);

        var flyout = new MenuFlyout();
        AddBrushMenuItem(flyout, BrushKind.Oil, "油彩ブラシ", "♠");
        AddBrushMenuItem(flyout, BrushKind.Watercolor, "水彩ブラシ", "☂");
        AddBrushMenuItem(flyout, BrushKind.Marker, "マーカー", "▰");
        AddBrushMenuItem(flyout, BrushKind.NaturalPencil, "鉛筆風ブラシ", "✎");
        AddBrushMenuItem(flyout, BrushKind.Airbrush, "エアブラシ", "✺");
        AddBrushMenuItem(flyout, BrushKind.Calligraphy, "カリグラフィー", "◢");
        AddBrushMenuItem(flyout, BrushKind.Crayon, "クレヨン", "▨");
        AddBrushMenuItem(flyout, BrushKind.Flat, "平筆", "▬");
        AddBrushMenuItem(flyout, BrushKind.Pixel, "ピクセルペン", "▪");
        flyout.ShowAt(PencilToolButton, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }

    private void AddBrushMenuItem(MenuFlyout flyout, BrushKind kind, string label, string glyph)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = label,
            Icon = CreateUnifiedIcon(GetBrushUnifiedIconId(kind)),
            Tag = kind,
            IsChecked = _brushKind == kind
        };
        item.Click += (_, _) => SelectBrush(kind, label, glyph);
        flyout.Items.Add(item);
    }

    private void SelectBrush(BrushKind kind, string label, string glyph)
    {
        _brushKind = kind;
        SetUnifiedButtonIcon(PencilToolButton, GetBrushUnifiedIconId(kind));
        _tool = PaintTool.Brush;
        ClearShapeSelectionVisual();
        foreach (var button in _toolButtons)
        {
            button.IsChecked = ReferenceEquals(button, PencilToolButton);
        }

        var shortName = label.Replace("ブラシ", "").Trim();
        ToolTipService.SetToolTip(PencilToolButton, $"鉛筆・ブラシ: {(shortName.Length > 0 ? shortName : "ブラシ")}");
        UpdateCanvasCursor();
    }

    private void DrawConfiguredStroke(Vector2 from, Vector2 to)
    {
        if (_surface is null)
        {
            return;
        }

        var width = (float)StrokeWidthSlider.Value * (_preferences.PressureWidth ? Math.Max(.05f, _strokePressure) : 1f);
        var color = _tool == PaintTool.Eraser ? _secondaryColor : WithCurrentOpacity(_primaryColor);
        if (_preferences.PressureOpacity) color.A = (byte)Math.Clamp(Math.Round(color.A * _strokePressure), 0, 255);
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

        var profile = BrushStrokePlanner.GetProfile(_brushKind);
        var brushWidth = Math.Max(1f, width * profile.WidthScale);
        switch (_brushKind)
        {
            case BrushKind.Watercolor:
                DrawWatercolorStroke(session, from, to, color, brushWidth, profile);
                break;
            case BrushKind.Marker:
                DrawMarkerStroke(session, from, to, color, brushWidth, profile);
                break;
            case BrushKind.NaturalPencil:
                DrawNaturalPencilStroke(session, from, to, color, width, profile);
                break;
            case BrushKind.Airbrush:
                DrawAirbrushStroke(session, from, to, color, width);
                break;
            case BrushKind.Calligraphy:
                DrawCalligraphyStroke(session, from, to, color, brushWidth);
                break;
            case BrushKind.Crayon:
                DrawCrayonStroke(session, from, to, color, width, profile);
                break;
            case BrushKind.Flat:
                DrawFlatStroke(session, from, to, color, brushWidth, profile);
                break;
            case BrushKind.Pixel:
                DrawPixelStroke(session, from, to, color, width);
                break;
            default:
                DrawRoundStroke(session, from, to, color, brushWidth);
                break;
        }
    }

    private static void DrawRoundStroke(Microsoft.Graphics.Canvas.CanvasDrawingSession session, Vector2 from, Vector2 to, Color color, float width)
    {
        session.DrawLine(from, to, color, width);
        session.FillCircle(to, width / 2f, color);
    }

    private static void DrawWatercolorStroke(Microsoft.Graphics.Canvas.CanvasDrawingSession session, Vector2 from, Vector2 to, Color color, float width, BrushStrokeProfile profile)
    {
        var wash = ScaleAlpha(color, profile.OpacityScale);
        for (var i = -2; i <= 2; i++)
        {
            var offset = new Vector2(i * width * 0.08f, -i * width * 0.05f);
            session.DrawLine(from + offset, to + offset, wash, width * (0.72f - Math.Abs(i) * 0.07f));
        }
        session.FillCircle(to, width * 0.34f, wash);
    }

    private static void DrawMarkerStroke(Microsoft.Graphics.Canvas.CanvasDrawingSession session, Vector2 from, Vector2 to, Color color, float width, BrushStrokeProfile profile)
    {
        var marker = ScaleAlpha(color, profile.OpacityScale);
        using var style = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Square, EndCap = CanvasCapStyle.Square };
        session.DrawLine(from, to, marker, width, style);
    }

    private static void DrawNaturalPencilStroke(Microsoft.Graphics.Canvas.CanvasDrawingSession session, Vector2 from, Vector2 to, Color color, float baseWidth, BrushStrokeProfile profile)
    {
        var width = Math.Max(1f, baseWidth * profile.WidthScale);
        var grain = ScaleAlpha(color, profile.OpacityScale);
        session.DrawLine(from, to, grain, width);
        foreach (var dab in BrushStrokePlanner.PlanSegment(BrushKind.NaturalPencil, from.X, from.Y, to.X, to.Y, baseWidth))
        {
            session.FillCircle(dab.X, dab.Y, dab.Radius, color);
        }
    }

    private static void DrawAirbrushStroke(Microsoft.Graphics.Canvas.CanvasDrawingSession session, Vector2 from, Vector2 to, Color color, float width)
    {
        var profile = BrushStrokePlanner.GetProfile(BrushKind.Airbrush);
        var spray = ScaleAlpha(color, profile.OpacityScale);
        foreach (var dab in BrushStrokePlanner.PlanSegment(BrushKind.Airbrush, from.X, from.Y, to.X, to.Y, width))
        {
            session.FillCircle(dab.X, dab.Y, dab.Radius, spray);
        }
    }

    private static void DrawCalligraphyStroke(Microsoft.Graphics.Canvas.CanvasDrawingSession session, Vector2 from, Vector2 to, Color color, float width)
    {
        var nib = new Vector2(width * 0.28f, -width * 0.28f);
        for (var i = -2; i <= 2; i++)
        {
            var offset = nib * (i / 2f);
            session.DrawLine(from + offset, to + offset, color, Math.Max(1f, width * 0.18f));
        }
    }

    private static void DrawCrayonStroke(Microsoft.Graphics.Canvas.CanvasDrawingSession session, Vector2 from, Vector2 to, Color color, float baseWidth, BrushStrokeProfile profile)
    {
        var grain = ScaleAlpha(color, profile.OpacityScale);
        session.DrawLine(from, to, grain, Math.Max(1f, baseWidth * profile.WidthScale * 0.62f));
        foreach (var dab in BrushStrokePlanner.PlanSegment(BrushKind.Crayon, from.X, from.Y, to.X, to.Y, baseWidth))
        {
            var size = dab.Radius * 2f;
            session.FillRectangle(dab.X - dab.Radius, dab.Y - dab.Radius, size, size, grain);
        }
    }

    private static void DrawFlatStroke(Microsoft.Graphics.Canvas.CanvasDrawingSession session, Vector2 from, Vector2 to, Color color, float width, BrushStrokeProfile profile)
    {
        var paint = ScaleAlpha(color, profile.OpacityScale);
        using var style = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Square, EndCap = CanvasCapStyle.Square };
        session.DrawLine(from, to, paint, width, style);
        var highlight = ScaleAlpha(color, profile.OpacityScale * 0.38f);
        session.DrawLine(from + new Vector2(0, width * 0.22f), to + new Vector2(0, width * 0.22f), highlight, Math.Max(1f, width * 0.16f), style);
    }

    private static void DrawPixelStroke(Microsoft.Graphics.Canvas.CanvasDrawingSession session, Vector2 from, Vector2 to, Color color, float baseWidth)
    {
        foreach (var dab in BrushStrokePlanner.PlanSegment(BrushKind.Pixel, from.X, from.Y, to.X, to.Y, baseWidth))
        {
            var size = Math.Max(1f, MathF.Round(dab.Radius * 2f));
            session.FillRectangle(dab.X - (size / 2f), dab.Y - (size / 2f), size, size, color);
        }
    }

    private static Color ScaleAlpha(Color color, float scale) => Color.FromArgb(
        (byte)Math.Clamp((int)MathF.Round(color.A * scale), 1, byte.MaxValue),
        color.R,
        color.G,
        color.B);

    private async Task ShowTextDialogAndInsertAsync(Vector2 point)
    {
        await ShowTextEditorAsync(null, point);
    }

    private async Task ShowTextEditorAsync(EditableObject? existing, Vector2 point)
    {
        var existingStyle = existing?.TextStyle ?? new TextObjectStyle(
            _textFontFamily,
            _textFontSize,
            _textBold,
            _textItalic,
            _textUnderline,
            false,
            _textAlignment switch
            {
                CanvasHorizontalAlignment.Center => EditableTextAlignment.Center,
                CanvasHorizontalAlignment.Right => EditableTextAlignment.Right,
                _ => EditableTextAlignment.Left
            },
            ToCoreColor(WithCurrentOpacity(_primaryColor)),
            false,
            ToCoreColor(WithCurrentOpacity(_secondaryColor)));
        var textBox = new TextBox
        {
            Text = existing?.Text ?? string.Empty,
            PlaceholderText = "ここにテキストを入力してください。",
            MinWidth = 460,
            MinHeight = 120,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };
        ConfigureFieldAccessibility(textBox, "テキスト内容", "複数行のテキストを入力します");

        var fontFamily = new ComboBox { Width = 180, Header = "フォント" };
        ConfigureFieldAccessibility(fontFamily, "フォント", "テキストのフォントを選択します");
        foreach (var name in new[] { "Segoe UI", "Arial", "Calibri", "Times New Roman", "Yu Gothic UI", "Meiryo", "Consolas" })
        {
            fontFamily.Items.Add(name);
        }
        fontFamily.SelectedItem = existingStyle.FontFamily;

        var fontSize = new NumberBox
        {
            Width = 112,
            Minimum = 1,
            Maximum = 300,
            Value = existingStyle.FontSize,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        ConfigureFieldAccessibility(fontSize, "文字サイズ", "文字サイズを1から300ポイントで指定します");
        var bold = new ToggleButton { Content = CreateUnifiedIcon("A-text-bold"), FontWeight = FontWeights.Bold, IsChecked = existingStyle.Bold, Width = 42 };
        var italic = new ToggleButton { Content = CreateUnifiedIcon("A-text-italic"), FontStyle = Windows.UI.Text.FontStyle.Italic, IsChecked = existingStyle.Italic, Width = 42 };
        var underline = new ToggleButton { Content = CreateUnifiedIcon("A-text-underline"), IsChecked = existingStyle.Underline, Width = 42 };
        var strike = new ToggleButton { Content = CreateUnifiedIcon("A-text-strike"), IsChecked = existingStyle.Strikethrough, Width = 42 };
        var background = new ToggleButton { Content = "背景", IsChecked = existingStyle.HasBackground, Width = 56 };
        ConfigureFieldAccessibility(bold, "太字", "太字を切り替えます");
        ConfigureFieldAccessibility(italic, "斜体", "斜体を切り替えます");
        ConfigureFieldAccessibility(underline, "下線", "下線を切り替えます");
        ConfigureFieldAccessibility(strike, "取り消し線", "取り消し線を切り替えます");
        ConfigureFieldAccessibility(background, "背景塗り", "テキストの背景塗りを切り替えます");
        var alignment = new ComboBox { Width = 112, Header = "配置" };
        ConfigureFieldAccessibility(alignment, "文字の配置", "左揃え、中央揃え、右揃えを選択します");
        alignment.Items.Add("左揃え");
        alignment.Items.Add("中央揃え");
        alignment.Items.Add("右揃え");
        alignment.SelectedIndex = existingStyle.Alignment switch
        {
            EditableTextAlignment.Center => 1,
            EditableTextAlignment.Right => 2,
            _ => 0
        };

        var fontRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        fontRow.Children.Add(fontFamily);
        fontRow.Children.Add(fontSize);
        fontRow.Children.Add(alignment);

        var styleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        styleRow.Children.Add(bold);
        styleRow.Children.Add(italic);
        styleRow.Children.Add(underline);
        styleRow.Children.Add(strike);
        styleRow.Children.Add(background);

        var formatRow = new StackPanel { Spacing = 8 };
        formatRow.Children.Add(fontRow);
        formatRow.Children.Add(styleRow);

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(formatRow);
        content.Children.Add(textBox);
        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot,
            Title = existing is null ? "テキストを挿入" : "テキストを編集",
            Content = content,
            PrimaryButtonText = existing is null ? "挿入" : "更新",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary
        };
        ConfigureDialogAccessibility(dialog);

        if (await dialog.ShowManagedAsync() != ContentDialogResult.Primary || string.IsNullOrEmpty(textBox.Text) || _surface is null)
        {
            return;
        }

        _textFontFamily = fontFamily.SelectedItem?.ToString() ?? existingStyle.FontFamily;
        _textFontSize = (float)(double.IsNaN(fontSize.Value) ? existingStyle.FontSize : fontSize.Value);
        _textBold = bold.IsChecked == true;
        _textItalic = italic.IsChecked == true;
        _textUnderline = underline.IsChecked == true;
        _textAlignment = alignment.SelectedIndex switch
        {
            1 => CanvasHorizontalAlignment.Center,
            2 => CanvasHorizontalAlignment.Right,
            _ => CanvasHorizontalAlignment.Left
        };
        var style = existingStyle with
        {
            FontFamily = _textFontFamily,
            FontSize = _textFontSize,
            Bold = _textBold,
            Italic = _textItalic,
            Underline = _textUnderline,
            Strikethrough = strike.IsChecked == true,
            Alignment = alignment.SelectedIndex switch
            {
                1 => EditableTextAlignment.Center,
                2 => EditableTextAlignment.Right,
                _ => EditableTextAlignment.Left
            },
            Foreground = ToCoreColor(WithCurrentOpacity(_primaryColor)),
            HasBackground = background.IsChecked == true,
            Background = ToCoreColor(WithCurrentOpacity(_secondaryColor))
        };
        var bounds = existing?.Bounds ?? EstimateTextBounds(point, textBox.Text, _textFontSize);
        var editable = existing is null
            ? EditableObject.CreateText(bounds, textBox.Text, style, layerId: ActiveLayerId)
            : existing with { Text = textBox.Text, TextStyle = style, Bounds = bounds };
        CaptureUndo();
        if (existing is null)
        {
            _objects.Add(editable);
            ActivateEditableObject(editable.Id, isNew: true);
        }
        else
        {
            ReplaceEditableObject(editable);
            _activeObjectId = editable.Id;
            _activeObjectIsNew = false;
            _selection = null;
            UpdateSelectionCommandState();
            PaintCanvas.Invalidate();
        }
        _selectionMode = SelectionMode.Rectangle;
        ActivateSelectionTool();
        FinishEdit();
    }

    private ObjectBounds EstimateTextBounds(Vector2 point, string text, float fontSize)
    {
        var longestLine = text.Split('\n').Select(line => line.Length).DefaultIfEmpty(1).Max();
        var width = Math.Clamp(Math.Max(80, longestLine * fontSize * 0.65f), 1, Math.Max(1, _imageWidth - point.X));
        var height = Math.Clamp(Math.Max(fontSize * 1.5f, text.Count(character => character == '\n') * fontSize * 1.35f + fontSize * 1.5f), 1, Math.Max(1, _imageHeight - point.Y));
        return new ObjectBounds(point.X, point.Y, width, height);
    }

}
