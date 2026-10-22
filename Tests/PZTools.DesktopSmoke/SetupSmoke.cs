using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PZTools;
using PZTools.Core.Windows.Dialogs;

internal static class SetupSmoke
{
    public static int Run()
    {
        var originalDirectory = AppPaths.CurrentDirectoryPath;
        var root = Path.Combine(Path.GetTempPath(), "PZTools-Setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AppPaths.SetCurrentDirectory(root);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PZTools;component/Core/Windows/Themes/Dark.xaml", UriKind.Relative) });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PZTools;component/Core/Windows/Styles/AppStyles.xaml", UriKind.Relative) });
        try
        {
            CheckClose("window close", w => w.Close());
            CheckClose("Cancel button", w => Invoke(w, "CancelButton_Click"));
            CheckClose("decline then accept", w => w.Close(), declineFirst: true);
            CheckClose("pending setup", w => w.Close(), pending: true);
            CheckValidation(root, managed: true);
            CheckValidation(root, managed: false);
            Console.WriteLine("Setup smoke checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            AppPaths.SetCurrentDirectory(originalDirectory);
            app.Shutdown();
        }
    }

    private static void CheckClose(string name, Action<AppSetup> action, bool declineFirst = false, bool pending = false)
    {
        var prompts = 0;
        var declinedStayedOpen = !declineFirst;
        var completion = new TaskCompletionSource<bool>();
        RunDialog(w =>
        {
            if (pending) Field(w, "_setupTask").SetValue(w, completion.Task);
            action(w);
        }, (w, title, handle) =>
        {
            prompts++;
            var decline = declineFirst && prompts == 1;
            Respond(handle, decline ? 7 : 6); // IDNO / IDYES
            if (decline)
                w.Dispatcher.BeginInvoke(() =>
                {
                    declinedStayedOpen = w.IsVisible;
                    action(w);
                }, DispatcherPriority.ApplicationIdle);
            else if (pending)
                w.Dispatcher.BeginInvoke(() =>
                {
                    // Repeated close requests while cancellation is waiting must not prompt again.
                    w.Close();
                    completion.TrySetResult(false);
                }, DispatcherPriority.ApplicationIdle);
        });
        Check(prompts == (declineFirst ? 2 : 1) && declinedStayedOpen, name + " confirms once per request");
    }

    private static void CheckValidation(string root, bool managed)
    {
        var install = Directory.CreateDirectory(Path.Combine(root, managed ? "managed-app" : "existing-app")).FullName;
        var reachedSteam = false;
        var rejectedMissingExecutable = false;
        RunDialog(w =>
        {
            ((TextBox)w.FindName("txtAppInstallPath")).Text = install;
            ((RadioButton)w.FindName(managed ? "rdoManagedGame" : "rdoExistingGame")).IsChecked = true;
            ((TextBox)w.FindName("txtExistingGamePath")).Text = managed ? "" : root;
            Invoke(w, "FinishButton_Click");
            Field(w, "_allowClose").SetValue(w, true);
            w.DialogResult = false;
        }, (w, title, handle) =>
        {
            rejectedMissingExecutable |= title == "Invalid Game Folder";
            Respond(handle, 1); // IDOK
        }, w =>
        {
            var login = Application.Current.Windows.OfType<SteamLogin>().FirstOrDefault();
            if (login != null)
            {
                reachedSteam = true;
                // Let the timer return before closing a nested modal dialog.
                login.Dispatcher.BeginInvoke(() => login.DialogResult = false);
            }
        });
        Check(managed ? reachedSteam && !rejectedMissingExecutable : rejectedMissingExecutable,
            managed ? "managed setup reaches Steam without an existing game" : "existing setup rejects a missing executable");
    }

    private static void RunDialog(Action<AppSetup> action, Action<AppSetup, string, nint> message, Action<AppSetup>? tick = null)
    {
        var window = new AppSetup();
        var timedOut = false;
        Exception? error = null;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var thread = GetCurrentThreadId();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        DispatcherUnhandledExceptionEventHandler onError = (_, e) => { error = e.Exception; e.Handled = true; };
        Application.Current.DispatcherUnhandledException += onError;
        timer.Tick += (_, _) =>
        {
            EnumThreadWindows(thread, (handle, _) =>
            {
                var className = new StringBuilder(256);
                GetClassName(handle, className, className.Capacity);
                if (className.ToString() != "#32770" || !IsWindowVisible(handle)) return true;
                var title = new StringBuilder(256);
                GetWindowText(handle, title, title.Capacity);
                message(window, title.ToString(), handle);
                return true;
            }, 0);
            tick?.Invoke(window);
            if (DateTime.UtcNow < deadline && error == null) return;
            timedOut = error == null;
            Field(window, "_allowClose").SetValue(window, true);
            window.Close();
        };
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() => action(window));
        timer.Start();
        try { window.ShowDialog(); }
        finally
        {
            timer.Stop();
            Application.Current.DispatcherUnhandledException -= onError;
        }
        if (error != null) throw new InvalidOperationException("Setup raised a dispatcher error.", error);
        Check(!timedOut, "setup closes after the confirmed request");
    }

    private static FieldInfo Field(AppSetup window, string name) => window.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void Invoke(AppSetup window, string name) => window.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { window, new RoutedEventArgs() });
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private static void Respond(nint handle, int button)
    {
        var control = GetDlgItem(handle, button);
        if (control == 0 && button == 1) control = GetDlgItem(handle, 2); // OK-only message boxes may use IDCANCEL.
        if (control != 0) SendMessage(control, 0x00F5, 0, 0); // BM_CLICK
    }
    private delegate bool EnumWindow(nint handle, nint state);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, nint state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint handle, StringBuilder value, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder value, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern nint GetDlgItem(nint handle, int id);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint handle, uint message, nint wParam, nint lParam);
}
