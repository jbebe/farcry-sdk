using System.Collections.Concurrent;
using System.Xml.Linq;
using JackAll.Core.Format.Rml;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>A world's particle library split per particle system, keyed by the system's CPathID.</summary>
public class ParticleLibrarySplitterTests : IDisposable
{
    private static readonly ParticleLibraryContainerSplitter Splitter = ParticleLibraryContainerSplitter.Instance;

    /// <summary>The DLC's library: 58 systems, its weapons' muzzle flashes and blood among them.</summary>
    private const string Library = "Particles/entitylibrary_deploadnewparticles.rml";

    private const string LibraryPath = @"downloadcontent\dlc1\generated\entitylibrary_deploadnewparticles.rml";
    private const string Flash = "dlc1_particles.dlc1.pl_muzzleflash_sawedgun";
    private const string Blood = "bullet_impact.bullet_impact.b_imp_flesh";

    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixture_was_actually_found() => Fixture.AssertPresent(Library);

    [Theory]
    [InlineData("world1_deploadnewparticles.rml", true)]
    [InlineData("ENTITYLIBRARY_DEPLOADNEWPARTICLES.RML", true)]
    [InlineData("world1_deploadnewparticles.xml", false)]
    [InlineData("world1_depload.dat", true)]
    public void Libraries_are_recognised_by_their_suffix(string fileName, bool splits)
        => Assert.Equal(splits, ContainerFormats.IsContainerSegment(fileName));

    /// <summary>The id carries the resource id the depload and a weapon's data name the system by.</summary>
    [Fact]
    public void A_systems_id_is_numbered_by_its_resource_id()
        => Assert.Equal("weapons.weapons.pl_muzzleflash_ak47.483129128.xml",
            ParticleLibraryContainerSplitter.IdOf("weapons.weapons.pl_muzzleflash_ak47"));

    [Fact]
    public void Every_system_spliced_back_is_the_bytes_it_came_from()
    {
        if (Fixture.Read(Library) is not { } library) return;

        IContainerTree tree = Splitter.Open(library);
        Dictionary<string, string> every = tree.List().ToDictionary(r => r.Id, r => tree.Extract(r.Id)!);

        Assert.Equal(58, every.Count);
        Fixture.AssertSameBytes(Library, library, Splitter.Apply(library, every));
    }

    [Fact]
    public void Two_mods_editing_different_systems_both_land()
    {
        if (Fixture.Read(Library) is not { } library) return;

        FolderModLayer modA = Stage("mod_a", Flash, WithEmitterAttribute(library, Flash, "HDRMul", "2"));
        FolderModLayer modB = Stage("mod_b", Blood, WithEmitterAttribute(library, Blood, "NbInitPart", "3"));

        XElement built = RmlDocument.Deserialize(Splitter.Apply(library, Resolve(library, null, modA, modB)));

        Assert.Equal("2", (string?)SystemNamed(built, Flash).Element("PartEmit")!.Attribute("HDRMul"));
        Assert.Equal("3", (string?)SystemNamed(built, Blood).Element("PartEmit")!.Attribute("NbInitPart"));
    }

    /// <summary>A curve is one value: two mods reshaping one curve conflict, and the winner's comes through whole.</summary>
    [Fact]
    public void Two_mods_reshaping_one_curve_conflict_and_the_winner_comes_through_whole()
    {
        if (Fixture.Read(Library) is not { } library) return;

        FolderModLayer modA = Stage("mod_a", Flash, WithSize(library, "0.4"));
        FolderModLayer modB = Stage("mod_b", Flash, WithSize(library, "0.9"));

        var conflicts = new ConcurrentQueue<ModConflict>();
        XElement built = RmlDocument.Deserialize(Splitter.Apply(library, Resolve(library, conflicts, modA, modB)));

        Assert.Equal("mod_b", Assert.Single(conflicts).WinningLayer);
        Assert.Equal("0.9", (string?)SystemNamed(built, Flash).Element("PartEmit")!
            .Element("InitSizeKF")!.Element("ValueKF")!.Attribute("Value"));
    }

    [Fact]
    public void A_new_system_is_added_and_found_by_its_name()
    {
        if (Fixture.Read(Library) is not { } library) return;

        XElement copy = XElement.Parse(Splitter.Open(library).Extract(ParticleLibraryContainerSplitter.IdOf(Flash))!);
        copy.SetAttributeValue("Name", "mod.flash.copy");
        string id = ParticleLibraryContainerSplitter.IdOf("mod.flash.copy");

        IContainerTree built = Splitter.Open(Splitter.Apply(library, new Dictionary<string, string> { [id] = copy.ToString() }));

        Assert.Equal(59, built.List().Count);
        Assert.NotNull(built.Extract(id));
        Assert.NotNull(built.Extract(ParticleLibraryContainerSplitter.IdOf(Flash)));
    }

    [Fact]
    public void A_system_staged_under_another_systems_id_is_refused()
    {
        if (Fixture.Read(Library) is not { } library) return;

        string blood = Splitter.Open(library).Extract(ParticleLibraryContainerSplitter.IdOf(Blood))!;
        var wrong = new Dictionary<string, string> { [ParticleLibraryContainerSplitter.IdOf(Flash)] = blood };

        Assert.Throws<InvalidDataException>(() => Splitter.Apply(library, wrong));
    }

    private static XElement SystemNamed(XElement library, string name)
        => library.Elements("PartSys").Single(s => (string?)s.Attribute("Name") == name);

    private static XElement SystemOf(byte[] library, string name)
        => XElement.Parse(Splitter.Open(library).Extract(ParticleLibraryContainerSplitter.IdOf(name))!);

    private static string WithEmitterAttribute(byte[] library, string name, string attribute, string value)
    {
        XElement system = SystemOf(library, name);
        system.Element("PartEmit")!.SetAttributeValue(attribute, value);
        return system.ToString();
    }

    private static string WithSize(byte[] library, string value)
    {
        XElement system = SystemOf(library, Flash);
        system.Element("PartEmit")!.Element("InitSizeKF")!.Element("ValueKF")!.SetAttributeValue("Value", value);
        return system.ToString();
    }

    private static Dictionary<string, string> Resolve(
        byte[] file, ConcurrentQueue<ModConflict>? conflicts, params FolderModLayer[] layers)
        => TestSupport.ResolveFragments(Splitter, file, LibraryPath, conflicts, layers);

    private FolderModLayer Stage(string name, string system, string xml)
        => TestSupport.StageFragment(_sandbox, name, LibraryPath, ParticleLibraryContainerSplitter.IdOf(system), xml);
}
