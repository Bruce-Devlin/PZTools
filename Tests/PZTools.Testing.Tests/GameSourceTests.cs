using System.IO;
using PZTools.Core.Windows.Dialogs;

namespace PZTools.Testing.Tests;

public sealed class GameSourceTests
{
    [Fact]
    public void DiscoversExistingAndManagedMediaWithoutAssumingRootContainsMedia()
    {
        var root = Path.Combine(Path.GetTempPath(), "PZToolsSources-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "42.20", "media", "lua"));
            Directory.CreateDirectory(Path.Combine(root, "41.78", "media", "scripts"));
            Directory.CreateDirectory(Path.Combine(root, "incomplete"));
            Assert.Equal(2, GameSources.DiscoverBuilds(root, true).Count);
            Assert.Empty(GameSources.DiscoverBuilds(root, false));
            Assert.Single(GameSources.DiscoverBuilds(Path.Combine(root, "42.20"), false));
            Assert.Empty(GameSources.DiscoverBuilds("", false));
        }
        finally { Directory.Delete(root, true); }
    }
}
