using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Steam;

internal static class WorkshopUploadChecks
{
    public static void Run(string testRoot)
    {
        var root = Path.Combine(testRoot, "workshop-upload-checks");
        Directory.CreateDirectory(root);
        var poster = Path.Combine(root, "poster.png");
        using (var bitmap = new Bitmap(800, 800, PixelFormat.Format24bppRgb))
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, 800, 800), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var pixels = new byte[data.Stride * data.Height];
                new Random(42).NextBytes(pixels);
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally { bitmap.UnlockBits(data); }
            bitmap.Save(poster, ImageFormat.Png);
        }
        Expect(new FileInfo(poster).Length >= WorkshopPreview.MaxBytes, "oversized preview fixture");
        var originalHash = SHA256.HashData(File.ReadAllBytes(poster));
        var preview = WorkshopPreview.Prepare(poster, root);
        Expect(new FileInfo(preview).Length < WorkshopPreview.MaxBytes, "generated preview meets Steam size limit");
        using (var image = Image.FromFile(preview))
            Expect(image.Width == image.Height && image.Width > 0, "generated preview is a decodable image with original aspect ratio");
        Expect(originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(poster))), "original poster preserved byte for byte");
        var copyRoot = Path.Combine(root, "copy");
        Directory.CreateDirectory(copyRoot);
        var copy = WorkshopPreview.Prepare(preview, copyRoot);
        Expect(File.ReadAllBytes(copy).SequenceEqual(File.ReadAllBytes(preview)), "small previews copied unchanged");

        var project = ProjectEngine.CreateProject("WorkshopRecovery", "42");
        var settings = WorkshopSettingsStore.Load(project);
        settings.DefaultChangeNote = "Previous successful note";
        var manifest = Path.Combine(root, "upload.vdf");
        File.WriteAllText(manifest, "\"workshopitem\" { \"publishedfileid\" \"3796940479\" }");
        ExpectFailure(() => WorkshopUploader.CompleteUpload(project, settings, manifest, "Failed note", 9,
            "ERROR! Failed to update workshop item (Limit exceeded)."), "Limit exceeded");
        var saved = WorkshopSettingsStore.Load(project);
        Expect(saved.PublishedFileId == "3796940479", "failed upload persists Steam-assigned ID for retry");
        Expect(saved.DefaultChangeNote == "Previous successful note", "failure does not record successful change note");
        WorkshopUploader.CompleteUpload(project, saved, manifest, "Successful retry", 0, "Success.");
        Expect(WorkshopSettingsStore.Load(project).DefaultChangeNote == "Successful retry", "successful retry persists change note");
        ExpectFailure(() => WorkshopUploader.CompleteUpload(project, saved, manifest, "Bad result", 0,
            "ERROR! Failed to update workshop item (Access Denied)."), "Access Denied");
        Expect(WorkshopSettingsStore.Load(project).PublishedFileId == "3796940479", "access errors retain the existing item ID");
        ExpectFailure(() => WorkshopUploader.CompleteUpload(project, saved, manifest, "Missing local file", 9,
            "ERROR! File Not Found: preview.jpg"), "preview.jpg");
        Expect(WorkshopSettingsStore.Load(project).PublishedFileId == "3796940479", "local file errors do not recreate Workshop items");
        try
        {
            WorkshopUploader.CompleteUpload(project, saved, manifest, "Deleted item", 9,
                "[2026-09-06 18:17:44] ERROR! Failed to update workshop item (File Not Found).");
            throw new Exception("Expected deleted-item recovery");
        }
        catch (WorkshopUploader.WorkshopItemMissingException ex)
        {
            Expect(ex.ItemId == "3796940479", "deleted item requests replacement upload");
        }
        var replacement = WorkshopSettingsStore.Load(project);
        Expect(replacement.PublishedFileId == "0", "deleted item ID is cleared persistently before retry");
        Expect(replacement.DefaultChangeNote == "Successful retry", "deleted-item recovery preserves metadata");
        // A fresh creation can allocate an ID before failing: keep that new ID,
        // even if Steam reports File Not Found during this first content upload.
        File.WriteAllText(manifest, "\"publishedfileid\" \"3796940480\"");
        ExpectFailure(() => WorkshopUploader.CompleteUpload(project, replacement, manifest, "Replacement", 9,
            "ERROR! Failed to update workshop item (File Not Found)."), "has been saved");
        Expect(WorkshopSettingsStore.Load(project).PublishedFileId == "3796940480", "new replacement ID survives an initial upload failure");
        WorkshopUploader.CompleteUpload(project, replacement, manifest, "Replacement complete", 0, "Success.");
        Expect(WorkshopSettingsStore.Load(project).PublishedFileId == "3796940480", "successful replacement retains its new ID");
        settings.PublishedFileId = "0";
        File.WriteAllText(manifest, "\"publishedfileid\" \"0\"");
        ExpectFailure(() => WorkshopUploader.CompleteUpload(project, settings, manifest, "Missing ID", 0, ""), "without assigning");
        Directory.Delete(project.RootPath, recursive: true);
    }

    private static void ExpectFailure(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException ex) when (ex.Message.Contains(message, StringComparison.Ordinal))
        {
            System.Console.WriteLine("PASS: upload failure reports " + message);
            return;
        }
        throw new InvalidOperationException("Expected upload failure: " + message);
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        System.Console.WriteLine("PASS: " + message);
    }
}
