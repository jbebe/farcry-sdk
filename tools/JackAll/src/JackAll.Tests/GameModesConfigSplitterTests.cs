using System.Collections.Concurrent;
using System.Text;
using System.Xml.Linq;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>One fragment per property block and per game mode of gamemodesconfig.xml, merged by attribute.</summary>
public class GameModesConfigSplitterTests : IDisposable
{
    private static readonly GameModesConfigContainerSplitter Splitter = GameModesConfigContainerSplitter.Instance;

    /// <summary>The patch's copy: 43 property blocks, two of them named alike but for case, and 15 game
    /// modes, with comments between them.</summary>
    private const string Config = "Ai/gamemodesconfig.xml";

    private const string ConfigPath = @"engine\gamemodes\gamemodesconfig.xml";
    private const string MuzzleFlash = "DefaultMuzzleFlashManager.xml";
    private const string Settings = "DefaultCountersService.xml";

    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixture_was_actually_found() => Fixture.AssertPresent(Config);

    [Theory]
    [InlineData("gamemodesconfig.xml", true)]
    [InlineData("GameModesConfig.xml", true)]
    [InlineData("gamemodes.xml", false)]
    public void The_file_is_recognised_by_name(string fileName, bool splits)
        => Assert.Equal(splits, ContainerFormats.IsContainerSegment(fileName));

    [Fact]
    public void Each_block_and_game_mode_is_one_fragment()
    {
        if (Fixture.Read(Config) is not { } config) return;

        IContainerTree tree = Splitter.Open(config);
        IReadOnlyList<FcbFragmentInfo> rows = tree.List();

        Assert.Equal(42 + 15, rows.Count);
        Assert.Contains(rows, r => r.Id == MuzzleFlash);
        Assert.Contains(rows, r => r.Id == "GameMode.Single.xml");
        Assert.StartsWith("<EditorUiService", tree.Extract("EditorUIService.xml"), StringComparison.Ordinal);
    }

    /// <summary>Every fragment spliced straight back reads the same, with the comments and the file's
    /// CRLF line endings kept and no declaration added.</summary>
    [Fact]
    public void Every_fragment_spliced_back_keeps_the_file()
    {
        if (Fixture.Read(Config) is not { } config) return;

        IContainerTree tree = Splitter.Open(config);
        Dictionary<string, string> every = tree.List().ToDictionary(r => r.Id, r => tree.Extract(r.Id)!);

        byte[] rebuilt = Splitter.Apply(config, every);

        IContainerTree again = Splitter.Open(rebuilt);
        Assert.All(every, kv => Assert.Equal(kv.Value, again.Extract(kv.Key)));
        Assert.Equal(tree.Skeleton(_ => true), again.Skeleton(_ => true));
        string text = Encoding.UTF8.GetString(rebuilt);
        Assert.StartsWith("<GameModeConfig", text, StringComparison.Ordinal);
        Assert.Equal(text.Count(c => c == '\n'), text.Split("\r\n").Length - 1);
        Assert.Equal(Encoding.UTF8.GetString(config).Split("<!--").Length, text.Split("<!--").Length);
    }

    [Fact]
    public void Two_mods_changing_different_attributes_of_one_block_both_land()
    {
        if (Fixture.Read(Config) is not { } config) return;

        FolderModLayer modA = Stage("mod_a", MuzzleFlash, WithAttribute(config, MuzzleFlash, "fRadius", "4.f"));
        FolderModLayer modB = Stage("mod_b", MuzzleFlash, WithAttribute(config, MuzzleFlash, "fIntensity", "2"));

        var conflicts = new ConcurrentQueue<ModConflict>();
        XElement merged = BlockOf(Splitter.Apply(config, Resolve(config, conflicts, modA, modB)), MuzzleFlash);

        Assert.Empty(conflicts);
        Assert.Equal("4.f", (string?)merged.Attribute("fRadius"));
        Assert.Equal("2", (string?)merged.Attribute("fIntensity"));
    }

    [Fact]
    public void One_attribute_changed_two_ways_is_a_conflict_settled_by_load_order()
    {
        if (Fixture.Read(Config) is not { } config) return;

        FolderModLayer modA = Stage("mod_a", MuzzleFlash, WithAttribute(config, MuzzleFlash, "fRadius", "4.f"));
        FolderModLayer modB = Stage("mod_b", MuzzleFlash, WithAttribute(config, MuzzleFlash, "fRadius", "3.f"));

        var conflicts = new ConcurrentQueue<ModConflict>();
        XElement merged = BlockOf(Splitter.Apply(config, Resolve(config, conflicts, modA, modB)), MuzzleFlash);

        Assert.Equal("mod_b", Assert.Single(conflicts).WinningLayer);
        Assert.Equal("3.f", (string?)merged.Attribute("fRadius"));
    }

