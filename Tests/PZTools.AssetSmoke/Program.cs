using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PZTools.Core.Functions.Projects;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "PZTools-AssetSmoke-" + Guid.NewGuid().ToString("N"));
        var mod = Directory.CreateDirectory(Path.Combine(root, "mod")).FullName;
        var external = Directory.CreateDirectory(Path.Combine(root, "external")).FullName;
        try
        {
            foreach (var name in new[] { "poster.png", "icon.png" })
            {
                var destination = Path.Combine(mod, name);
                var source = Path.Combine(external, name);
                WriteImage(destination, 10);
                WriteImage(source, 200);
                var oldPreview = AssetPreviewLoader.Load(destination);
                var selectedPreview = AssetPreviewLoader.Load(source);
                Check(ModInfoParser.EnsureLocalAsset(mod, source) == name, "copy while both previews remain alive");
                var refreshed = AssetPreviewLoader.Load(destination);
                Check(Pixel(oldPreview) == 10 && Pixel(refreshed) == 200, "refresh reads replacement pixels");
                Check(ModInfoParser.EnsureLocalAsset(mod, destination) == name, "select existing local asset");
                using (File.Open(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                File.Delete(source);
                Check(Pixel(selectedPreview) == 200, "preview survives source deletion");
                GC.KeepAlive(oldPreview);
                GC.KeepAlive(refreshed);
                Console.WriteLine($"PASS: {name} preview releases files and refreshes correctly.");
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static byte Pixel(BitmapSource image)
    {
        var pixels = new byte[4];
        image.CopyPixels(pixels, 4, 0);
        return pixels[0];
    }

    private static void WriteImage(string path, byte blue)
    {
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { blue, 0, 0, 255 }, 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
