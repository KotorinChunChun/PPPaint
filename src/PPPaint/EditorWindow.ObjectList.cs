using System.Collections.Immutable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PPPaint.Core;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private ImmutableArray<ScreenshotAnnotation> _listedAnnotations;
    private bool _syncingObjectList;
    private bool _showObjectHandles;

    private void ObjectPanel_Click(object sender, RoutedEventArgs e)
    {
        ObjectPanel.Visibility = ObjectPanelButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RefreshObjectList();
    }

    private void ObjectHandles_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _dialogOpen || _pointerId is not null) { ObjectHandlesButton.IsChecked = _showObjectHandles; return; }
        _showObjectHandles = ObjectHandlesButton.IsChecked == true;
        UpdateCursor(_hoverPoint);
    }

    private void RefreshObjectList()
    {
        if (ObjectPanel?.Visibility != Visibility.Visible) return;
        var annotations = _document.State.Annotations;
        _syncingObjectList = true;
        try
        {
            if (annotations != _listedAnnotations)
            {
                if (annotations.Length != ObjectList.Items.Count ||
                    !ObjectList.Items.Cast<ListViewItem>().Select(row => (string)row.Tag).SequenceEqual(annotations.Reverse().Select(item => item.Id)))
                {
                    ObjectList.Items.Clear();
                    foreach (var annotation in annotations.Reverse())
                        ObjectList.Items.Add(CreateObjectRow(annotation.Id));
                }

                for (var i = 0; i < annotations.Length; i++)
                {
                    var row = (ListViewItem)ObjectList.Items[i];
                    var label = $"{i + 1}. {ScreenshotCommands.Describe(annotations[^(i + 1)])}";
                    ((TextBlock)((Grid)row.Content).Children[0]).Text = label;
                    AutomationProperties.SetName(row, label);
                    ToolTipService.SetToolTip(row, label);
                }
                _listedAnnotations = annotations;
            }

            for (var i = ObjectList.SelectedItems.Count - 1; i >= 0; i--)
                if (ObjectList.SelectedItems[i] is ListViewItem { Tag: string id } && !_selectedIds.Contains(id))
                    ObjectList.SelectedItems.RemoveAt(i);
            var selectedRows = ObjectList.SelectedItems.ToHashSet();
            foreach (var row in ObjectList.Items.Cast<ListViewItem>())
                if (row.Tag is string id && _selectedIds.Contains(id) && !selectedRows.Contains(row))
                    ObjectList.SelectedItems.Add(row);

            foreach (var button in ObjectOrderButtons.Children.OfType<Button>())
                button.IsEnabled = _selectedIds.Count > 0 && annotations.Length > 1;
        }
        finally { _syncingObjectList = false; }
    }

    private void ObjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingObjectList) return;
        if (_busy || _dialogOpen || _pointerId is not null) { RefreshObjectList(); return; }
        SelectObjectTargets(ObjectList.SelectedItems.Cast<ListViewItem>().Select(row => (string)row.Tag).ToArray());
    }

    private void ObjectList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_busy || _dialogOpen || _pointerId is not null) return;
        if (FindObjectRow(e.OriginalSource) is not { Tag: string id } row) return;

        if (!_selectedIds.Contains(id) || _region is not null)
            SelectObjectTargets([id]);
        var flyout = new MenuFlyout();
        AddOrderItems(flyout, enabled: ObjectList.Items.Count > 1);
        flyout.ShowAt(row, new FlyoutShowOptions { Position = e.GetPosition(row) });
        e.Handled = true;
    }

    private ListViewItem? FindObjectRow(object? source)
    {
        var element = source as DependencyObject;
        while (element is not null && element != ObjectList && element is not ListViewItem)
            element = VisualTreeHelper.GetParent(element);
        return element is ListViewItem row && ObjectList.Items.Contains(row) ? row : null;
    }

    private void SelectObjectTargets(string[] ids)
    {
        SetColorPicking(false);
        _region = null;
        SetCurrentTool(ScreenshotTool.Select);
        SetSelection(ids);
        UpdateCursor(_hoverPoint);
    }
}
