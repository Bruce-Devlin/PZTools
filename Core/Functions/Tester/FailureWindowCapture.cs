using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using PZTools.Core.Models.Test;

namespace PZTools.Core.Functions.Tester;

internal static class FailureWindowCapture
{
    internal static void Capture(Process process, string directory, CaseDiagnostic diagnostic)
    {
        if (process.HasExited) return;
        diagnostic.ProcessId = process.Id;
        var window = process.MainWindowHandle;
        if (window == 0 || !GetWindowRect(window, out var rect)) return;
        GetWindowThreadProcessId(window, out var owner);
        if (owner != process.Id || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return;
        diagnostic.Window = "0x" + window.ToString("X");
        using var bitmap = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);
        using var graphics = Graphics.FromImage(bitmap);
        var dc = graphics.GetHdc();
        bool captured;
        try { captured = PrintWindow(window, dc, 2); }
        finally { graphics.ReleaseHdc(dc); }
        if (!captured) { diagnostic.CaptureNote = "Windows could not capture the owned client window."; return; }
        var path = Path.Combine(directory, $"failure-{process.Id}-{Guid.NewGuid():N}.png");
        bitmap.Save(path, ImageFormat.Png);
        diagnostic.Screenshot = path;
        diagnostic.CaptureNote = "Owned-window PrintWindow capture; OpenGL content may be blank. No desktop capture was taken.";
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint window, nint dc, uint flags);
}
