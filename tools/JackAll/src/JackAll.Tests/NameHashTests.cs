using JackAll.Core.Format;

namespace JackAll.Tests;

/// <summary>
/// <see cref="NameHash"/> against the engine's own path rules: <c>CFileManager::FormatPath</c> and the
/// extension swap <c>CPathID::SetContent</c> makes before hashing.
/// </summary>
public class NameHashTests
{
    [Theory]
    [InlineData(@"UI/Textures//Common\Hardcore.xbt", @"ui\textures\common\hardcore.xbt")]
    [InlineData(@"\graphics\a.xbt", @"graphics\a.xbt")]
    [InlineData(@"/graphics/a.xbt", @"\graphics\a.xbt")]
    [InlineData(@"\\server\a.xbt", @"\\server\a.xbt")]
    public void Normalize_follows_the_engine(string path, string expected)
        => Assert.Equal(expected, NameHash.Normalize(path));

    [Theory]
    [InlineData(@"ui\textures\common\hardcore.dds", @"ui\textures\common\hardcore.xbt")]
    [InlineData(@"graphics\body.skel.xml", @"graphics\body.skeleton")]
    // The extension runs from the first dot, so this one matches no source extension.
    [InlineData(@"graphics\v1.2.dds", @"graphics\v1.2.dds")]
    [InlineData(@"graphics.dds\a.fcb", @"graphics.dds\a.fcb")]
    public void ResourcePath_swaps_a_source_extension_for_the_cooked_one(string path, string expected)
        => Assert.Equal(expected, NameHash.ResourcePath(path));

    [Fact]
    public void A_source_path_hashes_to_its_cooked_entry()
        => Assert.Equal(0xD580E9A0u, NameHash.Compute("ui/textures/common/hardcore.dds"));

    [Fact]
    public void An_empty_path_has_no_key()
        => Assert.Equal(0xFFFFFFFFu, NameHash.Compute(""));
}
