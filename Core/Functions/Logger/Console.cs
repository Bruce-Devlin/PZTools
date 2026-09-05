using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PZTools.Core.Functions.Logger
{
    public static class Console
    {
        public enum LogLevel
        {
            Info,
            Warning,
            Error,
            Debug
        }

        private static readonly List<string> cliCache = new();
        private static readonly object Sync = new();
        public static string[] GetAllMessages()
        {
            lock (Sync)
                return cliCache.ToArray();
        }
        public static event EventHandler<string>? OnLogMessage;

        public static Task Log(string message, LogLevel level = LogLevel.Info, string? title = null) => Log(null, message, level, title);

        public static Task Log(this object? callingObj, string message, LogLevel level = LogLevel.Info, string? title = null)
        {
            string objMethodName = "";
            var callingObjMethod = new StackFrame(1, true).GetMethod();
            if (callingObj != null)
            {
                Type objType = callingObj.GetType();
                objMethodName = objType.Name;
            }
            else
                objMethodName = "Task";

            string timestamp = string.Format("{0:dd-MM-yyyy | hh-mm-ss}", DateTime.Now);
            string messageStamp = $"[{timestamp}] TestFrameworker";

            string titleHelper = "";

            if (title != null)
                titleHelper += $"{title.ToUpper()}";
            else if (objMethodName != "")
                titleHelper += $"{objMethodName}";

            string messageLogged = FormatLogMessage(message, level, titleHelper);
            Debug.WriteLine(messageLogged);
            if (!LogHelper.IsHidden)
            {
                if (level != LogLevel.Debug)
                    System.Console.WriteLine(messageLogged);

                if (App.IsDebug && level == LogLevel.Error)
                {
                    System.Console.WriteLine(FormatLogMessage("Press any key to continue...", level, titleHelper));
                    System.Console.ReadKey();
                }
                else if (level == LogLevel.Error)
                {
                    MessageBox.Show(messageLogged, "PZTools Error", MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            LogToFile(messageLogged);

            lock (Sync)
                cliCache.Add(messageLogged);
            OnLogMessage?.Invoke(null, messageLogged);
            return Task.CompletedTask;
        }


        private static bool loggerStarted = false;
        private static void LogToFile(string message)
        {
            var currDir = Directory.GetParent(AppPaths.CurrentFilePath);
            var logFilePath = Path.Combine(currDir?.FullName ?? AppContext.BaseDirectory, "PZTools.log");
            if (!File.Exists(logFilePath))
                File.Create(logFilePath).Dispose();
            if (!loggerStarted)
            {
                using (StreamWriter sw = new StreamWriter(logFilePath, true))
                {
                    sw.WriteLine("\r\n" +
                        "===========================\r\n" +
                        "    Project Zomboid Tools  \r\n" +
                        "      by Bruce Devlin      \r\n" +
                        "===========================\r\n"
                    );
                    loggerStarted = true;
                }
            }

            try
            {
                lock (Sync)
                {
                    using StreamWriter sw = new StreamWriter(logFilePath, true);
                    sw.WriteLine(message);
                }
            }
            catch
            {
            }
        }

        private static string FormatLogMessage(string messageToFormat, LogLevel type, string title)
        {
            string timestamp = string.Format("{0:yyyy-MM-dd | hh-mm-ss}", DateTime.Now);
            string messageStamp = $"[{timestamp}] PZTools";

            if (!string.IsNullOrEmpty(title))
                messageStamp += $" [{title.ToUpper()}]";
            messageStamp += $" [{type.ToString()}]";
            string prefix = "";

            string formattedMessage = $"{prefix}{messageStamp}: {messageToFormat}";
            return formattedMessage;
        }

    }
}
