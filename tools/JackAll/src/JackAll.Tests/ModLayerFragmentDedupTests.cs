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
        string containerDir = Path.Combine(root, Container);
        Directory.CreateDirectory(containerDir);
        File.WriteAllText(Path.Combine(containerDir, NamedSpelling), "fragment");
        File.WriteAllText(Path.Combine(containerDir, BareSpelling), "fragment");

        var layer = new FolderModLayer(root, "rescan");

        Assert.Single(layer.FragmentOverrides[NameHash.Compute(Container)]);
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
