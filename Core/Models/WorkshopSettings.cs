namespace PZTools.Core.Models
{
    public enum WorkshopVisibility
    {
        Public = 0,
        FriendsOnly = 1,
        Private = 2,
        Unlisted = 3
    }

    public sealed class WorkshopSettings
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Tags { get; set; } = new() { "Mod" };
        public WorkshopVisibility Visibility { get; set; } = WorkshopVisibility.Public;
        public string PublishedFileId { get; set; } = "0";
        public string DefaultChangeNote { get; set; } = "Updated with PZTools";
    }
}
