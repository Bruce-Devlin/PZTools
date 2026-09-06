namespace PZTools.Core.Models
{
    public enum PlaytestMode
    {
        SinglePlayer,
        DedicatedServer
    }

    public enum PlaytestSaveMode
    {
        Empty,
        FreshClone,
        ReuseProfileData
    }

    public enum PlaytestWindowMode
    {
        Windowed,
        BorderlessWindowed,
        Fullscreen
    }

    public sealed class PlaytestDependency
    {
        public string ModId { get; set; } = "";
        public string WorkshopId { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public bool IsProjectRequired { get; set; }

        public override string ToString() => string.IsNullOrWhiteSpace(WorkshopId) ? ModId : $"{ModId} ({WorkshopId})";
    }

    public sealed class PlaytestProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Default playtest";
        public PlaytestMode Mode { get; set; }
        public double Build { get; set; } = 42;
        public string LaunchArguments { get; set; } = "-debug -nosteam";
        public PlaytestWindowMode WindowMode { get; set; } = PlaytestWindowMode.Windowed;
        public int WindowWidth { get; set; } = 1280;
        public int WindowHeight { get; set; } = 720;
        public PlaytestSaveMode SaveMode { get; set; } = PlaytestSaveMode.Empty;
        public string SourceSavePath { get; set; } = "";
        public bool KeepSessionData { get; set; }
        public List<PlaytestDependency> Dependencies { get; set; } = new();

        public string ServerInstallPath { get; set; } = "";
        public string ServerName { get; set; } = "PZToolsTest";
        public int ServerPort { get; set; } = 16261;
        public int MaxPlayers { get; set; } = 8;
        public int ClientCount { get; set; } = 1;
        public int ServerStartupTimeoutSeconds { get; set; } = 90;
        public bool NoSteam { get; set; } = true;
        public bool AutoConnectClients { get; set; } = true;
        public string AdditionalServerOptions { get; set; } = "";

        public override string ToString() => Name;
    }

    public sealed class PlaytestSessionWorkspace
    {
        public required string RootPath { get; init; }
        public required string ServerCachePath { get; init; }
        public required IReadOnlyList<string> ClientCachePaths { get; init; }
        public bool IsReusable { get; init; }
    }
}
