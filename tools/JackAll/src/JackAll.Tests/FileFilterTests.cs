using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.Tests;

public class FileFilterTests
{
    private static VfsFile File(string path, uint hash = 1)
        => new(hash, path, new FileType("test", Path.GetExtension(path).TrimStart('.')), 0, "common", SourceKind.Archive,
            IsOverriding: false, NameIsKnown: true);

    private static bool Matches(string filter, VfsFile file) => FileFilter.Parse(filter).Matches(file, _ => "common");

    [Fact]
    public void Every_word_must_appear_and_no_excluded_one()
    {
        VfsFile file = File(@"graphics\vehicles\land\buggy\buggy.xbg");

        Assert.True(Matches("buggy land", file));
        Assert.False(Matches("buggy sea", file));
        Assert.False(Matches("buggy -land", file));
    }

    [Theory]
    [InlineData("spk:reload", true)]
    [InlineData("spk:\"ak47 reload\"", true)]
    [InlineData("spk:\"ak47 rel", true)]
    [InlineData("soundbinary spk:ak47", true)]
    [InlineData("spk:\"ak47 jam\"", false)]
    [InlineData("ak47", false)]
    public void Spk_words_match_the_label_and_plain_words_only_the_path(string filter, bool matches)
    {
        VfsFile file = File(@"soundbinary\004b27c7.spk");

        Assert.Equal(matches, FileFilter.Parse(filter).Matches(file, _ => "common", _ => "weapon · reload, 1st person (ak47)"));
    }

    [Fact]
    public void A_file_without_a_label_never_matches_spk()
        => Assert.False(FileFilter.Parse("spk:reload").Matches(File(@"soundbinary\004b27c7.spk"), _ => "common"));

    [Fact]
    public void A_forward_slash_matches_a_backslash()
        => Assert.True(Matches("land/buggy", File(@"graphics\vehicles\land\buggy\buggy.xbg")));

    [Fact]
    public void Ext_takes_the_extension_with_or_without_a_dot()
    {
        VfsFile file = File(@"graphics\vehicles\land\buggy\buggy.xbg");

        Assert.True(Matches("ext:xbg", file));
        Assert.True(Matches("ext:.xbg", file));
        Assert.False(Matches("ext:xbt", file));
    }

    [Fact]
    public void Hash_takes_hex_with_or_without_0x_and_ignores_a_bad_one()
    {
        VfsFile file = File(@"a\b.xbg", 0x1A2B3C4D);

        Assert.True(Matches("hash:1a2b3c4d", file));
        Assert.True(Matches("hash:0x1A2B3C4D", file));
        Assert.False(Matches("hash:1a2b3c4e", file));
        Assert.True(FileFilter.Parse("hash:zz").IsEmpty);
    }

    [Fact]
    public void Arch_matches_the_module_name()
    {
        FileFilter filter = FileFilter.Parse("arch:dlc");

        Assert.True(filter.Matches(File(@"a\b.xbg"), _ => "dlc1/menus"));
        Assert.False(filter.Matches(File(@"a\b.xbg"), _ => "common"));
    }
}
