using System.IO.Compression;
using System.Text;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Move;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.Tests;

/// <summary>
/// A "legacy mod" here is built by running <see cref="PatchBuilder"/> itself against a throwaway copy
/// of the checked-in patch.dat/.fat fixture, then zipping up its output - that's exactly what the old
/// build_patch.bat-style workflow produces: a full replacement patch.dat/.fat, mostly vanilla bytes,
/// with the mod's actual edits mixed in. A second, untouched copy of the same fixture stands in for
/// "the base game" the import diffs against.
/// </summary>
public class LegacyPatchImporterTests : IDisposable
{
    private const string FixturesDir = "Fixtures/Patch";

    private readonly string _sandbox;
    private readonly GameInstall? _legacySourceInstall;
    private readonly GameInstall? _cleanInstall;

    public LegacyPatchImporterTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

        string fixtureFat = Path.Combine(FixturesDir, "patch.fat");
        string fixtureDat = Path.Combine(FixturesDir, "patch.dat");
        if (!File.Exists(fixtureFat) || !File.Exists(fixtureDat))
        {
            return;
        }

        _legacySourceInstall = MakeFakeInstall("legacy_source", fixtureFat, fixtureDat);
        _cleanInstall = MakeFakeInstall("clean", fixtureFat, fixtureDat);
    }

    private GameInstall MakeFakeInstall(string name, string fixtureFat, string fixtureDat)
    {
        string root = Path.Combine(_sandbox, name);
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "Data_Win32"));
        File.WriteAllText(Path.Combine(root, "bin", "FarCry2.exe"), "stub");
        File.Copy(fixtureFat, Path.Combine(root, "Data_Win32", "patch.fat"));
        File.Copy(fixtureDat, Path.Combine(root, "Data_Win32", "patch.dat"));
        return GameInstall.TryOpen(root, out _)!;
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixture_files_were_actually_found()
        => Assert.True(
            File.Exists(Path.Combine(FixturesDir, "patch.fat")) && File.Exists(Path.Combine(FixturesDir, "patch.dat")),
            $"{FixturesDir} had no patch.fat/patch.dat, so every test in this class silently no-opped.");

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Only_the_whole_file_and_fragment_changes_survive_the_import()
    {
        if (_legacySourceInstall is null || _cleanInstall is null) return;

        NameDatabase names = TestSupport.LoadNames();

        const string wholeFilePath = "engine/gamemodes/gamemodesconfig.xml";
        byte[] wholeFileContent = "legacy whole-file change"u8.ToArray();

        VfsFile container;
        string fragmentId;
        byte[] fragmentReplacementXml;
        using (var vfs = GameVfs.Load(_legacySourceInstall, names))
        {
            VfsFile fragment = vfs.Files.Values.First(f => TestSupport.IsFcbFragment(f) && f.NameIsKnown);
            container = vfs.Files[fragment.ContainerHash!.Value];
            fragmentId = fragment.FragmentId!;

            // Derived from the vanilla fragment with one added value, keeping its identity fields -
            // the realistic edit shape, and what lets the import attribute the change to this exact
            // fragment id rather than falling back to a coarser unit.
            FcbObject vanillaFragment = FcbFragments.Find(
                FcbDocument.Deserialize(vfs.ReadOriginal((uint)container.Hash)!), fragmentId)!;
            fragmentReplacementXml = TestSupport.RenderWithValueSetAt(vanillaFragment, [], 0xDEADBEEF, [0x2A, 0x00, 0x00, 0x00]);
        }

        var mod = MakeZipMod(
            "legacy_mod_source",
            (wholeFilePath, wholeFileContent),
            ($"{container.Path}\\{fragmentId}", fragmentReplacementXml));

        using (var vfsForRead = GameVfs.Load(_legacySourceInstall, names))
        {
            PatchBuilder.Build(_legacySourceInstall, [mod], vfsForRead.ReadOriginal);
        }

        // Zip up the just-built "legacy" patch.dat/.fat - stands in for the old-style mod a user would
        // have downloaded and copied straight into Data_Win32 by hand.
        string legacyZipPath = Path.Combine(_sandbox, "legacy_mod.zip");
        using (var zip = ZipFile.Open(legacyZipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(_legacySourceInstall.PatchFat, "Data_Win32/patch.fat");
            zip.CreateEntryFromFile(_legacySourceInstall.PatchDat, "Data_Win32/patch.dat");
        }

        string workspaceDir = Path.Combine(_sandbox, "workspace");
        Directory.CreateDirectory(workspaceDir);
        var workspace = new FolderModLayer(workspaceDir, "workspace");

        using var cleanVfs = GameVfs.Load(_cleanInstall, names);
        LegacyImportResult result = LegacyPatchImporter.Import(
            legacyZipPath, workspace, names, FcbClassDefinitions.Empty, cleanVfs.ReadOriginal, cleanVfs.ReadOriginalHash);

        // Exactly the whole-file change and the one touched fragment get staged - every other archive
        // entry, plus every untouched sibling fragment of the container that was touched, is identical
        // (byte-for-byte, or logically for fragments) to the clean fixture and left out as noise.
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.FragmentsImported);
        Assert.True(result.Skipped > 0);

        workspace.Rescan();

        uint wholeFileHash = NameHash.Compute(wholeFilePath);
        Assert.Contains(wholeFileHash, workspace.Hashes);
        Assert.Equal(wholeFileContent, workspace.Read(wholeFileHash));

        // The edit kept the fragment's identity fields, so the staged override lands on the very id
        // that was edited - not a coarser group or whole-file fallback - with the edited content.
        Assert.True(workspace.FragmentOverrides.TryGetValue((uint)container.Hash, out var overrides));
        FragmentOverride staged = Assert.Single(overrides!);
        Assert.True(FcbFragments.IdComparer.Equals(fragmentId, staged.FragmentId));
        Assert.Equal(fragmentReplacementXml, workspace.Read(staged.EntryHash));
    }

    /// <summary>
    /// A `depload.dat` a legacy mod changed comes back as the one resource whose dependencies moved,
    /// not as the whole manifest - the granularity two mods need in order to coexist.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_changed_depload_imports_as_the_resource_that_changed()
    {
        if (_legacySourceInstall is null || _cleanInstall is null) return;

        NameDatabase names = TestSupport.LoadNames();
        const string containerPath = "worlds\\tmpla\\generated\\tmpla_depload.dat";
        uint containerHash = NameHash.Compute(containerPath);

        byte[] edited;
        string fragmentId;
        using (var vfs = GameVfs.Load(_cleanInstall, names))
        {
            byte[] vanilla = vfs.ReadOriginal(containerHash)!;
            DepLoadParent parent = DepLoadDocument.Decode(vanilla).Parents.First(p => p.Children.Count > 0);
            fragmentId = DepLoadContainerSplitter.IdOf(parent.Hash);
            DepLoadParent withOneMore = parent with
            {
                Children = [.. parent.Children, new DepLoadChild(0xFEEDFACE, parent.Children[0].TypeHash)],
            };
            edited = DepLoadContainerSplitter.Instance.Apply(
                vanilla,
                new Dictionary<string, string> { [fragmentId] = DepLoadXml.FragmentToXml(withOneMore) });
        }

        string zipPath = BuildLegacyPatch(
            "depload", names, MakeZipMod("depload_source", (containerPath, edited)));
        (LegacyImportResult result, FolderModLayer workspace) = ImportLegacy(zipPath, "depload_ws", names);

        Assert.Equal(1, result.FragmentsImported);
        Assert.Empty(result.Refused);
        Assert.DoesNotContain(containerHash, workspace.Hashes);

        FragmentOverride staged = Assert.Single(workspace.FragmentOverrides[containerHash]);
        Assert.True(FcbFragments.IdComparer.Equals(fragmentId, staged.FragmentId));
    }

    /// <summary>
    /// The VSS Vintorez's animation edit, shipped the old way as a whole 1.8 MB graph, comes back as
    /// the branches it actually changed - and rebuilds to the very bytes the legacy patch carried.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_changed_move_graph_imports_as_the_states_that_changed()
    {
        if (_legacySourceInstall is null || _cleanInstall is null || VssMoveFragments() is not { } vss)
        {
            return;
        }

        NameDatabase names = TestSupport.LoadNames();
        byte[] vanillaGraph = File.ReadAllBytes(VanillaMoveGraph);
        byte[] editedGraph = MoveContainerSplitter.Instance.Apply(vanillaGraph, vss);

        Seed(_cleanInstall, names, MakeZipMod("move_vanilla", (MoveGraphPath, vanillaGraph)));
        string zipPath = BuildLegacyPatch(
            "move", names, MakeZipMod("move_source", (MoveGraphPath, editedGraph)));
        (LegacyImportResult result, FolderModLayer workspace) = ImportLegacy(zipPath, "move_ws", names);

        Assert.Empty(result.Refused);
        Assert.Equal(vss.Count, result.FragmentsImported);

        uint containerHash = NameHash.Compute(MoveGraphPath);
        Assert.DoesNotContain(containerHash, workspace.Hashes);

        Dictionary<string, string> imported = workspace.FragmentOverrides[containerHash]
            .ToDictionary(o => o.FragmentId, o => Encoding.UTF8.GetString(workspace.Read(o.EntryHash)!));
        Assert.Equal(
            vss.Keys.Select(FcbFragments.Canonicalize).Order(),
            imported.Keys.Select(FcbFragments.Canonicalize).Order());

        // The point of the whole exercise: what the layer builds is what the legacy patch shipped.
        Assert.Equal(editedGraph, MoveContainerSplitter.Instance.Apply(vanillaGraph, imported));
    }

    /// <summary>
    /// A MOVE graph whose change fragments cannot express is left out and reported, not coarsened:
    /// a whole-file override of one is last-wins against every other animation mod, silently.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_move_graph_a_fragment_cannot_express_is_left_out_rather_than_overridden_whole()
    {
        if (_legacySourceInstall is null || _cleanInstall is null || !File.Exists(VanillaMoveGraph))
        {
            return;
        }

        NameDatabase names = TestSupport.LoadNames();
        byte[] vanillaGraph = File.ReadAllBytes(VanillaMoveGraph);

        // Two states swap places in the machine's slot list. Every fragment still says the same
        // thing; the order they sit in is the container's own, and no override carries one.
        MoveFile graph = MoveCodec.Load(vanillaGraph);
        MoveObject machine = graph.StateMachine!;
        List<int> slots = [.. Enumerable.Range(0, machine.Ops.Count)
            .Where(i => machine.Ops[i].Name == "CMoveBaseState"
                        && machine.Ops[i].Kind == MoveOpKind.PointerNew)];
        (machine.Ops[slots[^1]], machine.Ops[slots[^2]]) = (machine.Ops[slots[^2]], machine.Ops[slots[^1]]);
        byte[] reorderedGraph = MoveCodec.Save(graph);

        Seed(_cleanInstall, names, MakeZipMod("reorder_vanilla", (MoveGraphPath, vanillaGraph)));
        string zipPath = BuildLegacyPatch(
            "reorder", names, MakeZipMod("reorder_source", (MoveGraphPath, reorderedGraph)));

        // Without this the refusal below would pass for the wrong reason - "no copy to compare
        // against" rather than "this change won't fit in a fragment".
        uint containerHash = NameHash.Compute(MoveGraphPath);
        using (var vfs = GameVfs.Load(_cleanInstall, names))
        {
            Assert.NotNull(vfs.ReadOriginal(containerHash));
        }

        (LegacyImportResult result, FolderModLayer workspace) = ImportLegacy(zipPath, "reorder_ws", names);

        LegacyImportNote refusal = Assert.Single(result.Refused);
        Assert.Equal(MoveGraphPath, refusal.ContainerPath);

        Assert.DoesNotContain(containerHash, workspace.Hashes);
        Assert.DoesNotContain(containerHash, workspace.FragmentOverrides.Keys);
    }

    private const string SectorPath =
        @"levels\mp_14_woodlands\generated\worldsectors\worldsector56.data.fcb";

    private const string SectorFixture = "Fixtures/WorldSector/worldsector56.data.fcb";

    /// <summary>
    /// The pattern behind nearly every whole-file fallback the two largest community mods produce:
    /// entities moved out of <c>main</c> into a mission layer the mod adds. It imports as the moved
    /// entity plus the sector's layout, so the mod keeps per-fragment merging instead of claiming the
    /// whole sector.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_reparented_entity_imports_as_its_fragment_plus_a_layout()
    {
        if (_legacySourceInstall is null || _cleanInstall is null || !File.Exists(SectorFixture)) return;

        NameDatabase names = TestSupport.LoadNames();
        byte[] vanillaSector = File.ReadAllBytes(SectorFixture);
        const string addedLayer = @"missions\outposts\test\zone_01";

        FcbObject moved = FcbDocument.Deserialize(vanillaSector);
        FcbObject main = moved.Children.First(c =>
            c.TypeHash == WorldHashes.MissionLayer
            && MissionLayers.IsMain(FcbEntityFields.ReadString(c, WorldHashes.TextPathId)));
        FcbObject entity = main.Children.First(e => e.TypeHash == WorldHashes.Entity);
        ulong entityId = FcbEntityFields.ReadU64(entity, WorldHashes.DisEntityId);
        main.Children.Remove(entity);
        // Real outpost mods declare the layer on the entity as well as nesting it there.
        DeclareLayer(entity, NameHash.Compute(addedLayer));
        moved.Children.Insert(0, NewLayer(addedLayer, entity));

        Seed(_cleanInstall, names, MakeZipMod("reparent_vanilla", (SectorPath, vanillaSector)));
        string zipPath = BuildLegacyPatch(
            "reparent", names, MakeZipMod("reparent_source", (SectorPath, FcbDocument.Serialize(moved))));

        (LegacyImportResult result, FolderModLayer workspace) = ImportLegacy(zipPath, "reparent_ws", names);

        Assert.Empty(result.Refused);
        Assert.Empty(result.WholeFile);
        Assert.Equal(0, result.Imported);
        Assert.Equal(2, result.FragmentsImported); // the entity, and the sector's layout

        uint containerHash = NameHash.Compute(SectorPath);
        Assert.DoesNotContain(containerHash, workspace.Hashes);
        Assert.True(workspace.FragmentOverrides.TryGetValue(containerHash, out var staged));
        Assert.Contains(staged!, o => ContainerLayout.IsLayoutId(o.FragmentId));

        // The real test of the import: the staged pieces put the entity back under the mod's own
        // layer, without staging the sector.
        var splitter = new FcbContainerSplitter(FcbClassDefinitions.Empty);
        Dictionary<string, string> byId = staged!.ToDictionary(
            o => o.FragmentId, o => Encoding.UTF8.GetString(workspace.Read(o.EntryHash)), FcbFragments.IdComparer);
        IContainerTree rebuilt = splitter.Open(splitter.Apply(vanillaSector, byId));

        Assert.Equal(addedLayer, rebuilt.AncestryOf($"{entityId}.xml")!.ParentName);
    }

    /// <summary>A sector whose entities were only shuffled inside one layer is not a reparent, so it
    /// still falls back - and the fallback still says so.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_reordered_sector_still_stages_whole_and_says_so()
    {
        if (_legacySourceInstall is null || _cleanInstall is null || !File.Exists(SectorFixture)) return;

        NameDatabase names = TestSupport.LoadNames();
        byte[] vanillaSector = File.ReadAllBytes(SectorFixture);

        FcbObject reordered = FcbDocument.Deserialize(vanillaSector);
        FcbObject main = reordered.Children.First(c =>
            c.TypeHash == WorldHashes.MissionLayer
            && MissionLayers.IsMain(FcbEntityFields.ReadString(c, WorldHashes.TextPathId)));
        (main.Children[0], main.Children[1]) = (main.Children[1], main.Children[0]);

        Seed(_cleanInstall, names, MakeZipMod("reorder_vanilla_sector", (SectorPath, vanillaSector)));
        string zipPath = BuildLegacyPatch(
            "reorder_sector", names, MakeZipMod("reorder_sector_source", (SectorPath, FcbDocument.Serialize(reordered))));

        (LegacyImportResult result, FolderModLayer workspace) = ImportLegacy(zipPath, "reorder_sector_ws", names);

        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.FragmentsImported);
        LegacyImportNote note = Assert.Single(result.WholeFile);
        Assert.Equal(SectorPath, note.ContainerPath);
        Assert.Contains(NameHash.Compute(SectorPath), workspace.Hashes);
    }

    /// <summary>Adds the mission component an entity carries when a mod re-files it.</summary>
    private static void DeclareLayer(FcbObject entity, uint pathId)
    {
        var component = new FcbObject { TypeHash = WorldHashes.CMissionComponent };
        component.Values.Add(WorldHashes.HidMissionLayerPath, BitConverter.GetBytes(pathId));

        var components = new FcbObject { TypeHash = WorldHashes.Components };
        components.Children.Add(component);
        entity.Children.Add(components);
    }

    /// <summary>A mission layer carrying <paramref name="entity"/>, shaped the way a shipped one is:
    /// the authored path, then its id.</summary>
    private static FcbObject NewLayer(string path, FcbObject entity)
    {
        var layer = new FcbObject { TypeHash = WorldHashes.MissionLayer };
        layer.Values.Add(WorldHashes.TextPathId, [.. System.Text.Encoding.UTF8.GetBytes(path), 0]);
        layer.Values.Add(WorldHashes.PathId, BitConverter.GetBytes(NameHash.Compute(path)));
        layer.Children.Add(entity);
        return layer;
    }

    private const string MoveGraphPath = "graphics\\move\\movemgr.bin";

    private static string VanillaMoveGraph =>
        Path.Combine(Fc2Corpus.Root, "common", "graphics", "move", "movemgr.bin");

    /// <summary>The repo's own VSS Vintorez MOVE fragments, or null when either half of the pair is
    /// missing - the same real-mod-against-a-known-target pair MoveVssMigrationTests uses.</summary>
    private static Dictionary<string, string>? VssMoveFragments()
    {
        string dir = Path.Combine(TestSupport.RepositoryRoot, "mods", "vss-vintorez", "layer", "mods",
            "graphics", "move", "movemgr.bin");
        return File.Exists(VanillaMoveGraph) && Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.xml").ToDictionary(f => Path.GetFileName(f)!, File.ReadAllText)
            : null;
    }

    /// <summary>
    /// A deleted archetype imports as the container's fragments plus a layout that names it - the
    /// same unit a sector's deleted entity gets, keyed on the archetype's path-shaped id.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_deleted_archetype_imports_as_a_layout_that_names_it()
    {
        if (_legacySourceInstall is null || _cleanInstall is null) return;

        NameDatabase names = TestSupport.LoadNames();

        VfsFile container;
        byte[] withOneArchetypeRemoved;
        string removedId;
        using (var vfs = GameVfs.Load(_legacySourceInstall, names))
        {
            VfsFile fragment = vfs.Files.Values.First(f => TestSupport.IsFcbFragment(f) && f.NameIsKnown);
            container = vfs.Files[fragment.ContainerHash!.Value];

            FcbObject tree = FcbDocument.Deserialize(vfs.ReadOriginal((uint)container.Hash)!);
            FcbObject group = tree.Children.First(c => c.Children.Count > 1);
            removedId = FcbFragments.List(tree)
                .First(f => ReferenceEquals(f.Node, group.Children[0])).Id;
            group.Children.RemoveAt(0);
            withOneArchetypeRemoved = FcbDocument.Serialize(tree);
        }

        var mod = MakeZipMod("deletion_source", (container.Path, withOneArchetypeRemoved));
        using (var vfsForRead = GameVfs.Load(_legacySourceInstall, names))
        {
            PatchBuilder.Build(_legacySourceInstall, [mod], vfsForRead.ReadOriginal);
        }

        string legacyZipPath = Path.Combine(_sandbox, "deletion_mod.zip");
        using (var zip = ZipFile.Open(legacyZipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(_legacySourceInstall.PatchFat, "Data_Win32/patch.fat");
            zip.CreateEntryFromFile(_legacySourceInstall.PatchDat, "Data_Win32/patch.dat");
        }

        string workspaceDir = Path.Combine(_sandbox, "deletion_workspace");
        Directory.CreateDirectory(workspaceDir);
        var workspace = new FolderModLayer(workspaceDir, "workspace");

        using var cleanVfs = GameVfs.Load(_cleanInstall, names);
        LegacyImportResult result = LegacyPatchImporter.Import(
            legacyZipPath, workspace, names, FcbClassDefinitions.Empty, cleanVfs.ReadOriginal, cleanVfs.ReadOriginalHash);

        Assert.Equal(0, result.Imported);
        Assert.Empty(result.WholeFile);

        workspace.Rescan();
        Assert.DoesNotContain((uint)container.Hash, workspace.Hashes);
        IReadOnlyList<FragmentOverride>? staged = workspace.FragmentOverrides[(uint)container.Hash];

        // The layout is what carries the deletion, and it names the archetype by its path-shaped id.
        FragmentOverride layout = Assert.Single(staged!, o => ContainerLayout.IsLayoutId(o.FragmentId));
        string xml = System.Text.Encoding.UTF8.GetString(workspace.Read(layout.EntryHash));
        Assert.Equal([removedId], ContainerLayout.Parse(xml).Deleted);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Importing_an_extracted_folder_matches_importing_the_same_mod_as_a_zip()
    {
        if (_legacySourceInstall is null || _cleanInstall is null) return;

        NameDatabase names = TestSupport.LoadNames();
        const string wholeFilePath = "engine/gamemodes/gamemodesconfig.xml";
        byte[] wholeFileContent = "legacy whole-file change"u8.ToArray();

        var mod = MakeZipMod("folder_vs_zip_source", (wholeFilePath, wholeFileContent));
        using (var vfsForRead = GameVfs.Load(_legacySourceInstall, names))
        {
            PatchBuilder.Build(_legacySourceInstall, [mod], vfsForRead.ReadOriginal);
        }

        // The same built patch, offered two ways: still zipped, and already extracted the way a mod
        // manager hands it over. The directory overload is the real body now, so this pins that the
        // zip wrapper didn't grow a behaviour of its own.
        string legacyZipPath = Path.Combine(_sandbox, "folder_vs_zip.zip");
        using (var zip = ZipFile.Open(legacyZipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(_legacySourceInstall.PatchFat, "Data_Win32/patch.fat");
            zip.CreateEntryFromFile(_legacySourceInstall.PatchDat, "Data_Win32/patch.dat");
        }

        string extractedDir = Path.Combine(_sandbox, "extracted", "Data_Win32");
        Directory.CreateDirectory(extractedDir);
        File.Copy(_legacySourceInstall.PatchFat, Path.Combine(extractedDir, "patch.fat"));
        File.Copy(_legacySourceInstall.PatchDat, Path.Combine(extractedDir, "patch.dat"));

        using var cleanVfs = GameVfs.Load(_cleanInstall, names);

        LegacyImportResult fromZip = LegacyPatchImporter.Import(
            legacyZipPath, MakeWorkspace("ws_zip"), names, FcbClassDefinitions.Empty, cleanVfs.ReadOriginal,
            cleanVfs.ReadOriginalHash);

        (string Fat, string Dat)? pair = LegacyPatchImporter.FindPatchPair(Path.Combine(_sandbox, "extracted"));
        Assert.NotNull(pair);

        FolderModLayer folderWorkspace = MakeWorkspace("ws_folder");
        LegacyImportResult fromFolder = LegacyPatchImporter.Import(
            pair!.Value.Fat, pair.Value.Dat, folderWorkspace, names, FcbClassDefinitions.Empty,
            cleanVfs.ReadOriginal, cleanVfs.ReadOriginalHash);

        Assert.Equal(fromZip, fromFolder);
        Assert.Equal(1, fromFolder.Imported);

        folderWorkspace.Rescan();
        Assert.Equal(wholeFileContent, folderWorkspace.Read(NameHash.Compute(wholeFilePath)));
    }

    [Fact]
    public void A_folder_with_no_patch_pair_is_reported_as_not_being_a_legacy_mod()
    {
        string dir = Path.Combine(_sandbox, "plain_mod", "worlds", "world1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "foo.xml"), "hi");

        Assert.Null(LegacyPatchImporter.FindPatchPair(Path.Combine(_sandbox, "plain_mod")));
        Assert.Null(LegacyPatchImporter.FindPatchPair(Path.Combine(_sandbox, "does_not_exist")));
    }

    [Fact]
    public void A_lone_patch_fat_with_no_matching_dat_is_not_mistaken_for_a_legacy_mod()
    {
        string dir = Path.Combine(_sandbox, "half_patch", "Data_Win32");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "patch.fat"), "not really an index");

        Assert.Null(LegacyPatchImporter.FindPatchPair(Path.Combine(_sandbox, "half_patch")));
    }

    /// <summary>
    /// Builds a legacy patch carrying these layers' edits and zips it the way a user would have
    /// downloaded one - a full replacement patch.dat/patch.fat under Data_Win32.
    /// </summary>
    private string BuildLegacyPatch(string name, NameDatabase names, params ZipModLayer[] layers)
    {
        Seed(_legacySourceInstall!, names, layers);

        string zipPath = Path.Combine(_sandbox, $"{name}.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(_legacySourceInstall!.PatchFat, "Data_Win32/patch.fat");
        zip.CreateEntryFromFile(_legacySourceInstall.PatchDat, "Data_Win32/patch.dat");
        return zipPath;
    }

    /// <summary>
    /// Compiles layers into an install's patch archive and makes the result its new baseline - how a
    /// container the fixture doesn't ship gets into one, so the import has something to call vanilla.
    /// The backup the build takes on its way in has to go with it, or <see cref="GameVfs"/> keeps
    /// answering from the pre-seed archive and the container looks like it was never there.
    /// </summary>
    private static void Seed(GameInstall install, NameDatabase names, params ZipModLayer[] layers)
    {
        using (var vfs = GameVfs.Load(install, names))
        {
            PatchBuilder.Build(install, layers, vfs.ReadOriginal);
        }

        File.Delete(install.VanillaPatchFat);
        File.Delete(install.VanillaPatchDat);
    }

    private (LegacyImportResult Result, FolderModLayer Workspace) ImportLegacy(
        string zipPath, string workspaceName, NameDatabase names)
    {
        FolderModLayer workspace = MakeWorkspace(workspaceName);
        using var cleanVfs = GameVfs.Load(_cleanInstall!, names);
        LegacyImportResult result = LegacyPatchImporter.Import(
            zipPath, workspace, names, FcbClassDefinitions.Empty,
            cleanVfs.ReadOriginal, cleanVfs.ReadOriginalHash);
        workspace.Rescan();
        return (result, workspace);
    }

    private FolderModLayer MakeWorkspace(string name)
    {
        string dir = Path.Combine(_sandbox, name);
        Directory.CreateDirectory(dir);
        return new FolderModLayer(dir, name);
    }

    [Fact]
    public void A_zip_with_no_patch_pair_is_rejected_rather_than_silently_no_opping()
    {
        Directory.CreateDirectory(_sandbox);
        string zipPath = Path.Combine(_sandbox, "not_legacy.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("worlds/world1/generated/foo.xml");
            using var stream = entry.Open();
            stream.Write("hi"u8);
        }

        string workspaceDir = Path.Combine(_sandbox, "workspace2");
        Directory.CreateDirectory(workspaceDir);
        var workspace = new FolderModLayer(workspaceDir, "workspace");

        Assert.Throws<InvalidDataException>(() => LegacyPatchImporter.Import(
            zipPath, workspace, NameDatabase.LoadFrom([]), FcbClassDefinitions.Empty, _ => null, _ => null));
    }

    private ZipModLayer MakeZipMod(string name, params (string Path, byte[] Content)[] files)
    {
        string zipPath = Path.Combine(_sandbox, $"{name}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach ((string path, byte[] content) in files)
            {
                // Callers pass game paths; the layer contract wants them under mods\.
                var entry = zip.CreateEntry($"mods/{path}");
                using var stream = entry.Open();
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

    private static float Nudge(float value, int ulps)
        => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value) + ulps);

    private static string Frag(object value)
        => $"""<object><field name="x">{value}</field></object>""";

    /// <summary>
    /// A community mod's editor rewrites every float it reads, so a fragment whose only difference is
    /// that rounding is not an edit at all.
    /// </summary>
    [Fact]
    public void A_float_inside_the_precision_interval_is_not_a_change()
    {
        const float value = 0.36544f;

        Assert.True(LegacyPatchImporter.SameWithinFloatNoise(
            Frag(value), Frag(Nudge(value, LegacyPatchImporter.FloatNoiseUlps))));
    }

    /// <summary>The other half of the interval: one step past it is a real edit and must still stage.
    /// Without this the test above would pass just as well for a filter that swallowed everything.</summary>
    [Fact]
    public void A_float_past_the_precision_interval_is_still_a_change()
    {
        const float value = 0.36544f;

        Assert.False(LegacyPatchImporter.SameWithinFloatNoise(
            Frag(value), Frag(Nudge(value, LegacyPatchImporter.FloatNoiseUlps + 1))));
        Assert.False(LegacyPatchImporter.SameWithinFloatNoise(Frag(value), Frag(0.4f)));
    }

    /// <summary>
    /// Two whole numbers are compared exactly. These two ids are one apart and round to the same
    /// float32, so a tolerance applied to every number would wave a changed id through as rounding.
    /// </summary>
    [Fact]
    public void A_whole_number_is_compared_exactly_however_close_it_rounds()
    {
        Assert.False(LegacyPatchImporter.SameWithinFloatNoise(Frag("1073741824"), Frag("1073741825")));
        Assert.True(LegacyPatchImporter.SameWithinFloatNoise(Frag("1073741824"), Frag("1073741824")));
    }

    /// <summary>The tolerance reaches values only - never a name, an attribute, or the shape.</summary>
    [Fact]
    public void Only_a_values_precision_is_forgiven()
    {
        Assert.False(LegacyPatchImporter.SameWithinFloatNoise(
            """<object><field name="x">1.5</field></object>""",
            """<object><field name="y">1.5</field></object>"""));
        Assert.False(LegacyPatchImporter.SameWithinFloatNoise(
            """<object><field name="x">1.5</field></object>""",
            """<object><field name="x">1.5</field><field name="z">2.5</field></object>"""));
        Assert.False(LegacyPatchImporter.SameWithinFloatNoise(Frag(1.5f), "not xml at all"));
    }

    /// <summary>A sign flip is a real change, not a rounding step across zero.</summary>
    [Fact]
    public void A_float_that_changes_sign_is_a_change()
    {
        float tiny = Nudge(0f, 2);

        Assert.False(LegacyPatchImporter.SameWithinFloatNoise(Frag(tiny), Frag(-tiny)));
    }

    /// <summary>
    /// A fragment staged for a real edit must not carry the editor's rounding on everything else:
    /// those values would overwrite vanilla's own on build, which is the whole point of the interval.
    /// </summary>
    [Fact]
    public void A_fragment_staged_for_a_real_edit_keeps_vanillas_other_floats()
    {
        const float slope = 60f;
        string vanilla = $"""
            <object><field name="slope">{slope}</field><field name="range">15</field></object>
            """;
        string legacy = $"""
            <object><field name="slope">{Nudge(slope, 1)}</field><field name="range">40</field></object>
            """;

        string restored = Assert.IsType<string>(LegacyPatchImporter.WithoutFloatNoise(vanilla, legacy));

        // The rounded float is vanilla's again; the real edit beside it survives untouched.
        Assert.Contains(">60<", restored);
        Assert.DoesNotContain("60.000004", restored);
        Assert.Contains(">40<", restored);
    }

    /// <summary>Nothing to put back means nothing to re-render, so the fragment stages as it came.</summary>
    [Fact]
    public void A_fragment_with_no_rounding_is_left_exactly_as_it_is()
    {
        string vanilla = """<object><field name="range">15</field></object>""";
        string legacy = """<object><field name="range">40</field></object>""";

        Assert.Null(LegacyPatchImporter.WithoutFloatNoise(vanilla, legacy));
        Assert.Null(LegacyPatchImporter.WithoutFloatNoise(vanilla, vanilla));
    }

    /// <summary>
    /// A value the mod added beside an untouched one must not hide it. This is how a worldsector
    /// guard arrives - it gains a mission component, and every other value on it is still rounded.
    /// </summary>
    [Fact]
    public void A_value_the_mod_added_does_not_hide_the_rounding_beside_it()
    {
        const float slope = 60f;
        string vanilla = $"""<object><field name="slope">{slope}</field></object>""";
        string legacy = $"""
            <object><field name="slope">{Nudge(slope, 1)}</field><field name="added">1</field></object>
            """;

        string restored = Assert.IsType<string>(LegacyPatchImporter.WithoutFloatNoise(vanilla, legacy));

        Assert.Contains(">60<", restored);
        Assert.DoesNotContain("60.000004", restored);
        Assert.Contains("added", restored);
    }
}
