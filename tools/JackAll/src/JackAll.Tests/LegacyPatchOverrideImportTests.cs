using System.IO.Hashing;
using System.Text;
using System.Xml.Linq;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// A legacy patch override built from the retail libraries: a stock archetype edited and another
/// deleted, world archetypes copied in with and without an edit, a DLC archetype copied in, and one
/// archetype no library declares. Copies carry a wrapped rml document and a foreign entity id; the
/// mod's world library also edits the unchanged copy and declares the new archetype.
/// </summary>
public sealed class LegacyPatchOverrideImportTests : IDisposable
{
    private const string AddedId = "test\\added.xml";
    private static readonly uint Edit = FcbClassDefinitions.Crc32Ascii("fLegacyEdit");
    private static readonly uint OverrideHash = NameHash.Compute(LegacyPatchImporter.PatchOverride);
    private static readonly uint[] Worlds = [.. LegacyPatchImporter.WorldLibraries.Select(NameHash.Compute)];

    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));
    private readonly FcbClassDefinitions _defs = BundledAssets.LoadFcbClasses();

    private sealed record Scenario(
        string Root, Dictionary<uint, byte[]> Base, string StockEdited, string WorldEdited, string WorldCopied,
        string DlcCopied);

    [Fact]
    public void Copies_of_world_archetypes_become_edits_to_both_worlds()
    {
        if (Build() is not { } scenario) return;

        (LegacyImportResult result, FolderModLayer workspace) = Import(scenario, withBaseOverride: true);

        foreach (uint world in Worlds)
        {
            FragmentOverride moved = Assert.Single(workspace.FragmentOverrides[world]);
            Assert.True(FcbFragments.IdComparer.Equals(scenario.WorldEdited, moved.FragmentId));

            string xml = Encoding.UTF8.GetString(workspace.Read(moved.EntryHash));
            FcbObject staged = FcbXml.FromXml(xml);
            Assert.NotNull(TestSupport.FindNodeWithValue(staged, Edit));
            Assert.Empty(XElement.Parse(xml).Descendants("rml"));
            FcbObject entity = TestSupport.FindNodeWithValue(staged, WorldHashes.DisEntityId)!;
            Assert.NotEqual(999_999UL, FcbEntityFields.ReadU64(entity, WorldHashes.DisEntityId));
        }

        string[] kept = [.. workspace.FragmentOverrides[OverrideHash].Select(f => f.FragmentId)];
        Assert.Equal(3, kept.Length);
        Assert.Contains(kept, id => FcbFragments.IdComparer.Equals(id, scenario.StockEdited));
        Assert.Contains(kept, id => FcbFragments.IdComparer.Equals(id, AddedId));
        Assert.Contains(kept, ContainerLayout.IsLayoutId);

        Assert.Contains(result.Unreachable, note => note.Reason.Contains("DLC archetype"));
    }

    /// <summary>GOG ships no override, so what no other library declares stays in one of the mod's
    /// own - and only that.</summary>
    [Fact]
    public void With_no_base_override_only_archetypes_no_library_declares_stay_in_one()
    {
        if (Build() is not { } scenario) return;

        (LegacyImportResult result, FolderModLayer workspace) = Import(scenario, withBaseOverride: false);

        HashSet<string> ids = Ids(FcbDocument.Deserialize(workspace.Read(OverrideHash)));
        Assert.Contains(scenario.StockEdited, ids);
        Assert.Contains(AddedId, ids);
        Assert.DoesNotContain(scenario.WorldEdited, ids);
        Assert.DoesNotContain(scenario.WorldCopied, ids);
        Assert.DoesNotContain(scenario.DlcCopied, ids);
        Assert.Single(result.WholeFile);

        foreach (uint world in Worlds)
        {
            string[] moved = [.. workspace.FragmentOverrides[world].Select(f => f.FragmentId)];
            Assert.Contains(moved, id => FcbFragments.IdComparer.Equals(id, scenario.WorldEdited));
            Assert.DoesNotContain(moved, id => FcbFragments.IdComparer.Equals(id, scenario.WorldCopied));
        }
    }

    private Scenario? Build()
    {
        if (Fixture.Read(FcbDocumentTests.PatchOverride) is not { } stockBytes
            || Fixture.Read(FcbDocumentTests.World1) is not { } worldBytes
            || Fixture.Read(FcbDocumentTests.Dlc1) is not { } dlcBytes)
        {
            return null;
        }

        FcbObject stock = FcbDocument.Deserialize(stockBytes);
        FcbObject world = FcbDocument.Deserialize(worldBytes);
        FcbObject dlc = FcbDocument.Deserialize(dlcBytes);
        HashSet<string> stockIds = Ids(stock), worldIds = Ids(world), dlcIds = Ids(dlc);

        // Stock archetypes no other library declares.
        FcbFragment[] stockOnly = [.. FcbFragments.List(stock).Where(f => !worldIds.Contains(f.Id) && !dlcIds.Contains(f.Id)).Take(2)];
        FcbFragment[] worldOnly = [.. FcbFragments.List(world)
            .Where(f => !stockIds.Contains(f.Id) && !dlcIds.Contains(f.Id) && HasDescriptor(f.Node)).Take(2)];
        FcbFragment dlcOnly = FcbFragments.List(dlc)
            .First(f => !stockIds.Contains(f.Id) && !worldIds.Contains(f.Id) && HasDescriptor(f.Node));

        foreach (FcbObject group in stock.Children)
        {
            group.Children.Remove(stockOnly[1].Node);
        }

        FcbObject added = Renamed(worldOnly[0].Node, "Test", "Added");
        byte[] legacyOverride = FcbAssembler.Apply(FcbDocument.Serialize(stock), new Dictionary<string, string>
        {
            [stockOnly[0].Id] = Xml(Edited(stockOnly[0].Node)),
            [worldOnly[0].Id] = Xml(Edited(Copied(worldOnly[0].Node))),
            [worldOnly[1].Id] = Xml(Copied(worldOnly[1].Node)),
            [dlcOnly.Id] = Xml(Copied(dlcOnly.Node)),
            [AddedId] = Xml(added),
        });
        byte[] legacyWorld = FcbAssembler.Apply(worldBytes, new Dictionary<string, string>
        {
            [worldOnly[1].Id] = Xml(Edited(worldOnly[1].Node)),
            [AddedId] = Xml(Edited(added)),
        });

        string root = Path.Combine(_sandbox, "legacy");
        Write(root, LegacyPatchImporter.PatchOverride, legacyOverride);
        Write(root, LegacyPatchImporter.WorldLibraries[0], legacyWorld);

        var baseGame = new Dictionary<uint, byte[]>
        {
            [OverrideHash] = stockBytes,
            [Worlds[0]] = worldBytes,
            [Worlds[1]] = worldBytes,
            [NameHash.Compute(LegacyPatchImporter.DlcLibrary)] = dlcBytes,
        };
        return new Scenario(root, baseGame, stockOnly[0].Id, worldOnly[0].Id, worldOnly[1].Id, dlcOnly.Id);
    }

    private (LegacyImportResult, FolderModLayer) Import(Scenario scenario, bool withBaseOverride)
    {
        Dictionary<uint, byte[]> baseGame = new(scenario.Base);
        if (!withBaseOverride)
        {
            baseGame.Remove(OverrideHash);
        }

        string dir = Path.Combine(_sandbox, withBaseOverride ? "steam" : "gog");
        Directory.CreateDirectory(dir);
        var workspace = new FolderModLayer(dir, "workspace");
        LegacyImportResult result = LegacyPatchImporter.ImportTree(
            scenario.Root, workspace, TestSupport.LoadNames(), _defs,
            hash => baseGame.GetValueOrDefault(hash),
            hash => baseGame.TryGetValue(hash, out byte[]? bytes) ? XxHash64.HashToUInt64(bytes) : null);
        return (result, workspace);
    }

    private static HashSet<string> Ids(FcbObject library)
        => new(FcbFragments.List(library).Select(f => f.Id), FcbFragments.IdComparer);

    private static bool HasDescriptor(FcbObject archetype)
        => TestSupport.FindNodeWithValue(archetype, WorldHashes.HidDescriptor) is { } node
           && FcbXml.TryDecodeRmlValue(node.Values[WorldHashes.HidDescriptor]) is not null;

    private string Xml(FcbObject node) => FcbXml.ToXml(node, _defs);

    private static FcbObject Edited(FcbObject archetype)
    {
        FcbObject edited = archetype.Clone();
        edited.Values[Edit] = BitConverter.GetBytes(1f);
        return edited;
    }

    /// <summary>The archetype as a legacy tool copies it: its rml wrapped, its id another library's.</summary>
    private static FcbObject Copied(FcbObject archetype)
    {
        FcbObject copy = archetype.Clone();
        FcbObject descriptor = TestSupport.FindNodeWithValue(copy, WorldHashes.HidDescriptor)!;
        descriptor.Values[WorldHashes.HidDescriptor] = FcbXml.EncodeRmlValue(
            new XElement("rml", FcbXml.TryDecodeRmlValue(descriptor.Values[WorldHashes.HidDescriptor])));
        TestSupport.FindNodeWithValue(copy, WorldHashes.DisEntityId)!.Values[WorldHashes.DisEntityId] = BitConverter.GetBytes(999_999UL);
        return copy;
    }

    private static FcbObject Renamed(FcbObject archetype, string group, string name)
    {
        FcbObject renamed = archetype.Clone();
        renamed.Values[WorldHashes.Name] = FcbEntityFields.StringBytes(name);
        TestSupport.FindNodeWithValue(renamed, WorldHashes.HidName)!.Values[WorldHashes.HidName] =
            FcbEntityFields.StringBytes($"{group}.{name}");
        return renamed;
    }

    private static void Write(string root, string path, byte[] bytes)
    {
        string file = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, bytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }
}
