namespace PZTools.Core.Models
{
    public sealed class AppSettings
    {
        // System
        public string AppInstallPath { get; set; } = string.Empty;
        public string GameMode { get; set; } = string.Empty;
        public string ExistingGamePath { get; set; } = string.Empty;
        public string ManagedGamePath { get; set; } = string.Empty;

        // General
        public string Theme { get; set; } = "Dark";
        public string StartPage { get; set; } = "Projects";
        public bool ConfirmOnExit { get; set; } = true;

        // Editor
        public int EditorFontSize { get; set; } = 14;
        public int EditorTabSize { get; set; } = 4;
        public bool EditorWordWrap { get; set; } = true;
        public bool EditorAutoReloadOpenFile { get; set; } = true;

        // Updates
        public bool CheckUpdatesOnStartup { get; set; } = true;
        public string UpdateChannel { get; set; } = "Stable";
        public DateTime? LastUpdateCheckUtc { get; set; } = null;
    }
}
