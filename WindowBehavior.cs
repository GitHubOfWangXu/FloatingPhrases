using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;

namespace FloatingPhrases;

public sealed class WindowBehavior
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;

    private IntPtr _handle;
    private readonly Window _window;

    public WindowBehavior(Window window)
    {
        _window = window;
        // DragMove uses the shell move loop. A resizable HWND opts into Aero Snap,
        // which competes with our own edge docking when released at the top.
        window.ResizeMode = ResizeMode.CanMinimize;
        // StateChanged depends on native window notifications and is not reliable
        // for a hidden HWND. Observe the WPF property itself, even before Show().
        var stateProperty = DependencyPropertyDescriptor.FromProperty(Window.WindowStateProperty, typeof(Window));
        EventHandler restoreNormalState = (_, _) =>
        {
            if (window.WindowState == WindowState.Maximized)
                window.WindowState = WindowState.Normal;
        };
        stateProperty.AddValueChanged(window, restoreNormalState);
        window.Closed += (_, _) => stateProperty.RemoveValueChanged(window, restoreNormalState);
    }

    public void Attach(IntPtr handle) => _handle = handle;

    public void Apply(WindowMode mode, double idleOpacity, bool pointerInside)
    {
        SetClickThrough(mode == WindowMode.ClickThrough);
        _window.Topmost = mode != WindowMode.Normal;
        _window.Opacity = mode switch
        {
            WindowMode.Normal => 1,
            WindowMode.Floating when pointerInside => 1,
            _ => Math.Clamp(idleOpacity, 0.2, 0.9)
        };
    }

    private void SetClickThrough(bool enabled)
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var styles = GetExtendedStyle(_handle);
        styles = enabled
            ? styles | WsExTransparent | WsExLayered
            : styles & ~WsExTransparent;
        SetExtendedStyle(_handle, styles);
    }

    private static int GetExtendedStyle(IntPtr handle) => IntPtr.Size == 8
        ? (int)GetWindowLongPtr64(handle, GwlExStyle)
        : GetWindowLong32(handle, GwlExStyle);

    private static void SetExtendedStyle(IntPtr handle, int styles)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(handle, GwlExStyle, new IntPtr(styles));
        }
        else
        {
            SetWindowLong32(handle, GwlExStyle, styles);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
