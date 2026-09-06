using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PZTools.Core.Controls;

// Own only the container HWND. The game remains owned by its original process.
public sealed class GameWindowHost : HwndHost
{
    private Window? _owner;

    public GameWindowHost()
    {
        Loaded += (_, _) =>
        {
            var owner = Window.GetWindow(this);
            if (owner == _owner) return;
            if (_owner is not null) _owner.Closing -= OwnerClosing;
            _owner = owner;
            if (_owner is not null) _owner.Closing += OwnerClosing;
        };
    }

    private void OwnerClosing(object? sender, CancelEventArgs e)
    {
        if (e.Cancel) return;
        try { Detach(); }
        catch (Win32Exception) { e.Cancel = true; }
    }

    private nint _game;
    private nint _parent;
    private nint _style;
    private RectI _bounds;
    public bool IsAttached => _game != 0 && IsWindow(_game);
    public nint GameHandle => IsAttached ? _game : 0;

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        var hwnd = CreateWindowEx(0, "static", "", 0x56000000, 0, 0, 1, 1, parent.Handle, 0, 0, 0);
        if (hwnd == 0) throw new Win32Exception();
        return new HandleRef(this, hwnd);
    }

    public void Attach(nint game)
    {
        if (Handle == 0 || !IsWindow(game)) throw new InvalidOperationException("The game window is not ready.");
        if (IsAttached) Detach();
        _game = game;
        _parent = GetParent(game);
        _style = GetWindowLongPtr(game, -16);
        GetWindowRect(game, out _bounds);
        try
        {
            SetStyle(game, (_style.ToInt64() & ~0xA1CF0000L) | 0x46000000L);
            Marshal.SetLastPInvokeError(0);
            if (SetParent(game, Handle) == 0 && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception();
            ResizeGame();
            ShowWindow(game, 5);
        }
        catch
        {
            Detach();
            throw;
        }
    }

    public void Detach()
    {
        if (!IsAttached) { _game = 0; return; }
        // Restore before destroying the host; destroying a parent also destroys its children.
        Marshal.SetLastPInvokeError(0);
        if (SetParent(_game, _parent) == 0 && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception();
        SetStyle(_game, (_style.ToInt64() & ~0x40000000L) | 0x00CF0000L);
        SetWindowPos(_game, 0, _bounds.Left, _bounds.Top,
            Math.Max(320, _bounds.Right - _bounds.Left), Math.Max(240, _bounds.Bottom - _bounds.Top), 0x0064);
        ShowWindow(_game, 5);
        _game = 0;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        ResizeGame();
    }

    // GLFW can move/resize its window again after initial display setup. Repair that
    // drift without sending continuous WM_SIZE/WM_WINDOWPOSCHANGED to the renderer.
    public void MaintainLayout()
    {
        if (!IsAttached) return;
        var style = GetWindowLongPtr(_game, -16).ToInt64();
        if ((style & 0x40000000L) == 0 || (style & 0xA1CF0000L) != 0)
            SetStyle(_game, (style & ~0xA1CF0000L) | 0x46000000L);
        ResizeGame();
        if (IsVisible && !IsWindowVisible(_game)) ShowWindow(_game, 5);
    }

    private void ResizeGame()
    {
        if (!IsAttached || !GetClientRect(Handle, out var rect)) return;
        GetWindowRect(_game, out var current);
        var origin = new PointI { X = current.Left, Y = current.Top };
        ScreenToClient(Handle, ref origin);
        var width = Math.Max(1, rect.Right);
        var height = Math.Max(1, rect.Bottom);
        if (origin.X != 0 || origin.Y != 0 || current.Right - current.Left != width || current.Bottom - current.Top != height)
            SetWindowPos(_game, 0, 0, 0, width, height, 0x0034);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        Detach();
        if (_owner is not null) _owner.Closing -= OwnerClosing;
        _owner = null;
        DestroyWindow(hwnd.Handle);
    }

    private static void SetStyle(nint hwnd, long style)
    {
        Marshal.SetLastPInvokeError(0);
        if (SetWindowLongPtr(hwnd, -16, (nint)style) == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception();
    }

    // The game and PZTools Windows distributions are 64-bit.
    [StructLayout(LayoutKind.Sequential)] private struct PointI { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint hwnd, ref PointI point);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(int ex, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetParent(nint hwnd, nint parent);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RectI rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out RectI rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
}
