using System.Diagnostics;
using System.IO;

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

        private static List<string> cliCache = new List<string>();
        public static string[] GetAllMessages() { return cliCache.ToArray(); }
        public static event EventHandler<string> OnLogMessage;

        public static async Task Log(string message, LogLevel level = LogLevel.Info, string title = null) => await Log(null, message, level, title);

        public static async Task Log(this object? callingObj, string message, LogLevel level = LogLevel.Info, string title = null)
        {
            string objMethodName = "";
            var callingObjMethod = new StackFrame(1, true).GetMethod();
            if (callingObj != null)
            {
                Type objType = callingObj.GetType();
                objMethodName = objType.Name;
            }
            else objMethodName = "Task";

            string timestamp = string.Format("{0:dd-MM-yyyy | hh-mm-ss}", DateTime.Now);
            string messageStamp = $"[{timestamp}] TestFrameworker";

            string titleHelper = "";

            if (title != null) titleHelper += $"{title.ToUpper()}";
            else if (objMethodName != "") titleHelper += $"{objMethodName}";

            string messageLogged = FormatLogMessage(message, level, titleHelper);
            Debug.WriteLine(messageLogged);
            if (!LogHelper.IsHidden)
            {
                if (level != LogLevel.Debug) System.Console.WriteLine(messageLogged);

                if (level == LogLevel.Error)
                {
                    System.Console.WriteLine(FormatLogMessage("Press any key to continue...", level, titleHelper));
                    System.Console.ReadKey();
                }
            }
            LogToFile(messageLogged);

            if (OnLogMessage != null) OnLogMessage.Invoke(null, messageLogged);
            cliCache.Add(messageLogged);
        }


        private static bool loggerStarted = false;
        private static void LogToFile(string message)
        {
            var currDir = Directory.GetParent(AppPaths.CurrentFilePath);
            var logFilePath = Path.Combine(currDir.FullName, "PZTools.log");
            if (!File.Exists(logFilePath)) File.Create(logFilePath).Dispose();
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
                using (StreamWriter sw = new StreamWriter(logFilePath, true))
                {
                    sw.WriteLine(message);
                }
            }
            catch
            {
                // Ignore logging errors
            }
        }

        private static string FormatLogMessage(string messageToFormat, LogLevel type, string title)
        {
            string timestamp = string.Format("{0:yyyy-MM-dd | hh-mm-ss}", DateTime.Now);
            string messageStamp = $"[{timestamp}] PZTools";

            if (!string.IsNullOrEmpty(title)) messageStamp += $" [{title.ToUpper()}]";
            messageStamp += $" [{type.ToString()}]";
            string prefix = "";

            string formattedMessage = $"{prefix}{messageStamp}: {messageToFormat}";
            return formattedMessage;
        }

    }
}
