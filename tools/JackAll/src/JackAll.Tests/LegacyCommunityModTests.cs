using System.IO.Compression;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.Tests;

/// <summary>
/// The import run against the legacy mods people still install, rather than one this suite built
/// itself.
/// </summary>
/// <remarks>
/// A manufactured patch only ever contains the edit the test put there. These carry hundreds of
/// containers written by tools nobody here controls, which is the only way to find out that a
/// worldsector arrives with its entities redistributed across new mission layers - a shape no
/// per-fragment override can express, and the reason `.fcb` keeps its whole-file fallback.
///
/// Whether a given mod yields fragments at all is a fact about that mod, not about the import, so
/// what is asserted here is what must hold for any of them; the per-format fragment paths are
/// pinned in <see cref="LegacyPatchImporterTests"/>.
///
/// Needs a real install besides the two mods under <c>Fixtures\Mods</c>: <c>JACKALL_FC2_INSTALL</c>
/// names it, or it is looked for at the conventional install roots.
/// </remarks>
public sealed class LegacyCommunityModTests : IDisposable
{
    private readonly string _sandbox =
        Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Where a zipped mod's patch archive is unpacked to be read back, kept out of
    /// <see cref="_sandbox"/> so the workspace scan never walks a 25 MB patch.dat.</summary>
    private readonly string _unpacked =
        Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N") + "-unpacked");

    private static readonly Lazy<FcbClassDefinitions> Definitions = new(BundledAssets.LoadFcbClasses);

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found()
    {
        Fixture.AssertPresent("Mods/functional_outposts.zip", "Mods/relaxed");
        Assert.True(Install() is not null, $"No install at {InstallRoot()}; point {InstallVariable} at one.");
    }

    /// <summary>
    /// A legacy mod converts into a layer that keeps its edits: real fragments where the containers
    /// allow it, and nothing quietly dropped where they don't.
    /// </summary>
    [Theory]
    // Zipped, and splits world sectors into fragments that re-file entities across mission layers.
    [InlineData("Mods/functional_outposts.zip")]
    // Already extracted, and stages only whole files - its entity library because a fragment moved.
    [InlineData("Mods/relaxed")]
    public void A_community_mod_converts_into_a_layer_that_keeps_its_edits(string mod)
    {
        if (Fixture.Locate(mod) is not { } modPath || Install() is not { } install) return;

        NameDatabase names = TestSupport.LoadNames();
        FcbClassDefinitions definitions = Definitions.Value;
        Directory.CreateDirectory(_sandbox);
        var workspace = new FolderModLayer(_sandbox, "workspace");

        using GameVfs vfs = ModPipeline.OpenOriginals(install, names);
        LegacyImportResult result = ModPipeline.IsZipSource(modPath)
            ? LegacyPatchImporter.Import(
                modPath, workspace, names, definitions, vfs.ReadOriginal, vfs.ReadOriginalHash)
            : LegacyPatchImporter.ImportFromDirectory(
                modPath, workspace, names, definitions, vfs.ReadOriginal, vfs.ReadOriginalHash);

        // None of these mods touches a MOVE graph, so a refusal is news either way: a mod that does,
        // or the shape comparison gone wrong.
        Assert.Empty(result.Refused);
        Assert.True(
            result.Imported + result.FragmentsImported > 0,
            $"{Path.GetFileName(modPath)} converted to an empty layer, so the diff threw its edits away.");

        // A container is overridden one way or the other, never both: a layer that staged a whole
        // file and fragments of it would be telling the build two different things.
        workspace.Rescan();
        Assert.DoesNotContain(workspace.FragmentOverrides.Keys, workspace.Hashes.Contains);

        AssertRebuildsEveryContainerItSplit(modPath, workspace, vfs, definitions);
    }

