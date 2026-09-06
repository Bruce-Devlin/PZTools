using System.IO;
using System.Windows.Media.Imaging;

namespace PZTools.Core.Functions.Projects;

internal static class AssetPreviewLoader
{
    public static BitmapImage Load(string path)
    {
        // Decode before closing the stream so previews never hold an asset open.
        // A stream also avoids reusing WPF's URI cache after an asset is replaced.
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
