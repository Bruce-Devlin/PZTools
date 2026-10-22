using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PZTools.Core.Functions.Tester;

internal static class GameTestStartupClick
{
    private static readonly SemaphoreSlim InputGate = new(1, 1);
    // GLFW polls physical button state; posting WM_LBUTTONDOWN alone is not
    // sufficient. Only send input after foreground ownership and hit testing
    // confirm the owned renderer (including a docked child) is under the cursor.
    internal static async Task ClickAsync(Process process, CancellationToken ct)
    {
        if (process.HasExited) return;
        nint renderer = 0;
        long largestArea = 0;
        bool Find(nint window, nint parameter)
        {
            GetWindowThreadProcessId(window, out var pid);
            if (pid != process.Id || !IsWindowVisible(window) || !GetClientRect(window, out var bounds)) return true;
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            if (name.ToString().Contains("GLFW", StringComparison.OrdinalIgnoreCase) ||
                name.ToString().Contains("LWJGL", StringComparison.OrdinalIgnoreCase))
            {
                var area = (long)bounds.Right * bounds.Bottom;
                if (bounds.Right >= 160 && bounds.Bottom >= 120 && area > largestArea)
                {
                    renderer = window;
                    largestArea = area;
                }
            }
            return true;
        }
        EnumWindows((window, parameter) =>
        {
            Find(window, parameter);
            EnumChildWindows(window, Find, 0);
            return true;
        }, 0);
        if (renderer == 0) return;
        await InputGate.WaitAsync(ct);
        try
        {
            var root = GetAncestor(renderer, 2);
            if (IsIconic(root)) ShowWindow(root, 9);
            SetForegroundWindow(root);
            if (GetForegroundWindow() != root) return;
            var point = new Point { X = 32, Y = 32 };
            if (!ClientToScreen(renderer, ref point)) return;
            if (WindowFromPoint(point) != renderer) return;
            GetCursorPos(out var previous);
            SetCursorPos(point.X, point.Y);
            try
            {
                if (GetForegroundWindow() != root || WindowFromPoint(point) != renderer) return;
                SendMouse(0x0002);
                try { await Task.Delay(100, ct); }
                finally { SendMouse(0x0004); }
            }
            finally { SetCursorPos(previous.X, previous.Y); }
        }
        finally { InputGate.Release(); }
    }

    private static void SendMouse(uint flags)
    {
        var input = new Input { Mouse = new MouseInput { Flags = flags } };
        if (SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) != 1)
            throw new InvalidOperationException("Windows rejected automatic startup input to the owned game client.");
    }

    private delegate bool WindowEnumerator(nint window, nint parameter);
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public MouseInput Mouse; }
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out Bounds bounds);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowEnumerator callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, WindowEnumerator callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
