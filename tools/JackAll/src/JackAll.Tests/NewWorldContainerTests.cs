using System.IO.Compression;
using System.Numerics;
using System.Text;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.Tests;

/// <summary>
/// A new world's sector exists only as fragments until it is packed, and a mod of a mod's map
/// fragments a container no archive ships. Both need an original other than the archives'.
/// </summary>
public class NewWorldContainerTests : IDisposable
{
    // A multiplayer sector, for two real placed entities to copy.
    private const string Donor = "WorldSector/worldsector56.data.fcb";

    private const string Container = @"levels\newworld\generated\worldsectors\worldsector5.data.fcb";

    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));
    private readonly GameInstall? _install;
    private readonly FcbClassDefinitions _defs = BundledAssets.LoadFcbClasses();

    public NewWorldContainerTests()
    {
        if (Fixture.Locate(FatArchiveTests.Fat) is not { } fat || Fixture.Locate(FatArchiveTests.Dat) is not { } dat
            || !Fixture.Present(Donor)) return;

        Directory.CreateDirectory(Path.Combine(_sandbox, "bin"));
        Directory.CreateDirectory(Path.Combine(_sandbox, "Data_Win32"));
        File.WriteAllText(Path.Combine(_sandbox, "bin", "FarCry2.exe"), "stub");
        File.Copy(fat, Path.Combine(_sandbox, "Data_Win32", "patch.fat"));
        File.Copy(dat, Path.Combine(_sandbox, "Data_Win32", "patch.dat"));
        _install = GameInstall.TryOpen(_sandbox, out _);
    }

    [Fact]
    public void A_fragment_only_sector_reads_as_its_skeleton_with_the_fragment_in_main()
    {
        if (_install is null) return;
        FcbFragment entity = DonorEntities()[0];

        using GameVfs vfs = GameVfs.Load(_install, TestSupport.LoadNames(), fcbDefinitions: _defs);
        vfs.Rebuild([Workspace((entity.Id, Xml(entity)))]);

        FcbObject sector = FcbDocument.Deserialize(vfs.ReadByPath(Container)!);
        Assert.Equal(WorldHashes.WorldSector, sector.TypeHash);
        Assert.Equal(5u, FcbEntityFields.ReadU32(sector, WorldHashes.WorldSectorId));
        FcbObject main = Assert.Single(FcbFragments.LayersOf(sector));
        Assert.True(MissionLayers.IsMain(MissionLayers.NameOf(main)));
        Assert.Equal(DisEntityId(entity.Node), DisEntityId(Assert.Single(main.Children)));
    }

    [Fact]
    public void A_build_leaves_out_a_fragment_only_sector_and_says_so()
    {
        if (_install is null) return;
        FcbFragment entity = DonorEntities()[0];

        using GameVfs originals = GameVfs.OpenForOriginalsOnly(_install, TestSupport.LoadNames());
        BuildResult result = PatchBuilder.Build(_install, [Workspace((entity.Id, Xml(entity)))], originals.ReadOriginal, _defs);

        Assert.Equal([Container], result.Unpacked);
        Assert.DoesNotContain(FatArchive.Read(_install.PatchFat).Entries, e => e.Hash == NameHash.Compute(Container));
    }

    [Fact]
    public void A_second_mods_fragment_merges_onto_a_first_mods_whole_sector()
    {
        if (_install is null) return;
        FcbFragment[] entities = DonorEntities();

        byte[] packed = FcbAssembler.Apply(
            FcbDocument.Serialize(ContainerSkeletons.WorldSector(5)),
            entities.Take(2).ToDictionary(e => e.Id, Xml, FcbFragments.IdComparer));
        IModLayer mapMod = ZipMod("map", ($"mods/{Container}", packed));

        FcbObject moved = entities[0].Node.Clone();
        moved.Values[WorldHashes.HidPos] = FcbEntityFields.Vector3Bytes(new Vector3(1, 2, 3));
        IModLayer alteringMod = ZipMod("altering", ($@"mods/{Container}\{entities[0].Id}", Encoding.UTF8.GetBytes(FcbXml.ToXml(moved, _defs))));

        using GameVfs originals = GameVfs.OpenForOriginalsOnly(_install, TestSupport.LoadNames());
        BuildResult result = PatchBuilder.Build(_install, [mapMod, alteringMod], originals.ReadOriginal, _defs);

        Assert.Empty(result.Unpacked);
        var entry = FatArchive.Read(_install.PatchFat).Entries.First(e => e.Hash == NameHash.Compute(Container));
        using var patch = DuniaArchive.Open(_install.PatchFat);
        FcbObject[] built = [.. FcbFragments.LayersOf(FcbDocument.Deserialize(patch.Read(entry))).SelectMany(l => l.Children)];
        Assert.Equal(FcbEntityFields.Vector3Bytes(new Vector3(1, 2, 3)), built.Single(e => DisEntityId(e) == DisEntityId(entities[0].Node)).Values[WorldHashes.HidPos]);
        Assert.Contains(built, e => DisEntityId(e) == DisEntityId(entities[1].Node));
    }

    private FcbFragment[] DonorEntities() => [.. FcbFragments.List(FcbDocument.Deserialize(Fixture.Read(Donor)!)).Take(2)];

    private string Xml(FcbFragment fragment) => FcbXml.ToXml(fragment.Node, _defs);

    private static ulong DisEntityId(FcbObject entity) => FcbEntityFields.ReadU64(entity, WorldHashes.DisEntityId);

    private FolderModLayer Workspace(params (string FragmentId, string Xml)[] fragments)
    {
        string root = Path.Combine(_sandbox, "workspace");
        foreach ((string id, string xml) in fragments)
        {
            string file = Path.Combine(root, "mods", Container, id);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, xml);
        }
        return new FolderModLayer(root, "workspace") { IsWorkspace = true };
    }

    private ZipModLayer ZipMod(string name, params (string Path, byte[] Content)[] files)
    {
        string zipPath = Path.Combine(_sandbox, $"{name}.zip");
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach ((string path, byte[] content) in files)
            {
                using Stream stream = zip.CreateEntry(path.Replace('\\', '/')).Open();
                stream.Write(content);
            }
        }
        return new ZipModLayer(zipPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true);
        }
        catch { /* temp dir cleanup is best-effort */ }
    }
}
