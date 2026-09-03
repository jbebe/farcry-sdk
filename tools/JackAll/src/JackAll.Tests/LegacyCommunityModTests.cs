using JackAll.Core;
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
/// A manufactured patch only ever contains the edit the test put there. These carry thousands of
/// containers written by tools nobody here controls, which is the only way to find out that a
/// worldsector arrives with its entities redistributed across new mission layers - a shape no
/// per-fragment override can express, and the reason `.fcb` keeps its whole-file fallback.
///
/// Whether a given mod yields fragments at all is a fact about that mod, not about the import, so
/// what is asserted here is what must hold for every one of them; the per-format fragment paths are
/// pinned in <see cref="LegacyPatchImporterTests"/>.
///
/// Needs both the mods and a real install, neither of which is committed: point
/// <c>JACKALL_FC2_MODS</c> at a folder of legacy mods and <c>JACKALL_FC2_INSTALL</c> at the game. A
/// mod distributed as anything but a zip is picked up once it is extracted into that folder.
/// </remarks>
[Trait("Category", "RequiresFixture")]
public sealed class LegacyCommunityModTests : IDisposable
{
    private readonly string _sandbox =
        Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_mod_and_an_install_were_actually_found()
        => Assert.True(
            LegacyMods().Count > 0 && Install() is not null,
            $"No legacy mod under {ModRoot} or no install at {InstallRoot()}, so this gate no-opped. "
            + "Point JACKALL_FC2_MODS and JACKALL_FC2_INSTALL at them.");

    /// <summary>
    /// Every legacy mod to hand converts into a layer that keeps its edits: real fragments where the
    /// containers allow it, and nothing quietly dropped where they don't.
    /// </summary>
    [Theory]
    [MemberData(nameof(LegacyMods))]
    public void A_community_mod_converts_into_a_layer_that_keeps_its_edits(string modPath)
    {
        if (modPath.Length == 0 || Install() is not { } install) return;

        NameDatabase names = BundledAssets.LoadNames();
        FcbClassDefinitions definitions = BundledAssets.LoadFcbClasses();
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
    }

    private const string ModsVariable = "JACKALL_FC2_MODS";
    private const string InstallVariable = "JACKALL_FC2_INSTALL";

    private static string ModRoot =>
        Environment.GetEnvironmentVariable(ModsVariable) is { Length: > 0 } custom
            ? custom
            : Path.Combine(TestSupport.RepositoryRoot, "tmp", "mods");

    /// <summary>
    /// Every legacy full-patch mod under <see cref="ModRoot"/> - a zip carrying a patch.fat/patch.dat
    /// pair, or a folder someone has already extracted one into. Anything else there is an ordinary
    /// path-tree mod, which this import is not for.
    /// </summary>
    public static TheoryData<string> LegacyMods()
    {
        var found = new TheoryData<string>();
        if (!Directory.Exists(ModRoot))
        {
            return found;
        }

        foreach (string zip in Directory.EnumerateFiles(ModRoot, "*.zip").Order())
        {
            if (LegacyPatchImporter.FindPatchPairInZip(zip) is not null)
            {
                found.Add(zip);
            }
        }

        foreach (string dir in Directory.EnumerateDirectories(ModRoot).Order())
        {
            if (LegacyPatchImporter.FindPatchPair(dir) is not null)
            {
                found.Add(dir);
            }
        }

        return found;
    }

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
    }
}
