using JackAll.Core.Format;
using JackAll.Tools.Labels;

namespace JackAll.Tests;

public class AssetTagTableTests
{
    private const string Desk = @"graphics\objects\furnitures\desks\desksmall01_urban.xbg";
    private const string Shed = @"graphics\buildings\industrial\warehouse01.xbg";
    private const string Shader = @"engine\shaders\obj\h3e\shadernumber_012d3ebe.pso";

    private static AssetTagTable Table()
    {
        var table = new AssetTagTable();
        table.Add([
            "# path\tkind\twhat\ttags\tsize\ttraits",
            $"{Desk}\tfurniture\tsmall urban writing desk\toffice home interior wood war\t1.84x0.88x0.78\tstatic",
            $"{Shed}\tbuilding\tcorrugated warehouse shed, high roof\twarehouse industrial tin\t20x10x6\tstatic building",
        ]);
        table.Add([
            "# path\tkind\twhat\ttags\tdetail",
            $"{Shader}\tweapon\tmesh_weapon, lit by ambient+sun\thigh veryhigh ultrahigh\tmesh_weapon · lit by ambient+sun · QUALITYHIGH · high, veryhigh, ultrahigh",
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
    [InlineData(Shader, "high", true)]
    [InlineData(Shader, "low", false)]
    [InlineData(Shader, "QUALITYHIGH", true)]
    [InlineData(Shed, "high", true)]
    public void A_word_of_the_files_table_matches_whole_and_any_other_word_part_of_the_text(string path, string word, bool matches)
        => Assert.Equal(matches, Table().Matches(NameHash.Compute(path), word));

    [Fact]
    public void A_file_is_labelled_by_its_kind_and_its_detail_or_description()
    {
        AssetTagTable table = Table();

        Assert.Equal("furniture · small urban writing desk", table.Find(NameHash.Compute(Desk)));
        Assert.Equal("office home interior wood war", table.TagsOf(NameHash.Compute(Desk)));
        Assert.Equal("weapon · mesh_weapon · lit by ambient+sun · QUALITYHIGH · high, veryhigh, ultrahigh",
            table.Find(NameHash.Compute(Shader)));
    }
}
