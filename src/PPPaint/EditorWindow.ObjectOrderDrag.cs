using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PPPaint.Core;
using Windows.Foundation;
using Windows.System;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private sealed record ObjectListGesture(uint PointerId, ListViewItem Row, string Id, Point Start,
        HashSet<string> Selection, bool Control, bool Shift);

    private ObjectListGesture? _objectDrag;
    private ListViewItem? _objectSelectionAnchor;
    private Border? _objectDropLine;

    private ListViewItem CreateObjectRow(string id)
    {
        var content = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        content.Children.Add(new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        content.Children.Add(new Border
        {
            Height = 2,
            Background = new SolidColorBrush(Colors.DodgerBlue),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        });
        var row = new ListViewItem { Tag = id, Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        content.PointerPressed += (_, e) => ObjectRow_PointerPressed(row, e);
        return row;
    }

    private void ObjectRow_PointerPressed(ListViewItem row, PointerRoutedEventArgs e)
    {
        if (_busy || _dialogOpen || _pointerId is not null || _objectDrag is not null) return;
        var pointer = e.GetCurrentPoint(ObjectList);
        if (!pointer.Properties.IsLeftButtonPressed || !ObjectList.CapturePointer(e.Pointer)) return;
        _objectDrag = new(e.Pointer.PointerId, row, (string)row.Tag, pointer.Position,
            new(_selectedIds), KeyDown(VirtualKey.Control), KeyDown(VirtualKey.Shift));
        e.Handled = true;
    }

    private void ObjectList_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_objectDrag is not { } drag || drag.PointerId != e.Pointer.PointerId) return;
        e.Handled = true;
        var point = e.GetCurrentPoint(ObjectList).Position;
        if (!InsideObjectList(point) || !MovedObjectListPointer(drag, point))
        {
            HideObjectDropLine();
            return;
        }
        ScrollObjectListDuringDrag(point.Y);
        ShowObjectDropLine(ObjectDropIndex(point.Y));
    }

    private void ObjectList_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_objectDrag is not { } drag || drag.PointerId != e.Pointer.PointerId) return;
        e.Handled = true;
        var point = e.GetCurrentPoint(ObjectList).Position;
        CancelObjectOrderDrag();
        if (!InsideObjectList(point) || !ObjectList.Items.Contains(drag.Row)) return;
        if (!MovedObjectListPointer(drag, point))
        {
            SelectObjectListRow(drag);
            return;
        }
        if (ObjectList.Items.Count != _document.State.Annotations.Length) return;
        var moving = drag.Selection.Contains(drag.Id) ? drag.Selection : new HashSet<string> { drag.Id };
        // The document stores back-to-front order. The top of the list is the front.
        _document.ReorderAt(moving, ObjectList.Items.Count - ObjectDropIndex(point.Y));
        SelectObjectTargets(moving.ToArray());
    }

    private bool InsideObjectList(Point p) =>
        p.X >= 0 && p.X < ObjectList.ActualWidth && p.Y >= 0 && p.Y < ObjectList.ActualHeight;

    private static bool MovedObjectListPointer(ObjectListGesture drag, Point p) =>
        Math.Abs(p.X - drag.Start.X) >= 4 || Math.Abs(p.Y - drag.Start.Y) >= 4;

    private void SelectObjectListRow(ObjectListGesture drag)
    {
        drag.Row.Focus(FocusState.Pointer);
        if (drag.Shift && _objectSelectionAnchor is { } anchor && ObjectList.Items.IndexOf(anchor) is var first && first >= 0)
        {
            var last = ObjectList.Items.IndexOf(drag.Row);
            var range = ObjectList.Items.Cast<ListViewItem>()
                .Skip(Math.Min(first, last)).Take(Math.Abs(first - last) + 1).Select(row => (string)row.Tag);
            SelectObjectTargets((drag.Control ? drag.Selection.Union(range) : range).ToArray());
            return;
        }
        _objectSelectionAnchor = drag.Row;
        SelectObjectTargets(drag.Control ? ScreenshotManipulation.ClickSelection(drag.Selection, drag.Id, true).ToArray() : [drag.Id]);
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

    private void ObjectList_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_objectDrag?.PointerId == e.Pointer.PointerId) CancelObjectOrderDrag();
    }

    private void CancelObjectOrderDrag()
    {
        _objectDrag = null;
        HideObjectDropLine();
        ObjectList.ReleasePointerCaptures();
    }
}
