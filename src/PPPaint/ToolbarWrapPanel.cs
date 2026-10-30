using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace PPPaint;

public sealed class ToolbarWrapPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(ToolbarWrapPanel), new PropertyMetadata(4d, SpacingChanged));
    public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(nameof(RowSpacing), typeof(double), typeof(ToolbarWrapPanel), new PropertyMetadata(4d, SpacingChanged));
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    public double RowSpacing { get => (double)GetValue(RowSpacingProperty); set => SetValue(RowSpacingProperty, value); }
    private static void SpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((ToolbarWrapPanel)sender).InvalidateMeasure();

    private sealed class Row
    {
        public List<UIElement> Items { get; } = [];
        public double Width { get; set; }
        public double Height { get; set; }
    }
    private List<Row> Rows(double width)
    {
        var rows = new List<Row>(); var row = new Row();
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var spacing = row.Items.Count == 0 ? 0 : Math.Max(0, Spacing);
            if (row.Items.Count > 0 && row.Width + spacing + child.DesiredSize.Width > width + .01)
            { rows.Add(row); row = new Row(); spacing = 0; }
            row.Items.Add(child); row.Width += spacing + child.DesiredSize.Width; row.Height = Math.Max(row.Height, child.DesiredSize.Height);
        }
        if (row.Items.Count > 0) rows.Add(row);
        return rows;
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : double.PositiveInfinity;
        foreach (var child in Children) if (child.Visibility != Visibility.Collapsed) child.Measure(new Size(width, double.PositiveInfinity));
        var rows = Rows(width);
        return new Size(Math.Min(width, rows.Select(row => row.Width).DefaultIfEmpty(0).Max()), rows.Sum(row => row.Height) + Math.Max(0, rows.Count - 1) * Math.Max(0, RowSpacing));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0d;
        foreach (var row in Rows(Math.Max(0, finalSize.Width)))
        {
            var x = 0d;
            foreach (var child in row.Items)
            { child.Arrange(new Rect(x, y, child.DesiredSize.Width, row.Height)); x += child.DesiredSize.Width + Math.Max(0, Spacing); }
            y += row.Height + Math.Max(0, RowSpacing);
        }
        return finalSize;
    }
}
