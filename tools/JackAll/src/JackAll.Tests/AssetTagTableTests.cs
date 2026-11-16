using JackAll.Core.Format;
using JackAll.Tools.Labels;

namespace JackAll.Tests;

public class AssetTagTableTests
{
    private const string Desk = @"graphics\objects\furnitures\desks\desksmall01_urban.xbg";
    private const string Shed = @"graphics\buildings\industrial\warehouse01.xbg";

    private static AssetTagTable Table()
    {
        var table = new AssetTagTable();
        table.Add([
            "# path\tkind\twhat\ttags\tsize\ttraits",
            $"{Desk}\tfurniture\tsmall urban writing desk\toffice home interior wood war\t1.84x0.88x0.78\tstatic",
            $"{Shed}\tbuilding\tcorrugated warehouse shed\twarehouse industrial tin\t20x10x6\tstatic building",
        ]);
        return table;
    }

    [Theory]
    [InlineData(Desk, "war", true)]
    [InlineData(Shed, "war", false)]
    [InlineData(Shed, "warehouse", true)]
    [InlineData(Desk, "STATIC", true)]
    [InlineData(Desk, "writ", true)]
    [InlineData(Desk, "kitch", false)]
    [InlineData(Desk, "oak", true)]
    public void A_vocabulary_word_matches_whole_tags_and_any_other_word_part_of_the_text(string path, string word, bool matches)
        => Assert.Equal(matches, Table().Matches(NameHash.Compute(path), word, "furniture · oak desk"));

    [Fact]
    public void A_file_has_its_kind_and_description_as_a_label_and_its_tags()
    {
        AssetTagTable table = Table();

        Assert.Equal("furniture · small urban writing desk", table.Find(NameHash.Compute(Desk))!.Text);
        Assert.Equal("office home interior wood war", table.TagsOf(NameHash.Compute(Desk)));
    }
}
