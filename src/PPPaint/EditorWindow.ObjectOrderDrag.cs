using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private readonly string _objectDragFormat = "PPPaint.ObjectOrder." + Guid.NewGuid().ToString("N");
    private HashSet<string> _draggedObjectIds = [];
    private Border? _objectDropLine;

    private static ListViewItem CreateObjectRow(string id)
    {
        var content = new Grid();
        content.Children.Add(new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        content.Children.Add(new Border
        {
            Height = 2,
            Background = new SolidColorBrush(Colors.DodgerBlue),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        });
        return new ListViewItem { Tag = id, Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }

    private void ObjectList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _draggedObjectIds = e.Items.OfType<ListViewItem>().Select(row => (string)row.Tag).ToHashSet();
        if (_draggedObjectIds.Overlaps(_selectedIds)) _draggedObjectIds.UnionWith(_selectedIds);
        if (_busy || _dialogOpen || _pointerId is not null || _draggedObjectIds.Count == 0)
        {
            _draggedObjectIds.Clear();
            e.Cancel = true;
            return;
        }

        e.Data.SetData(_objectDragFormat, "1");
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void ObjectList_DragOver(object sender, DragEventArgs e)
    {
        if (_draggedObjectIds.Count == 0 || !e.DataView.Contains(_objectDragFormat)) return;
        var y = e.GetPosition(ObjectList).Y;
        ScrollObjectListDuringDrag(y);
        ShowObjectDropLine(ObjectDropIndex(y));
        e.AcceptedOperation = DataPackageOperation.Move;
        e.Handled = true;
    }

    private void ObjectList_Drop(object sender, DragEventArgs e)
    {
        if (_draggedObjectIds.Count == 0 || !e.DataView.Contains(_objectDragFormat)) return;
        e.Handled = true;
        HideObjectDropLine();
        if (_busy || _dialogOpen || _pointerId is not null) return;
        if (ObjectList.Items.Count != _document.State.Annotations.Length) return;

        var index = ObjectDropIndex(e.GetPosition(ObjectList).Y);
        // The document stores back-to-front order. The top of the list is the front.
        _document.ReorderAt(_draggedObjectIds, ObjectList.Items.Count - index);
        SelectObjectTargets(_draggedObjectIds.ToArray());
        e.AcceptedOperation = DataPackageOperation.Move;
    }

    private int ObjectDropIndex(double y)
    {
        var next = ObjectList.Items.Count;
        var previous = -1;
        if (ObjectList.ItemsPanelRoot is not { } panel) return next;
        foreach (var row in panel.Children.OfType<ListViewItem>())
        {
            var index = ObjectList.IndexFromContainer(row);
            if (index < 0 || row.ActualHeight <= 0) continue;
            var midpoint = row.TransformToVisual(ObjectList).TransformPoint(new Point(0, row.ActualHeight / 2)).Y;
            if (y < midpoint) next = Math.Min(next, index);
            else previous = Math.Max(previous, index);
        }
        return next < ObjectList.Items.Count || previous < 0 ? next : previous + 1;
    }

    private void ScrollObjectListDuringDrag(double y)
    {
        const double edge = 24;
        var delta = y < edge ? -12 : y > ObjectList.ActualHeight - edge ? 12 : 0;
        if (delta == 0) return;
        var parent = ObjectList.ItemsPanelRoot as DependencyObject;
        while (parent is not null && parent != ObjectList && parent is not ScrollViewer)
            parent = VisualTreeHelper.GetParent(parent);
        if (parent is ScrollViewer scroll)
            scroll.ChangeView(null, Math.Clamp(scroll.VerticalOffset + delta, 0, scroll.ScrollableHeight), null, true);
    }

    private void ShowObjectDropLine(int index)
    {
        HideObjectDropLine();
        var row = index < ObjectList.Items.Count ? ObjectList.ContainerFromIndex(index) as ListViewItem : null;
        var alignment = VerticalAlignment.Top;
        if (row is null && index > 0)
        {
            row = ObjectList.ContainerFromIndex(index - 1) as ListViewItem;
            alignment = VerticalAlignment.Bottom;
        }
        if (row?.Content is not Grid content || content.Children[1] is not Border line) return;
        _objectDropLine = line;
        line.VerticalAlignment = alignment;
        line.Visibility = Visibility.Visible;
    }

    private void HideObjectDropLine()
    {
        if (_objectDropLine is null) return;
        _objectDropLine.Visibility = Visibility.Collapsed;
        _objectDropLine = null;
    }

    private void ObjectList_DragLeave(object sender, DragEventArgs e) => HideObjectDropLine();

    private void ObjectList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs e)
    {
        HideObjectDropLine();
        _draggedObjectIds.Clear();
    }
}
