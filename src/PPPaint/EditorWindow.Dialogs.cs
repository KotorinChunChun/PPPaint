using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PPPaint.Core;
using Windows.System;
using Windows.Foundation;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private sealed class EditorDialog : ContentDialog
    {
        private Button? _primaryAction, _secondaryAction, _closeAction;
        private string _primaryLabel = "", _secondaryLabel = "", _closeLabel = "";
        private bool _composing, _actionPending;
        private readonly HashSet<Button> _configuredActions = [];
        private readonly HashSet<Control> _configuredFields = [];
        private readonly HashSet<UIElement> _keyboardRoots = [];
        private UIElement? _actionScope;
        public string SecondaryAccessKey { get; init; } = "D";
        public bool AllowPlainActionKeys { get; init; }
        public EditorDialog()
        {
            DefaultStyleKey = typeof(ContentDialog);
            BindKeyboardRoot(this);
        }
        private void BindKeyboardRoot(UIElement root)
        {
            if (_keyboardRoots.Add(root))
            {
                root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Dialog_KeyDown), true);
                root.ProcessKeyboardAccelerators += (_, args) =>
                {
                    if (TryInvokeShortcut(args.Key, args.Modifiers.HasFlag(VirtualKeyModifiers.Control), args.Modifiers.HasFlag(VirtualKeyModifiers.Menu), args.Modifiers.HasFlag(VirtualKeyModifiers.Shift))) args.Handled = true;
                };
            }
        }
        internal void PrepareActions()
        {
            _primaryLabel = PrimaryButtonText; _secondaryLabel = SecondaryButtonText; _closeLabel = CloseButtonText;
            // ContentDialog must measure the complete caption, including the key suffix.
            PrimaryButtonText = Caption(_primaryLabel, "S");
            SecondaryButtonText = Caption(_secondaryLabel, SecondaryAccessKey);
            CloseButtonText = Caption(_closeLabel, "C");
        }
        private static string Caption(string label, string key) => string.IsNullOrEmpty(label) ? "" : $"{label} ({key})";
        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            // ContentDialog later moves the template into its popup; capture buttons here.
            _primaryAction = GetTemplateChild("PrimaryButton") as Button; _secondaryAction = GetTemplateChild("SecondaryButton") as Button; _closeAction = GetTemplateChild("CloseButton") as Button;
            _actionScope = GetTemplateChild("LayoutRoot") as UIElement ?? this;
            _actionScope.IsAccessKeyScope = true; BindKeyboardRoot(_actionScope);
            ConfigureAction(_primaryAction, _primaryLabel, "S", "PrimaryButton", DefaultButton == ContentDialogButton.Primary);
            ConfigureAction(_secondaryAction, _secondaryLabel, SecondaryAccessKey, "SecondaryButton", DefaultButton == ContentDialogButton.Secondary);
            ConfigureAction(_closeAction, _closeLabel, "C", "CloseButton", DefaultButton == ContentDialogButton.Close);
        }
        private void ConfigureAction(Button? button, string label, string key, string templateName, bool isDefault)
        {
            if (button is null || string.IsNullOrEmpty(label)) return;
            var keys = (AllowPlainActionKeys ? key + ", " : "") + "Alt+" + key + (isDefault ? ", Enter" : "") + (templateName == "CloseButton" ? ", Esc" : "");
            button.AccessKey = key; button.AccessKeyScopeOwner = _actionScope; button.IsTabStop = true;
            AutomationProperties.SetAutomationId(button, "Dialog" + templateName); AutomationProperties.SetName(button, label);
            AutomationProperties.SetAcceleratorKey(button, keys); AutomationProperties.SetHelpText(button, label + " (" + keys + ")"); ToolTipService.SetToolTip(button, label + " (" + keys + ")");
            var text = KeyLabel(label, key); text.TextWrapping = TextWrapping.Wrap; text.TextTrimming = TextTrimming.None;
            button.ContentTemplate = null; button.Content = text;
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            button.MinWidth = Math.Max(button.MinWidth, Math.Ceiling(text.DesiredSize.Width + button.Padding.Left + button.Padding.Right + button.BorderThickness.Left + button.BorderThickness.Right));
            // The dialog's template content can be hosted in a separate popup.
            BindKeyboardRoot(button);
            if (_configuredActions.Add(button))
            {
                bool CanInvoke() => !_composing && !EditorWindow.KeyDown(VirtualKey.Control) && !EditorWindow.KeyDown(VirtualKey.Shift);
                button.AccessKeyInvoked += (_, args) => { args.Handled = true; if (CanInvoke()) InvokeAction(button); };
            }
        }
        internal void InitializeInputKeys()
        {
            if (Content is not DependencyObject content) return;
            if (content is UIElement root) BindKeyboardRoot(root);
            foreach (var field in Descendants(content).OfType<Control>().Where(item => item.AccessKey.Length == 1))
            {
                var key = (VirtualKey)char.ToUpperInvariant(field.AccessKey[0]);
                if (_configuredFields.Add(field)) field.AccessKeyInvoked += (_, args) =>
                {
                    args.Handled = true;
                    TryInvokeShortcut(key, EditorWindow.KeyDown(VirtualKey.Control), true, EditorWindow.KeyDown(VirtualKey.Shift));
                };
            }
            foreach (var text in Descendants(content).OfType<TextBox>())
            {
                text.TextCompositionStarted += (_, _) => _composing = true;
                text.TextCompositionEnded += (_, _) => DispatcherQueue.TryEnqueue(() => _composing = false);
            }
        }
        private void Dialog_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var control = EditorWindow.KeyDown(VirtualKey.Control); var alt = EditorWindow.KeyDown(VirtualKey.Menu); var shift = EditorWindow.KeyDown(VirtualKey.Shift);
            if (!_composing && !control && !alt && !shift && e.Key is VirtualKey.Enter or VirtualKey.Escape && HasFocusedAncestor<NumberBox>())
            {
                // NumberBox consumes Enter/Esc; observe after it has validated its value.
                var action = e.Key == VirtualKey.Escape ? _closeAction : DefaultButton switch { ContentDialogButton.Primary => _primaryAction, ContentDialogButton.Secondary => _secondaryAction, _ => _closeAction };
                if (InvokeAction(action)) e.Handled = true;
                return;
            }
            if (TryInvokeShortcut(e.Key, control, alt, shift)) e.Handled = true;
        }
        private bool HasFocusedAncestor<T>() where T : DependencyObject
        {
            for (var current = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; current is not null && current != this; current = VisualTreeHelper.GetParent(current))
                if (current is T) return true;
            return false;
        }
        private bool TryInvokeShortcut(VirtualKey key, bool control, bool alt, bool shift)
        {
            // VirtualKey function-key values overlap lowercase ASCII letters.
            if (key is < VirtualKey.A or > VirtualKey.Z) return false;
            var editing = HasFocusedAncestor<TextBox>() || HasFocusedAncestor<NumberBox>() || HasFocusedAncestor<PasswordBox>() || HasFocusedAncestor<RichEditBox>();
            var command = new ScreenshotDialogShortcuts(AllowPlainActionKeys, SecondaryKey: SecondaryAccessKey[0])
                .Resolve((char)key, control, alt, shift, editing, _composing);
            var button = command switch
            {
                ScreenshotDialogAction.Primary when _primaryLabel.Length > 0 => _primaryAction,
                ScreenshotDialogAction.Secondary when _secondaryLabel.Length > 0 => _secondaryAction,
                ScreenshotDialogAction.Close when _closeLabel.Length > 0 => _closeAction,
                _ => null
            };
            if (InvokeAction(button)) return true;
            if (!alt || control || shift || _composing || _actionPending || Content is not DependencyObject content) return false;
            var field = Descendants(content).OfType<Control>().FirstOrDefault(item => item.IsEnabled && item.Visibility == Visibility.Visible && item.AccessKey.Equals(((char)key).ToString(), StringComparison.OrdinalIgnoreCase));
            if (field is null) return false;
            if (field is Button action) return InvokeAction(action);
            _actionPending = true;
            try
            {
                if (!field.Focus(FocusState.Programmatic)) return false;
                if (field is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton toggle) toggle.IsChecked = toggle.IsChecked != true;
            }
            finally { DispatcherQueue.TryEnqueue(() => _actionPending = false); }
            return true;
        }
        private bool InvokeAction(Button? action)
        {
            if (action?.IsEnabled != true || action.Visibility != Visibility.Visible || _actionPending) return false;
            if (new ButtonAutomationPeer(action).GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke) return false;
            _actionPending = true;
            try { action.Focus(FocusState.Programmatic); invoke.Invoke(); }
            finally { DispatcherQueue.TryEnqueue(() => _actionPending = false); }
            return true;
        }
        internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
    }
    private async Task<ContentDialogResult> ShowDialogAsync(EditorDialog dialog, Control? initialFocus = null)
    {
        if (_dialogOpen) throw new InvalidOperationException("別の画面を操作中です。");
        _dialogOpen = true;
        var previousFocus = FocusManager.GetFocusedElement(Root.XamlRoot) as Control;
        var backgroundKeys = EditorDialog.Descendants(Root).OfType<UIElement>().Where(element => !string.IsNullOrEmpty(element.AccessKey))
            .Select(element => (Element: element, Key: element.AccessKey)).ToArray();
        try
        {
            // Modal commands must not compete with dimmed tools behind the popup.
            foreach (var entry in backgroundKeys) entry.Element.AccessKey = "";
            dialog.IsAccessKeyScope = true; dialog.TabFocusNavigation = KeyboardNavigationMode.Cycle;
            AutomationProperties.SetAutomationId(dialog, "EditorDialog");
            AutomationProperties.SetName(dialog, dialog.Title?.ToString() ?? "ダイアログ");
            AutomationProperties.SetHelpText(dialog, "Tab/Shift+Tabで移動、Enterで既定操作、Escで取消。" + (dialog.AllowPlainActionKeys ? "括弧内の文字キーまたはAltとの組合せで操作。" : "Altと括弧内のキーで操作。"));
            if (dialog.DefaultButton == ContentDialogButton.None && string.IsNullOrEmpty(dialog.PrimaryButtonText)) dialog.DefaultButton = ContentDialogButton.Close;
            dialog.PrepareActions();
            dialog.Opened += (_, _) =>
            {
                dialog.InitializeInputKeys();
                initialFocus?.Focus(FocusState.Programmatic);
            };
            dialog.XamlRoot = Root.XamlRoot; dialog.RequestedTheme = Root.ActualTheme; return await dialog.ShowAsync();
        }
        finally
        {
            foreach (var entry in backgroundKeys) entry.Element.AccessKey = entry.Key;
            _dialogOpen = false; if (previousFocus?.IsLoaded == true) previousFocus.Focus(FocusState.Programmatic);
        }
    }
    private static void DialogField(Control control, string name, string key)
    {
        var label = KeyLabel(name, key);
        if (control is TextBox text) text.Header = label;
        if (control is NumberBox number) number.Header = label;
        AutomationProperties.SetName(control, name); AutomationProperties.SetAcceleratorKey(control, "Alt+" + key);
        AutomationProperties.SetHelpText(control, $"{name} (Alt+{key})"); ToolTipService.SetToolTip(control, $"{name} (Alt+{key})"); control.AccessKey = key;
    }
    private Task<ContentDialogResult> AskAsync(string title, string text, string primary, string secondary = "") => ShowDialogAsync(new()
    {
        Title = title, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 16 },
        PrimaryButtonText = primary, SecondaryButtonText = secondary, SecondaryAccessKey = secondary == "保存せずに閉じる" ? "N" : "D", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Close, AllowPlainActionKeys = true
    });
    private async Task MessageAsync(string title, string text) => await ShowDialogAsync(new()
    {
        Title = title, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 16, MaxWidth = 500 }, CloseButtonText = "閉じる", AllowPlainActionKeys = true
    });
    private sealed record FileEntry(string Path, string Label, bool IsFolder);
    private string? _lastFolder;
    private async Task<string?> PickPathAsync(bool save, string? title = null)
    {
        var directory = _lastFolder ?? (_savePath is not null ? Path.GetDirectoryName(_savePath) : null) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = new TextBox { Text = directory, FontSize = 16 };
        AutomationProperties.SetName(folder, "フォルダー");
        AutomationProperties.SetAutomationId(folder, "DialogFolder"); DialogField(folder, "フォルダー", "L");
        var extension = _sourceName is not null && IsOutputImage(_sourceName) ? Path.GetExtension(_sourceName) : ".png";
        var name = new TextBox { Text = save ? Path.GetFileNameWithoutExtension(_sourceName ?? "スクリーンショット") + extension : "", PlaceholderText = save ? "PNG/JPEGファイル名" : "画像ファイル名", FontSize = 16 };
        AutomationProperties.SetName(name, "ファイル名");
        AutomationProperties.SetAutomationId(name, "DialogFileName"); DialogField(name, "ファイル名", "N");
        var error = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(error, "DialogValidationError");
        var list = new ListView { Height = 280, IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single };
        AutomationProperties.SetName(list, "ファイル一覧");
        AutomationProperties.SetAutomationId(list, "DialogFileList");
        var up = new Button { Content = "上へ", FontSize = 16 };
        CommandLabel(up, "上へ", "Alt+U"); up.AccessKey = "U"; up.VerticalAlignment = VerticalAlignment.Bottom;
        var row = new Grid { ColumnSpacing = 6 }; row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Grid.SetColumn(folder, 1); row.Children.Add(up); row.Children.Add(folder);
        var panel = new StackPanel { Spacing = 8, MinWidth = 420 }; panel.Children.Add(row); panel.Children.Add(list); panel.Children.Add(name); panel.Children.Add(error);
        string? chosen = null;
        var updatingFields = false;
        void UpdateFields(Action action)
        {
            var previous = updatingFields; updatingFields = true;
            try { action(); } finally { updatingFields = previous; }
        }
        bool LoadFolder(string path)
        {
            try
            {
                path = Path.GetFullPath(path);
                var directories = Directory.EnumerateDirectories(path).OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase).Take(1000).Select(p => new FileEntry(p, "▸ " + Path.GetFileName(p), true));
                var files = Directory.EnumerateFiles(path).Where(p => save ? IsOutputImage(p) : IsImage(p)).OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase).Take(2000).Select(p => new FileEntry(p, Path.GetFileName(p), false));
                var entries = directories.Concat(files).ToArray();
                UpdateFields(() =>
                {
                    list.Items.Clear();
                    foreach (var entry in entries) list.Items.Add(new ListViewItem { Content = entry.Label, Tag = entry, FontSize = 16 });
                    directory = path; folder.Text = path; error.Text = "";
                    if (!save) name.Text = "";
                });
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { error.Text = e.Message; return false; }
        }
        void ParentFolder() { var parent = Directory.GetParent(directory); if (parent is not null) LoadFolder(parent.FullName); }
        up.Click += (_, _) => ParentFolder();
        folder.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { if (LoadFolder(folder.Text)) list.Focus(FocusState.Programmatic); else folder.Focus(FocusState.Programmatic); e.Handled = true; } };
        folder.TextChanged += (_, _) => { if (!updatingFields) list.SelectedItem = null; };
        name.TextChanged += (_, _) => { if (!updatingFields) list.SelectedItem = null; };
        list.ItemClick += (_, e) => list.SelectedItem = e.ClickedItem;
        var dialog = new EditorDialog { Title = title ?? (save ? "画像保存" : "画像を開く"), Content = panel, PrimaryButtonText = save ? "保存" : "開く", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary };
        bool ChoosePath()
        {
            try
            {
                if ((list.SelectedItem as ListViewItem)?.Tag is FileEntry selected)
                {
                    if (selected.IsFolder) { LoadFolder(selected.Path); list.Focus(FocusState.Programmatic); return false; }
                    UpdateFields(() => name.Text = Path.GetFileName(selected.Path));
                }
                if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException(save ? "ファイル名を入力してください。" : "画像ファイルを選択してください。");
                var path = Path.GetFullPath(Path.Combine(folder.Text, name.Text.Trim()));
                if (Directory.Exists(path)) { LoadFolder(path); list.Focus(FocusState.Programmatic); return false; }
                if (save)
                {
                    if (string.IsNullOrEmpty(Path.GetExtension(path))) path += extension;
                    if (!IsOutputImage(path)) throw new ArgumentException("PNG（.png）またはJPEG（.jpg/.jpeg）を指定してください。");
                    if (!Directory.Exists(Path.GetDirectoryName(path))) throw new DirectoryNotFoundException("保存先フォルダーがありません。");
                }
                else if (!File.Exists(path) || !IsImage(path)) throw new FileNotFoundException("画像ファイルを選択してください。");
                _lastFolder = Path.GetDirectoryName(path); chosen = path; return true;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { error.Text = ex.Message; name.Focus(FocusState.Programmatic); return false; }
        }
        dialog.PrimaryButtonClick += (_, e) => e.Cancel = !ChoosePath();
        list.SelectionChanged += (_, _) =>
        {
            if ((list.SelectedItem as ListViewItem)?.Tag is FileEntry { IsFolder: false } entry)
                UpdateFields(() => name.Text = Path.GetFileName(entry.Path));
        };
        list.DoubleTapped += (_, e) =>
        {
            var source = e.OriginalSource as DependencyObject;
            while (source is not null && source != list && source is not ListViewItem) source = VisualTreeHelper.GetParent(source);
            if (source is not ListViewItem item || !list.Items.Contains(item) || item.Tag is not FileEntry) return;
            e.Handled = true;
            list.SelectedItem = item;
            if (ChoosePath()) dialog.Hide();
        };
        list.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            if (ChoosePath()) dialog.Hide();
        };
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Up && KeyDown(VirtualKey.Menu)) { ParentFolder(); e.Handled = true; }
        };
        LoadFolder(directory);
        await ShowDialogAsync(dialog, name); return chosen;
    }
    private async void CustomColor_Click(object sender, RoutedEventArgs e) => await RunAsync(() => EditPaletteColorAsync(_activePaletteIndex));
    private async void Settings_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var reset = new Button { Content = "描画設定を初期値に戻す", FontSize = 16 };
        AutomationProperties.SetAutomationId(reset, "ResetDrawingPreferencesButton");
        CommandLabel(reset, "描画設定を初期値に戻す", "Alt+R"); reset.AccessKey = "R";
        reset.Click += (_, _) => ResetDrawingPreferences();
        await ShowDialogAsync(new() { Title = "設定", Content = reset, CloseButtonText = "閉じる" });
    });
}
