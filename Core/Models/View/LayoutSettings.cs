namespace PZTools.Core.Models.View
{
    public class LayoutSettings
    {
        public LayoutSettings(double explorerWidth, double editorWidth, double propertiesWidth, double consoleHeight)
        {
            ExplorerWidth = explorerWidth;
            EditorWidth = editorWidth;
            PropertiesWidth = propertiesWidth;
            ConsoleHeight = consoleHeight;
        }
        public double ExplorerWidth { get; private set; } = 250;
        public double EditorWidth { get; private set; } = 300;
        public double PropertiesWidth { get; private set; } = 150;
        public double ConsoleHeight { get; private set; } = 100;
    }
}
