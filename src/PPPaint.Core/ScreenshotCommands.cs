namespace PPPaint.Core;

public sealed record ScreenshotToolCommand(ScreenshotTool Tool, string Name, char Key, string Basis);

public static class ScreenshotCommands
{
    public static IReadOnlyList<ScreenshotToolCommand> Tools { get; } = Array.AsReadOnly(new[]
    {
        new ScreenshotToolCommand(ScreenshotTool.Select, "選択", 'V', "Photoshopの移動V"),
        new(ScreenshotTool.Region, "範囲", 'M', "Photoshopの矩形選択M"),
        new(ScreenshotTool.Pen, "ペン", 'B', "PhotoshopのブラシB"),
        new(ScreenshotTool.Eraser, "消しゴム", 'E', "Photoshopの消しゴムE"),
        new(ScreenshotTool.Arrow, "矢印", 'A', "Greenshotの矢印A"),
        new(ScreenshotTool.Rectangle, "四角", 'R', "Greenshotの矩形R"),
        new(ScreenshotTool.Ellipse, "丸", 'O', "EllipseのEと消しゴムEの衝突を避ける独自割当"),
        new(ScreenshotTool.Line, "直線", 'L', "Greenshotの直線L"),
        new(ScreenshotTool.Text, "文字", 'T', "Photoshop/Greenshotの文字T"),
        new(ScreenshotTool.Number, "番号", 'N', "Numberの頭文字・独自割当"),
        new(ScreenshotTool.Redact, "情報隠し", 'K', "BlackoutのK・独自割当"),
        new(ScreenshotTool.Highlight, "蛍光ペン", 'H', "GreenshotのハイライトH"),
        new(ScreenshotTool.Blur, "ぼかし", 'F', "Filterの頭文字・独自割当"),
        new(ScreenshotTool.Callout, "吹き出し", 'Q', "Quoteの頭文字・独自割当"),
        new(ScreenshotTool.Mosaic, "モザイク", 'X', "PixelのX・独自割当"),
        new(ScreenshotTool.Watermark, "透かし", 'S', "Stampの頭文字・独自割当")
    });
    public static bool TryTool(char key, bool control, bool alt, bool shift, bool editingText, out ScreenshotTool tool)
    {
        tool = default;
        if (control || alt || shift || editingText) return false;
        var command = Tools.FirstOrDefault(c => c.Key == char.ToUpperInvariant(key));
        if (command is null) return false;
        tool = command.Tool; return true;
    }
}
