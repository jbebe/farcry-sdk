using System.Text;
using JackAll.Core.Format;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// A placed entity's fragment id carries a cosmetic name prefix ahead of the authoritative numeric
/// id, so one layer can hold two staged spellings of the very same fragment. Both reaching
/// <c>FragmentOverrides</c> would make that single layer collide with itself at build time.
/// </summary>
public class ModLayerFragmentDedupTests : IDisposable
{
    private const string Container = @"worlds\world1\generated\worldsectors\worldsector17.data.fcb";
    private const string NamedSpelling = @"Guard_12.2058514756624450165.xml";
    private const string BareSpelling = @"2058514756624450165.xml";

    private readonly string _sandbox;

    public ModLayerFragmentDedupTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Two_spellings_already_on_disk_rescan_into_one_override()
    {
        string root = Path.Combine(_sandbox, "rescan");
        WriteFragment(root, NamedSpelling);
        WriteFragment(root, BareSpelling);

        var layer = new FolderModLayer(root, "rescan");

        Assert.Single(layer.FragmentOverrides[NameHash.Compute(Container)]);
    }

    /// <summary>The removed group id space is refused outright rather than staged as a phantom group -
    /// at a container's own root only, and never at the cost of a real entity id.</summary>
    [Theory]
    [InlineData(@"03_Foo.xml")]
    [InlineData(@"1_a.xml")]
    [InlineData(@"24_Weapons.xml")]
    public void A_group_id_at_a_containers_root_is_refused(string fragmentId)
    {
        string root = Path.Combine(_sandbox, "refused");
        WriteFragment(root, fragmentId);

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => new FolderModLayer(root, "refused"));
        Assert.Contains("NN_Name.xml", ex.Message);
    }

    [Theory]
    [InlineData(@"2058514756624450165.xml")]                  // a placed entity by its bare numeric id
    [InlineData(@"Guard_12.2058514756624450165.xml")]         // ... and with its cosmetic name prefix
    [InlineData(@"12_Crate.2058514756624450165.xml")]         // ... whose name itself starts NN_
    [InlineData(@"vehicle\Land\Jeep.xml")]                    // an archetype path
    [InlineData(@"vehicle\03_Foo.xml")]                       // NN_-shaped, but not at the root
    [InlineData(@"_layout.xml")]                              // a sector's mission-layer placement
    public void A_real_fragment_id_is_not_mistaken_for_one(string fragmentId)
    {
        string root = Path.Combine(_sandbox, "kept-" + fragmentId.GetHashCode().ToString("x8"));
        WriteFragment(root, fragmentId);

        var layer = new FolderModLayer(root, "kept");

        Assert.Single(layer.FragmentOverrides[NameHash.Compute(Container)]);
    }

    private static void WriteFragment(string root, string fragmentId)
    {
        string file = Path.Combine(root, "mods", Container, fragmentId);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "fragment");
    }

    [Fact]
    public void Staging_a_second_spelling_replaces_the_first_rather_than_adding_to_it()
    {
        string root = Path.Combine(_sandbox, "stage");
        var layer = new FolderModLayer(root, "stage");
        uint containerHash = NameHash.Compute(Container);

        layer.Stage(containerHash, $@"{Container}\{NamedSpelling}", "xml", "first"u8.ToArray());
        layer.Stage(containerHash, $@"{Container}\{BareSpelling}", "xml", "second"u8.ToArray());

        FragmentOverride staged = Assert.Single(layer.FragmentOverrides[containerHash]);
        Assert.Equal("second", Encoding.UTF8.GetString(layer.Read(staged.EntryHash)));
    }
}
