using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows;

namespace PZTools.Core.Functions
{
    static class WindowsHelpers
    {
        public static bool ShowDialog(this System.Windows.Window parentWindow, System.Windows.Window child)
        {
            System.Windows.Window window = child;
            window.Owner = parentWindow;
            var result = window.ShowDialog();
            if (result.HasValue && result is bool) return (bool)result;
            else return true;
        }

        public static void CreateShortcut(string shortcutPath, string targetPath, string description)
        {
            var shell = new IWshRuntimeLibrary.WshShell();
            var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.Description = description;
            shortcut.Save();
        }

        public static bool NotNullOrEmpty(this string value)
        {
            return !string.IsNullOrEmpty(value);
        }

        public static string ShowInExplorer(string filePath)
        {
            try
            {
                filePath = Path.GetFullPath(filePath);

                if (File.Exists(filePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{filePath}\"",
                        UseShellExecute = true
                    });

                    return "File selected in Explorer.";
                }

                if (Directory.Exists(filePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{filePath}\"",
                        UseShellExecute = true
                    });

                    return "Directory opened in Explorer.";
                }

                return "Path does not exist.";
            }
            catch (Exception ex)
            {
                return $"Failed to open Explorer: {ex.Message}";
            }
        }

        public static string OpenFile(string filePath, string appPath = "", string appArgs = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(appPath))
                {
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = filePath,
                            UseShellExecute = true
                        }
                    };

                    process.Start();
                    return string.Empty;
                }
                else
                {
                    var quotedFilePath = $"\"{filePath}\"";

                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = appPath,
                            Arguments = string.IsNullOrWhiteSpace(appArgs)
                                ? quotedFilePath
                                : $"{appArgs} {quotedFilePath}",
                            UseShellExecute = false
                        }
                    };

                    process.Start();
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }


        public static string OpenFolderBrowser(string description)
        {
            using (var Dialogs = new FolderBrowserDialog())
            {
                Dialogs.Description = description;
                Dialogs.ShowNewFolderButton = true;
                if (Dialogs.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    return Dialogs.SelectedPath;
                }
            }
            return null;
        }

        public static void CopyDirectory(string sourceDir, string destDir)
        {
            var src = new DirectoryInfo(sourceDir);
            if (!src.Exists) throw new DirectoryNotFoundException(sourceDir);

            Directory.CreateDirectory(destDir);

            foreach (var file in src.GetFiles())
            {
                var destFile = Path.Combine(destDir, file.Name);
                file.CopyTo(destFile, true);
            }

            foreach (var sub in src.GetDirectories())
            {
                CopyDirectory(sub.FullName, Path.Combine(destDir, sub.Name));
            }
        }

        public static void ClearReadOnlyRecursive(string path)
        {
            var root = new DirectoryInfo(path);
            if (!root.Exists) return;

            foreach (var dir in root.EnumerateDirectories("*", SearchOption.AllDirectories))
                dir.Attributes = FileAttributes.Normal;

            foreach (var file in root.EnumerateFiles("*", SearchOption.AllDirectories))
                file.Attributes = FileAttributes.Normal;

            root.Attributes = FileAttributes.Normal;
        }

        public static void DeleteDirectoryRobust(string path, int retries = 6, int delayMs = 80)
        {
            if (!Directory.Exists(path)) return;

            ClearReadOnlyRecursive(path);

            for (int i = 0; i < retries; i++)
            {
                try
                {
                    Directory.Delete(path, true);
                    return;
                }
                catch (UnauthorizedAccessException) when (i < retries - 1)
                {
                    Thread.Sleep(delayMs);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                catch (IOException) when (i < retries - 1)
                {
                    Thread.Sleep(delayMs);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }

            // One final attempt that will throw with the real error
            Directory.Delete(path, true);
        }

        public static bool IsBeingFreeDragged(this Window window)
        {
            return draggingWindow;
        }

        private static bool draggingWindow = false;
        public static void FreeDragThisWindow(this Window window)
        {
            window.MouseLeftButtonDown += delegate { DraggyWindows(window); };
            window.MouseLeftButtonUp += delegate { DoneDragWindow(window); };
        }

        private static void DraggyWindows(Window window)
        {
            draggingWindow = true;
            window.DragMove();
            draggingWindow = false;
        }

        private static void DoneDragWindow(Window window)
        {
            if (draggingWindow)
            {
                window.MoveOnScreen();
            }
        }

        public static void MoveOnScreen(this Window window)
        {
            if (window.Top < SystemParameters.VirtualScreenTop)
            {
                window.Top = SystemParameters.VirtualScreenTop;
            }

            if (window.Left < SystemParameters.VirtualScreenLeft)
            {
                window.Left = SystemParameters.VirtualScreenLeft;
            }

            if (window.Left + window.Width > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth)
            {
                window.Left = SystemParameters.VirtualScreenWidth + SystemParameters.VirtualScreenLeft - window.Width;
            }

            if (window.Top + window.Height > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            {
                window.Top = SystemParameters.VirtualScreenHeight + SystemParameters.VirtualScreenTop - window.Height;
            }

            var taskBarLocation = GetTaskBarLocationPerScreen();

            foreach (var taskBar in taskBarLocation)
            {
                Rectangle windowRect = new Rectangle((int)window.Left, (int)window.Top, (int)window.Width, (int)window.Height);

                int avoidInfiniteLoopCounter = 25;
                while (windowRect.IntersectsWith(taskBar))
                {
                    avoidInfiniteLoopCounter--;
                    if (avoidInfiniteLoopCounter == 0)
                    {
                        break;
                    }

                    var intersection = Rectangle.Intersect(taskBar, windowRect);

                    if (intersection.Width < window.Width || taskBar.Contains(windowRect))
                    {
                        if (taskBar.Left == 0)
                        {
                            window.Left = window.Left + intersection.Width;
                        }
                        else
                        {
                            window.Left = window.Left - intersection.Width;
                        }
                    }

                    if (intersection.Height < window.Height || taskBar.Contains(windowRect))
                    {
                        if (taskBar.Top == 0)
                        {
                            window.Top = window.Top + intersection.Height;
                        }
                        else
                        {
                            window.Top = window.Top - intersection.Height;
                        }
                    }

                    windowRect = new Rectangle((int)window.Left, (int)window.Top, (int)window.Width, (int)window.Height);
                }
            }
        }

        public static List<Rectangle> GetTaskBarLocationPerScreen()
        {
            List<Rectangle> dockedRects = new List<Rectangle>();
            foreach (var screen in Screen.AllScreens)
            {
                if (screen.Bounds.Equals(screen.WorkingArea) == true)
                {
                    continue;
                }

                Rectangle rect = new Rectangle();

                var leftDockedWidth = Math.Abs((Math.Abs(screen.Bounds.Left) - Math.Abs(screen.WorkingArea.Left)));
                var topDockedHeight = Math.Abs((Math.Abs(screen.Bounds.Top) - Math.Abs(screen.WorkingArea.Top)));
                var rightDockedWidth = ((screen.Bounds.Width - leftDockedWidth) - screen.WorkingArea.Width);
                var bottomDockedHeight = ((screen.Bounds.Height - topDockedHeight) - screen.WorkingArea.Height);
                if ((leftDockedWidth > 0))
                {
                    rect.X = screen.Bounds.Left;
                    rect.Y = screen.Bounds.Top;
                    rect.Width = leftDockedWidth;
                    rect.Height = screen.Bounds.Height;
                }
                else if ((rightDockedWidth > 0))
                {
                    rect.X = screen.WorkingArea.Right;
                    rect.Y = screen.Bounds.Top;
                    rect.Width = rightDockedWidth;
                    rect.Height = screen.Bounds.Height;
                }
                else if ((topDockedHeight > 0))
                {
                    rect.X = screen.WorkingArea.Left;
                    rect.Y = screen.Bounds.Top;
                    rect.Width = screen.WorkingArea.Width;
                    rect.Height = topDockedHeight;
                }
                else if ((bottomDockedHeight > 0))
                {
                    rect.X = screen.WorkingArea.Left;
                    rect.Y = screen.WorkingArea.Bottom;
                    rect.Width = screen.WorkingArea.Width;
                    rect.Height = bottomDockedHeight;
                }
                else
                {
                }

                dockedRects.Add(rect);
            }

            if (dockedRects.Count == 0)
            {
            }

            return dockedRects;
        }

        public static string GetTargetFrameworkMoniker()
        {
            var assembly = Assembly.GetEntryAssembly();
            var attribute = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            if (attribute == null) return "unknown";

            string frameworkName = attribute.FrameworkName;
            // Example: ".NETCoreApp,Version=v10.0,Profile=Windows"

            // Split by commas
            var parts = frameworkName.Split(',');
            if (parts.Length < 2) return "unknown";

            // Map base framework
            string baseFramework = parts[0].Trim(); // ".NETCoreApp" or ".NETFramework"
            string tfm = "";

            switch (baseFramework)
            {
                case ".NETCoreApp":
                    tfm = "net";
                    break;
                case ".NETFramework":
                    tfm = "netframework";
                    break;
                case ".NETStandard":
                    tfm = "netstandard";
                    break;
                default:
                    tfm = baseFramework.ToLowerInvariant();
                    break;
            }

            string versionPart = parts[1].Trim();
            if (versionPart.StartsWith("Version=v"))
            {
                string version = versionPart.Substring("Version=v".Length);
                tfm += version;
            }

            if (parts.Length >= 3)
            {
                string profile = parts[2].Trim(); // "Profile=Windows"
                if (profile.StartsWith("Profile=", StringComparison.OrdinalIgnoreCase))
                {
                    string profileName = profile.Substring("Profile=".Length).ToLowerInvariant();
                    tfm += "-" + profileName;
                }
            }

            return tfm; // e.g. "net10.0-windows"
        }
    }
}
