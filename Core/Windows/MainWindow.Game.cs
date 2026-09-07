using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using PZTools.Core.Controls;
using PZTools.Core.Windows.Dialogs.Project;

namespace PZTools.Core.Windows;

public partial class MainWindow
{
    private readonly GameWindowHost _gameHost = new();
    private readonly DispatcherTimer _gameTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Process? _panelGameProcess;
    private nint _gameWindow;
    private bool _wantDock;
    private nint _candidateWindow;
    private DateTime _candidateSince;
    private DateTime _dockDeadline;
    private RunProject? _playtestWindow;
    private TestExplorer? _testExplorerWindow;

    public void ShowTestExplorer(PZTools.Core.Models.ModProject project)
    {
        if (_testExplorerWindow is null)
        {
            _testExplorerWindow = new TestExplorer(project) { Owner = this, DockClient = TrackGameClient };
            _testExplorerWindow.Closed += (_, _) => _testExplorerWindow = null;
            _testExplorerWindow.Show();
        }
        _testExplorerWindow.WindowState = WindowState.Normal;
        _testExplorerWindow.Activate();
    }
    private HwndSource? _gameHotkeySource;
    private const int GameFocusHotkey = 0x505A;

    private void InitializeGamePanel()
    {
        GameHostContainer.Child = _gameHost;
        _gameHost.Visibility = Visibility.Hidden;
        _gameTimer.Tick += (_, _) => PollGameWindow();
        _gameTimer.Start();
        SourceInitialized += (_, _) =>
        {
            _gameHotkeySource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _gameHotkeySource?.AddHook(GameFocusHook);
        };
    }

    private nint GameFocusHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam == GameFocusHotkey)
        {
            SetForegroundWindow(hwnd);
            UndockGameButton.Focus();
            handled = true;
        }
        return 0;
    }

    public void TrackGameClient(Process process)
    {
        if (IsClosing) return;
        _gameHost.Detach();
        _panelGameProcess = process;
        _gameWindow = 0;
        _candidateWindow = 0;
        _wantDock = true;
        _dockDeadline = DateTime.UtcNow.AddMinutes(3);
        WorkspaceTabs.SelectedIndex = 1;
        GameStatus.Text = "Waiting for the game window…";
        Activate();
    }

    private void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewPane is null || GamePane is null) return;
        var game = WorkspaceTabs.SelectedIndex == 1;
        PreviewPane.Visibility = game ? Visibility.Collapsed : Visibility.Visible;
        GamePane.Visibility = game ? Visibility.Visible : Visibility.Collapsed;
        UpdateGameHotkey();
    }

    private void PollGameWindow()
    {
        if (_panelGameProcess is null) return;
        try
        {
            if (_panelGameProcess.HasExited)
            {
                _gameHost.Detach();
                _panelGameProcess = null;
                _gameWindow = 0;
                _wantDock = false;
                GameStatus.Text = "Game exited. Run another playtest to continue.";
            }
            else
            {
                var candidate = FindGameWindow(_panelGameProcess.Id, _gameHost.GameHandle);
                if (candidate != _candidateWindow)
                {
                    _candidateWindow = candidate;
                    _candidateSince = DateTime.UtcNow;
                }
                // Wait for a stable rendering window, not the launcher's first visible HWND.
                if (candidate != 0 && DateTime.UtcNow - _candidateSince >= TimeSpan.FromSeconds(2))
                    _gameWindow = candidate;
                else if (!_gameHost.IsAttached)
                    _gameWindow = 0;

                if (_wantDock && _gameWindow != 0 && _gameHost.Handle != 0 &&
                    (!_gameHost.IsAttached || _gameHost.GameHandle != _gameWindow))
                {
                    _gameHost.Visibility = Visibility.Visible;
                    _gameHost.Attach(_gameWindow);
                    GameStatus.Text = "";
                }
                else if (_wantDock && !_gameHost.IsAttached && DateTime.UtcNow > _dockDeadline)
                {
                    _wantDock = false;
                    GameStatus.Text = "The game window was not found. Keep playing externally, or click Dock to retry.";
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _wantDock = false;
            GameStatus.Text = $"Could not dock the game: {ex.Message}. Use its normal window or retry Dock.";
            if (ex is ObjectDisposedException) _panelGameProcess = null;
        }
        if (_wantDock && _gameHost.IsAttached) _gameHost.MaintainLayout();
        _gameHost.Visibility = _gameHost.IsAttached ? Visibility.Visible : Visibility.Hidden;
        DockGameButton.IsEnabled = _panelGameProcess is not null && !_gameHost.IsAttached;
        UndockGameButton.IsEnabled = _gameHost.IsAttached;
        GameStatus.Visibility = _gameHost.IsAttached ? Visibility.Collapsed : Visibility.Visible;
        UpdateGameHotkey();
    }

    private static nint FindGameWindow(int processId, nint attached)
    {
        // Keep an attached HWND in the search: EnumWindows/MainWindowHandle omit child windows.
        nint best = 0;
        long bestScore = 0;
        void Consider(nint hwnd)
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != processId || (!NativeIsWindowVisible(hwnd) && hwnd != attached)) return;
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            var windowClass = name.ToString();
            if (windowClass.Contains("Splash", StringComparison.OrdinalIgnoreCase) ||
                windowClass is "ConsoleWindowClass" or "IME" || !NativeGetClientRect(hwnd, out var bounds)) return;
            if (bounds.Right < 160 || bounds.Bottom < 120) return;
            var renderer = windowClass.Contains("GLFW", StringComparison.OrdinalIgnoreCase) ||
                windowClass.Contains("LWJGL", StringComparison.OrdinalIgnoreCase);
            var score = (renderer ? 1L << 40 : 0) + (long)bounds.Right * bounds.Bottom;
            if (score > bestScore) { bestScore = score; best = hwnd; }
        }
        if (attached != 0) Consider(attached);
        EnumWindows((hwnd, _) => { Consider(hwnd); return true; }, 0);
        return best;
    }

    private delegate bool WindowEnumerator(nint hwnd, nint parameter);
    [StructLayout(LayoutKind.Sequential)] private struct WindowBounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowEnumerator callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int count);
    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")] private static extern bool NativeIsWindowVisible(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetClientRect")] private static extern bool NativeGetClientRect(nint hwnd, out WindowBounds bounds);

    private void DockGame_Click(object sender, RoutedEventArgs e)
    {
        _wantDock = true;
        _dockDeadline = DateTime.UtcNow.AddMinutes(3);
        PollGameWindow();
    }

    private void UndockGame_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _gameHost.Detach();
            _wantDock = false;
            GameStatus.Text = "Game is running in its own window. Click Dock to bring it back.";
            SetForegroundWindow(_gameWindow);
        }
        catch (System.ComponentModel.Win32Exception ex) { GameStatus.Text = ex.Message; }
        PollGameWindow();
    }

    private void UpdateGameHotkey()
    {
        if (_gameHotkeySource is null) return;
        UnregisterHotKey(_gameHotkeySource.Handle, GameFocusHotkey);
        if (_gameHost.IsAttached && WorkspaceTabs.SelectedIndex == 1)
            RegisterHotKey(_gameHotkeySource.Handle, GameFocusHotkey, 0x4006, 0x7B);
    }

    private void DisposeGamePanel()
    {
        _gameHost.Detach();
        _gameTimer.Stop();
        if (_gameHotkeySource is not null)
        {
            UnregisterHotKey(_gameHotkeySource.Handle, GameFocusHotkey);
            _gameHotkeySource.RemoveHook(GameFocusHook);
        }
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
