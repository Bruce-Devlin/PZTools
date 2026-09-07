using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PZTools.Core.Functions.Tester;

internal static class GameTestStartupClick
{
    // GLFW handles these window messages as mouse input. Address only the owned
    // renderer, including when it is a child of the editor's docked game host.
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
        const int point = (32 << 16) | 32;
        PostMessage(renderer, 0x0201, 1, point);
        try { await Task.Delay(100, ct); }
        finally { PostMessage(renderer, 0x0202, 0, point); }
    }

    private delegate bool WindowEnumerator(nint window, nint parameter);
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out Bounds bounds);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowEnumerator callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, WindowEnumerator callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
