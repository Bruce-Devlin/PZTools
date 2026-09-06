using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace PZTools.Core.Functions.Steam
{
    internal static class WorkshopPreview
    {
        internal const long MaxBytes = 1_000_000;

        internal static string Prepare(string sourcePath, string destinationDirectory)
        {
            if (new FileInfo(sourcePath).Length < MaxBytes)
            {
                var copyPath = Path.Combine(destinationDirectory, "preview" + Path.GetExtension(sourcePath).ToLowerInvariant());
                File.Copy(sourcePath, copyPath, overwrite: true);
                return copyPath;
            }

            // Generate only the Workshop thumbnail; the project's original poster is
            // retained in the mod content with its original resolution and format.
            using var source = Image.FromFile(sourcePath);
            var jpeg = ImageCodecInfo.GetImageEncoders().Single(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
            var destination = Path.Combine(destinationDirectory, "preview.jpg");
            for (var maxDimension = 1024; maxDimension >= 64; maxDimension /= 2)
            {
                var scale = Math.Min(1d, (double)maxDimension / Math.Max(source.Width, source.Height));
                using var thumbnail = new Bitmap(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
                using (var graphics = Graphics.FromImage(thumbnail))
                {
                    graphics.Clear(Color.White);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, 0, 0, thumbnail.Width, thumbnail.Height);
                }
                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                using var buffer = new MemoryStream();
                thumbnail.Save(buffer, jpeg, parameters);
                if (buffer.Length >= MaxBytes)
                    continue;

                File.WriteAllBytes(destination, buffer.ToArray());
                _ = Console.Log($"Prepared Workshop preview ({buffer.Length:N0} bytes). Original poster preserved.");
                return destination;
            }

            throw new InvalidDataException("Could not prepare a Workshop preview under 1 MB. Choose a smaller poster image.");
        }
    }
}
