using JackAll.Core.Format.Move;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// The end-to-end check on a real mod: the VSS Vintorez's MOVE edit, staged as fragments, rebuilds
/// the graph it used to ship as a 1.8 MB whole-file override.
/// </summary>
/// <remarks>
/// A real mod against a known-good target is worth more than any synthetic case, and it needs no
/// game launch: if the bytes match, the game cannot tell the difference.
/// </remarks>
[Trait("Category", "RequiresFixture")]
public sealed class MoveVssMigrationTests
{
    private static string Vanilla =>
        Path.Combine(Fc2Corpus.Root, "common", "graphics", "move", "movemgr.bin");

    private static string StagedFragments =>
        Path.Combine(RepoRoot(), "mods", "vss-vintorez", "layer", "mods",
            "graphics", "move", "movemgr.bin");

    /// <summary>
    /// What the mod actually changes: 81 clip references and nothing else. Measured against the
    /// binary it shipped before the migration.
    /// </summary>
    private const int ExpectedClipEdits = 81;

    [Fact]
    public void The_vss_fragments_change_only_the_clips_they_mean_to()
    {
        if (!File.Exists(Vanilla) || !Directory.Exists(StagedFragments))
        {
            return;
        }

        byte[] vanilla = File.ReadAllBytes(Vanilla);
        Dictionary<string, string> staged = Directory.EnumerateFiles(StagedFragments, "*.xml")
            .ToDictionary(Path.GetFileName, File.ReadAllText)!;
        Assert.NotEmpty(staged);

        byte[] built = MoveContainerSplitter.Instance.Apply(vanilla, staged);

        MoveFile before = MoveCodec.Load(vanilla);
        MoveFile after = MoveCodec.Load(built);

        // Same shape: nothing added, removed or reordered - a repoint rewrites values in place.
        Assert.Equal(before.Objects.Count, after.Objects.Count);
        Assert.Equal(vanilla.Length, built.Length);

        int changed = 0;
        for (int i = 0; i < before.Objects.Count; i++)
        {
            MoveObject a = before.Objects[i];
            MoveObject b = after.Objects[i];
            Assert.Equal(a.ClassName, b.ClassName);
            Assert.Equal(a.Ops.Count, b.Ops.Count);
            for (int op = 0; op < a.Ops.Count; op++)
            {
                if (a.Ops[op].Number == b.Ops[op].Number)
                {
                    continue;
                }

                // The only thing a clip repoint may touch.
                Assert.Equal("m_animNameHash", a.Ops[op].Name);
                changed++;
            }
        }

        Assert.Equal(ExpectedClipEdits, changed);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);
        while (at is not null && !Directory.Exists(Path.Combine(at.FullName, "mods")))
        {
            at = at.Parent;
        }

        return at?.FullName ?? AppContext.BaseDirectory;
    }
}
