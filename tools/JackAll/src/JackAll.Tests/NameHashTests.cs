using JackAll.Core.Format;

namespace JackAll.Tests;

public class NameHashTests
{
    [Theory]
    [InlineData(@"UI/Textures//Common\Hardcore.xbt", @"ui\textures\common\hardcore.xbt")]
    [InlineData(@"\graphics\a.xbt", @"graphics\a.xbt")]
    [InlineData(@"//graphics/a.xbt", @"graphics\a.xbt")]
    public void Normalize_gives_the_relative_lowercase_backslash_form(string path, string expected)
        => Assert.Equal(expected, NameHash.Normalize(path));

    [Fact]
    public void A_path_hashes_to_its_archive_entry()
        => Assert.Equal(0xD580E9A0u, NameHash.Compute("ui/textures/common/hardcore.xbt"));
}
