using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PPPaint;

public sealed partial class MainWindow
{
    private AppWindow? _appWindow;
    private AppWindowPresenterKind _presenterBeforeFullScreen = AppWindowPresenterKind.Overlapped;
    private bool _isFullScreen;

    private void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        SetFullScreen(!_isFullScreen);
    }

    private void ToggleFullScreen()
    {
        SetFullScreen(!_isFullScreen);
    }

    private bool ExitFullScreen()
    {
        if (!_isFullScreen)
        {
            return false;
        }

        SetFullScreen(false);
        return true;
    }

    private void SetFullScreen(bool enabled)
    {
        if (_appWindow is null || enabled == _isFullScreen)
        {
            UpdateFullScreenMenuState();
            return;
        }

        if (enabled)
        {
            _presenterBeforeFullScreen = _appWindow.Presenter.Kind;
            _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            _isFullScreen = true;
        }
        else
        {
            var restoreKind = _presenterBeforeFullScreen == AppWindowPresenterKind.FullScreen
                ? AppWindowPresenterKind.Overlapped
                : _presenterBeforeFullScreen;
            _appWindow.SetPresenter(restoreKind);
            _isFullScreen = false;
        }

        UpdateFullScreenMenuState();
    }

    private void UpdateFullScreenMenuState()
    {
        if (FullScreenMenuItem is not null)
        {
            FullScreenMenuItem.IsChecked = _isFullScreen;
        }
    }
}