    /// <summary>Hit locations are a list of differently named children; one mod's change to a child
    /// and another's to a block elsewhere in the same file both reach the game.</summary>
    [Fact]
    public void A_nested_change_and_another_blocks_change_both_land()
    {
        if (Fixture.Read(Config) is not { } config) return;

        XElement settings = BlockOf(config, Settings);
        settings.Descendants("HitLocations").Single().Element("Head")!.SetAttributeValue("multiplier", "100.0");
        FolderModLayer modA = Stage("mod_a", Settings, settings.ToString());
        FolderModLayer modB = Stage("mod_b", MuzzleFlash, WithAttribute(config, MuzzleFlash, "fRadius", "4.f"));

        byte[] built = Splitter.Apply(config, Resolve(config, null, modA, modB));

        Assert.Equal("100.0", (string?)BlockOf(built, Settings).Descendants("Head").Single().Attribute("multiplier"));
        Assert.Equal("4.f", (string?)BlockOf(built, MuzzleFlash).Attribute("fRadius"));
    }

    /// <summary>A mod that still ships the whole file is the base a fragment lands on, keeping that copy's own change.</summary>
    [Fact]
    public void A_fragment_lands_on_another_mods_whole_copy()
    {
        if (Fixture.Read(Config) is not { } config) return;

        XElement settings = BlockOf(config, Settings);
        settings.Descendants("HitLocations").Single().Element("Head")!.SetAttributeValue("multiplier", "100.0");
        byte[] wholeCopy = Splitter.Apply(config, new Dictionary<string, string> { [Settings] = settings.ToString() });

        byte[] built = Splitter.Apply(wholeCopy,
            new Dictionary<string, string> { [MuzzleFlash] = WithAttribute(config, MuzzleFlash, "fRadius", "4.f") });

        Assert.Equal("100.0", (string?)BlockOf(built, Settings).Descendants("Head").Single().Attribute("multiplier"));
        Assert.Equal("4.f", (string?)BlockOf(built, MuzzleFlash).Attribute("fRadius"));
    }

    [Fact]
    public void A_new_block_and_a_new_game_mode_are_added_where_they_belong()
    {
        if (Fixture.Read(Config) is not { } config) return;

        var block = new XElement("ModService", new XAttribute("fValue", "1"));
        var mode = new XElement("GameMode", new XElement("Desc", new XAttribute("Name", "ModMode")));

        byte[] built = Splitter.Apply(config, new Dictionary<string, string>
        {
            ["ModService.xml"] = block.ToString(),
            ["GameMode.ModMode.xml"] = mode.ToString(),
        });

        XElement root = XElement.Parse(Encoding.UTF8.GetString(built));
        Assert.NotNull(root.Element("GameModeProperties")!.Element("ModService"));
        Assert.Equal("ModMode", (string?)root.Elements("GameMode").Last().Element("Desc")!.Attribute("Name"));
    }

    [Fact]
    public void A_block_staged_under_another_name_is_refused()
    {
        if (Fixture.Read(Config) is not { } config) return;

        var wrong = new Dictionary<string, string> { [MuzzleFlash] = BlockOf(config, Settings).ToString() };

        Assert.Throws<InvalidDataException>(() => Splitter.Apply(config, wrong));
    }

    /// <summary>The second of two blocks named alike but for case has no id of its own: staging it is
    /// refused rather than landing on the first.</summary>
    [Fact]
    public void The_block_named_like_another_but_for_case_is_refused()
    {
        if (Fixture.Read(Config) is not { } config) return;

        var shadowed = new Dictionary<string, string> { ["EditorUIService.xml"] = new XElement("EditorUIService").ToString() };

        Assert.Throws<InvalidDataException>(() => Splitter.Apply(config, shadowed));
    }

    private static XElement BlockOf(byte[] file, string id) => XElement.Parse(Splitter.Open(file).Extract(id)!);

    private static string WithAttribute(byte[] file, string id, string attribute, string value)
    {
        XElement block = BlockOf(file, id);
        block.SetAttributeValue(attribute, value);
        return block.ToString();
    }

    private static Dictionary<string, string> Resolve(
        byte[] file, ConcurrentQueue<ModConflict>? conflicts, params FolderModLayer[] layers)
        => TestSupport.ResolveFragments(Splitter, file, ConfigPath, conflicts, layers);

    private FolderModLayer Stage(string name, string id, string xml)
        => TestSupport.StageFragment(_sandbox, name, ConfigPath, id, xml);
}
