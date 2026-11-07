using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace TransparentChatFilter;

public partial class MainWindow : Window
{
    private const int WmHotkey = 0x0312;
    private int _hotKeyId = 9000;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WindowMessageHook);
        }

        RegisterHotKey();
        UpdateClickThroughStyle();
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != NativeMethods.WmNcHitTest ||
            ResizeMode != ResizeMode.CanResize ||
            WindowState != WindowState.Normal)
        {
            return IntPtr.Zero;
        }

        var packedPoint = lParam.ToInt64();
        var screenX = unchecked((short)(packedPoint & 0xFFFF));
        var screenY = unchecked((short)((packedPoint >> 16) & 0xFFFF));
        var point = PointFromScreen(new Point(screenX, screenY));
        const double resizeBorder = 8;
        var left = point.X <= resizeBorder;
        var right = point.X >= ActualWidth - resizeBorder;
        var top = point.Y <= resizeBorder;
        var bottom = point.Y >= ActualHeight - resizeBorder;
        var hitTest = (left, right, top, bottom) switch
        {
            (true, _, true, _) => NativeMethods.HtTopLeft,
            (_, true, true, _) => NativeMethods.HtTopRight,
            (true, _, _, true) => NativeMethods.HtBottomLeft,
            (_, true, _, true) => NativeMethods.HtBottomRight,
            (true, _, _, _) => NativeMethods.HtLeft,
            (_, true, _, _) => NativeMethods.HtRight,
            (_, _, true, _) => NativeMethods.HtTop,
            (_, _, _, true) => NativeMethods.HtBottom,
            _ => 0
        };

        if (hitTest == 0)
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(hitTest);
    }

    private void RegisterHotKey()
    {
        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.RegisterHotKey(handle, _hotKeyId, NativeMethods.ModAlt | NativeMethods.ModControl, (int)KeyInterop.VirtualKeyFromKey(Key.T));
        NativeMethods.RegisterHotKey(handle, _hotKeyId + 1, NativeMethods.ModAlt | NativeMethods.ModControl | NativeMethods.ModShift, (int)KeyInterop.VirtualKeyFromKey(Key.T));
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    private void UnregisterHotKey()
    {
        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle;
        if (handle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(handle, _hotKeyId);
            NativeMethods.UnregisterHotKey(handle, _hotKeyId + 1);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == _hotKeyId)
        {
            Visibility = Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
            handled = true;
        }
        else if (msg == WmHotkey && wParam.ToInt32() == _hotKeyId + 1)
        {
            SetChatOnlyMode(!_settings.IsTransparent);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void UpdateClickThroughStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var extendedStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
        var enableClickThrough = _settings.ClickThroughEnabled &&
                                 _settings.IsTransparent &&
                                 !_cursorOverChatOnlyGear &&
                                 !_cursorOverResizeEdge &&
                                 !_chatOnlyGearDragActive;
        extendedStyle = enableClickThrough
            ? extendedStyle | NativeMethods.WsExTransparent
            : extendedStyle & ~NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLong(handle, NativeMethods.GwlExStyle, extendedStyle);
    }

    private void ExitChatOnlyButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _suppressNextGearClick = false;
        _suppressGearClickTimer.Stop();
        if (!NativeMethods.GetCursorPos(out _chatOnlyGearDragStart))
        {
            ViewModel.UpdateDiagnostic("Não foi possível iniciar o arrasto do chat transparente.");
            return;
        }

        _chatOnlyGearWindowStartLeft = Left;
        _chatOnlyGearWindowStartTop = Top;
        _chatOnlyGearHoldTimer.Start();
    }

    private void ExitChatOnlyButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _chatOnlyGearHoldTimer.Stop();
        FinishChatOnlyGearDrag();
    }

    private void ChatOnlyGearHoldTimer_Tick(object? sender, EventArgs e)
    {
        if (!_settings.IsTransparent || Mouse.LeftButton != MouseButtonState.Pressed)
        {
            _chatOnlyGearHoldTimer.Stop();
            FinishChatOnlyGearDrag();
            return;
        }

        if (!NativeMethods.GetCursorPos(out var cursorPosition))
        {
            _chatOnlyGearHoldTimer.Stop();
            ViewModel.UpdateDiagnostic("Não foi possível acompanhar o movimento do chat transparente.");
            return;
        }

        var deltaX = Math.Abs(cursorPosition.X - _chatOnlyGearDragStart.X);
        var deltaY = Math.Abs(cursorPosition.Y - _chatOnlyGearDragStart.Y);
        if (deltaX < SystemParameters.MinimumHorizontalDragDistance &&
            deltaY < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _chatOnlyGearDragActive = true;
        _suppressNextGearClick = true;
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = _chatOnlyGearWindowStartLeft +
               (cursorPosition.X - _chatOnlyGearDragStart.X) / dpi.DpiScaleX;
        Top = _chatOnlyGearWindowStartTop +
              (cursorPosition.Y - _chatOnlyGearDragStart.Y) / dpi.DpiScaleY;
    }

    private void FinishChatOnlyGearDrag()
    {
        if (!_chatOnlyGearDragActive)
        {
            return;
        }

        _chatOnlyGearDragActive = false;
        _suppressNextGearClick = true;
        _suppressGearClickTimer.Stop();
        _suppressGearClickTimer.Start();
    }

    private void ChatOnlyHitTestTimer_Tick(object? sender, EventArgs e)
    {
        var gearButton = ChatOnlyGearPopup.IsOpen
            ? (FrameworkElement)ChatOnlyGearPopupButton
            : ExitChatOnlyButton;
        if (!IsLoaded || !NativeMethods.GetCursorPos(out var cursorPosition))
        {
            return;
        }

        var cursorScreenPoint = new Point(cursorPosition.X, cursorPosition.Y);
        var cursorInWindow = PointFromScreen(cursorScreenPoint);
        const double resizeBorder = 8;
        var overResizeEdge = cursorInWindow.X >= 0 && cursorInWindow.Y >= 0 &&
                             cursorInWindow.X <= ActualWidth && cursorInWindow.Y <= ActualHeight &&
                             (cursorInWindow.X <= resizeBorder || cursorInWindow.X >= ActualWidth - resizeBorder ||
                              cursorInWindow.Y <= resizeBorder || cursorInWindow.Y >= ActualHeight - resizeBorder);
        var overGear = gearButton.IsVisible &&
                       new Rect(0, 0, gearButton.ActualWidth, gearButton.ActualHeight)
                           .Contains(gearButton.PointFromScreen(cursorScreenPoint));
        if (_cursorOverChatOnlyGear != overGear || _cursorOverResizeEdge != overResizeEdge)
        {
            _cursorOverChatOnlyGear = overGear;
            _cursorOverResizeEdge = overResizeEdge;
            UpdateClickThroughStyle();
        }
    }

}