    /// <summary>
    /// The check the import cannot make about itself: for every world sector it chose to split, the
    /// fragments it staged, applied to the base game's own copy, put every entity under the same
    /// mission layer the mod's own container has it under. A sector that re-files entities is the case
    /// this exists for - the layout override is the only thing that can carry it.
    /// </summary>
    /// <remarks>
    /// Vacuous for a mod that touches no sector, as the extracted one doesn't. That the
    /// mechanism works at all is pinned deterministically in <see cref="LegacyPatchImporterTests"/>;
    /// what this adds is the real mods nobody here wrote.
    /// </remarks>
    private void AssertRebuildsEveryContainerItSplit(
        string modPath, FolderModLayer workspace, GameVfs vfs, FcbClassDefinitions definitions)
    {
        (string fat, string dat) = PatchPairOf(modPath);
        using DuniaArchive mod = DuniaArchive.Open(fat, dat);
        var splitter = new FcbContainerSplitter(definitions);

        foreach ((uint containerHash, IReadOnlyList<FragmentOverride> staged) in workspace.FragmentOverrides)
        {
            // A layer overrides fragments of `depload` and string-table containers too, and neither
            // is an FCB tree - this check is about the containers that place entities in layers.
            if (vfs.ReadOriginal(containerHash) is not { } vanilla
                || TryReadLayerBearing(mod, containerHash) is not { } theirs)
            {
                continue;
            }

            Dictionary<string, string> byId = staged.ToDictionary(
                o => o.FragmentId,
                o => System.Text.Encoding.UTF8.GetString(workspace.Read(o.EntryHash)),
                FcbFragments.IdComparer);

            FcbObject ours = FcbDocument.Deserialize(splitter.Apply(vanilla, byId));
            Assert.Equal(PlacementOf(theirs), PlacementOf(ours));
        }
    }

    /// <summary>The mod's own copy of a container, when it places entities in mission layers at all -
    /// a world sector, or a world's omnis, managers or mapsdata.</summary>
    private static FcbObject? TryReadLayerBearing(DuniaArchive mod, uint containerHash)
    {
        try
        {
            FcbObject root = FcbDocument.Deserialize(mod.Read(containerHash));
            return FcbFragments.IsLayerBearing(root) ? root : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Every placed entity and the mission layer it sits under, qualified by the level cell
    /// where the container groups layers into cells - mapsdata holds one <c>main</c> per cell.</summary>
    private static SortedDictionary<ulong, string> PlacementOf(FcbObject root)
    {
        var placement = new SortedDictionary<ulong, string>();
        foreach ((string? cell, FcbObject layer) in FcbFragments.KeyedLayersOf(root))
        {
            string key = LayerSpec.KeyOf(cell, MissionLayers.NameOf(layer));
            foreach (FcbObject entity in layer.Children.Where(e => e.TypeHash == WorldHashes.Entity))
            {
                placement[FcbEntityFields.ReadU64(entity, WorldHashes.DisEntityId)] = key;
            }
        }
        return placement;
    }

    /// <summary>The mod's own patch archive, unpacked out of its zip first when that is how it ships -
    /// the check below has to cover a zipped mod too, not quietly pass over it.</summary>
    private (string Fat, string Dat) PatchPairOf(string modPath)
    {
        if (!ModPipeline.IsZipSource(modPath))
        {
            return LegacyPatchImporter.FindPatchPair(modPath)
                ?? throw new InvalidOperationException($"{modPath} has no patch pair, but it imported.");
        }

        (string fat, string dat) = LegacyPatchImporter.FindPatchPairInZip(modPath)
            ?? throw new InvalidOperationException($"{modPath} has no patch pair in it, but it imported.");

        Directory.CreateDirectory(_unpacked);
        using ZipArchive zip = ZipFile.OpenRead(modPath);
        string fatPath = Path.Combine(_unpacked, "patch.fat");
        string datPath = Path.Combine(_unpacked, "patch.dat");
        zip.GetEntry(fat)!.ExtractToFile(fatPath, overwrite: true);
        zip.GetEntry(dat)!.ExtractToFile(datPath, overwrite: true);
        return (fatPath, datPath);
    }

    private const string InstallVariable = "JACKALL_FC2_INSTALL";

    /// <summary>
    /// Where the game is. Conventional install roots are tried after the variable, because a machine
    /// that runs the game at all almost certainly has it at one of them.
    /// </summary>
    private static string? InstallRoot()
    {
        string[] candidates =
        [
            Environment.GetEnvironmentVariable(InstallVariable) ?? string.Empty,
            @"C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2",
            @"C:\Games\Far Cry 2",
        ];

        return candidates.FirstOrDefault(root => root.Length > 0 && Directory.Exists(root));
    }

    private static GameInstall? Install()
        => InstallRoot() is { } root ? GameInstall.TryOpen(root, out _) : null;

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_unpacked, recursive: true); } catch { /* best effort */ }
    }
}
