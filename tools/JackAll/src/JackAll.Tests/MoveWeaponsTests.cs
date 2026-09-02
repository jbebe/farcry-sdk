using JackAll.Core.Format.Move;

namespace JackAll.Tests;

/// <summary>
/// Locks in what "the clips weapon N plays" means.
/// </summary>
/// <remarks>
/// The failure this guards against is over-reach. A criterion pins the subtree it hangs off, but a
/// top-level state is a shared container holding one branch per weapon, so walking a whole state
/// that mentions N anywhere reaches most of the game's clips instead of that weapon's ~50. The
/// numbers below are from retail <c>movemgr.bin</c> and are stable.
/// </remarks>
public sealed class MoveWeaponsTests
{
    private const int DartRifle = 39;

    private static string BasePath =>
        Path.Combine(Fc2Corpus.Root, "common", "graphics", "move", "movemgr.bin");

    private static MoveFile Load() => MoveCodec.Load(File.ReadAllBytes(BasePath));

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Scopes_a_weapon_to_its_own_clips_not_the_whole_container()
    {
        Assert.True(File.Exists(BasePath), Fc2Corpus.MissingMessage("movemgr.bin"));
        MoveFile file = Load();

        IReadOnlyList<MoveClip> clips = MoveWeapons.ClipsFor(file, DartRifle);
        int total = MoveWeapons.AllClipReferences(file).Count;

        Assert.Equal(52, clips.Count);

        // The over-reach bug reached 1,761 of the graph's clips for this weapon.
        Assert.True(
            clips.Count < total / 20,
            $"weapon {DartRifle} claims {clips.Count} of {total} clips, which is container-sized");
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Flags_the_clips_another_weapon_also_plays()
    {
        Assert.True(File.Exists(BasePath), Fc2Corpus.MissingMessage("movemgr.bin"));
        IReadOnlyList<MoveClip> clips = MoveWeapons.ClipsFor(Load(), DartRifle);

        Assert.Equal(49, clips.Count(c => c.IsExclusive));

        // Borrowed from the AK-47's folder, and played by sixteen other weapons.
        MoveClip jam = clips.Single(c => c.Hash == 0x0E1937D3);
        Assert.Contains(2, jam.PlayedBy);
        Assert.Equal(17, jam.PlayedBy.Count);

        // In the Dart Rifle's *own* folder, yet the MGL-140 plays it too - which is why a
        // folder-name heuristic is unsafe for repointing, not merely incomplete.
        MoveClip draw = clips.Single(c => c.Hash == 0xB4B65546);
        Assert.Equal([DartRifle, 40], draw.PlayedBy);
    }

    /// <summary>
    /// A draw or holster branch is gated on DesiredWeapon, not EquippedWeapon, so a rule reading
    /// only channel 17 under-reports most weapons by the clips they play while being switched to.
    /// The AK-47 loses seven of its 63 clips to that blind spot.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Counts_clips_pinned_by_DesiredWeapon_too()
    {
        Assert.True(File.Exists(BasePath), Fc2Corpus.MissingMessage("movemgr.bin"));
        MoveFile file = Load();

        Assert.Equal(63, MoveWeapons.ClipsFor(file, 2).Count);

        // Three of the five sites playing the Dart Rifle's draw clip are pinned by channel 18.
        MoveObject pinned = file.Objects.Single(o => o.Index == 16787);
        Assert.Equal(39, MoveWeapons.WeaponOf(
            file.Objects.Single(o => o.Ops.Any(op => op.Target == pinned))));
    }

    /// <summary>
    /// The Dart Rifle's draw clip is reached from five sites: three the Dart Rifle governs and two
    /// the MGL-140 does. Retargeting it for one weapon must leave the other's sites alone - a
    /// rewrite by hash across the whole graph is what silently changes how the MGL-140 draws.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Retargets_only_the_sites_the_weapon_governs()
    {
        Assert.True(File.Exists(BasePath), Fc2Corpus.MissingMessage("movemgr.bin"));
        MoveFile file = Load();

        MoveRepointResult result = MoveRepoint.Apply(
            file, DartRifle, new Dictionary<uint, uint> { [0xB4B65546] = 0xDEADBEEF });

        Assert.Equal(3, result.Rewritten);
        Assert.Equal(2, result.OtherWeapon);
        Assert.Equal(0, result.Ungoverned);
        Assert.True(result.IsComplete);
        Assert.Empty(result.Unreferenced);

        // The MGL-140 still plays the original through the two sites it governs.
        Assert.Contains(0xB4B65546u, MoveWeapons.ClipsFor(file, 40).Select(c => c.Hash));
        Assert.Equal(
            file.Objects.Count, MoveCodec.Load(MoveCodec.Save(file)).Objects.Count);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Reports_a_mapped_clip_the_graph_never_names()
    {
        Assert.True(File.Exists(BasePath), Fc2Corpus.MissingMessage("movemgr.bin"));

        MoveRepointResult result = MoveRepoint.Apply(
            Load(), DartRifle, new Dictionary<uint, uint> { [0x1234_5678] = 0xDEADBEEF });

        Assert.Equal(0, result.Rewritten);
        Assert.Equal([0x1234_5678u], result.Unreferenced);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Finds_every_weapon_index_that_scopes_clips()
    {
        Assert.True(File.Exists(BasePath), Fc2Corpus.MissingMessage("movemgr.bin"));
        IReadOnlyList<int> indices = MoveWeapons.Indices(Load());

        Assert.Contains(DartRifle, indices);
        Assert.Contains(2, indices);
        Assert.DoesNotContain(44, indices);
    }
}
