namespace PZTools.Core.Models
{
    public sealed class DiscoveredModDependency
    {
        public string ModId { get; init; } = "";
        public string Name { get; init; } = "";
        public string SourcePath { get; init; } = "";
        public string WorkshopId { get; init; } = "";
        public double Build { get; init; }
        public string Origin { get; init; } = "Local";
        public ModInfo Info { get; init; } = new();

        public string DisplayName => string.IsNullOrWhiteSpace(WorkshopId)
            ? $"{Name} ({ModId}) - {Origin}"
            : $"{Name} ({ModId}) - Workshop {WorkshopId}";
    }
}
