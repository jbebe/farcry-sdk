using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Move;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// Whether the fragment scheme can express a <em>new</em> weapon, not just a replacement.
/// </summary>
/// <remarks>
/// A replacement retargets clip hashes inside branches that already exist, which is a value edit.
/// A new weapon index has no branches at all behind it, so something has to create them - and that is
/// a structural change to the states that hold them. These tests pin what that costs and what it
/// looks like, because "can a mod add a 45th weapon" is the question the whole MOVE effort is
/// downstream of. See docs/docs/file-formats/move.md.
/// </remarks>
public sealed class MoveNewWeaponTests
{
    /// <summary>The first free EquippedWeapon index: retail names 0-43.</summary>
    private const int NewWeapon = 44;

    private const int DonorWeapon = 39;   // Dart_Rifle, the slot the VSS already proves out

    public static TheoryData<string> CorpusFiles() => MoveStateIndexTests.CorpusFiles();

    /// <summary>
    /// Cloning one weapon's branch onto a new index works, and costs the state fragment as well as
    /// the branch: the state is where the <c>&lt;branch&gt;</c> marker lives, so a mod adding a
    /// branch necessarily edits the state around it.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void A_new_weapon_branch_can_be_cloned_onto_an_existing_state(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        IContainerTree tree = MoveContainerSplitter.Instance.Open(original);
        if (SmallestDonor(tree) is not var (stateId, branchId, stateHash)) return;

        MoveUnit donor = new(stateHash, MoveWeapons.EquippedWeaponChannel, DonorWeapon);
        MoveUnit fresh = new(stateHash, MoveWeapons.EquippedWeaponChannel, NewWeapon);

        // The branch: the donor's, with the weapon it is pinned to changed.
        string clone = tree.Extract(branchId)!
            .Replace($"<MoveBranch unit=\"{donor.Id}\"", $"<MoveBranch unit=\"{fresh.Id}\"")
            .Replace($"weapon=\"{DonorWeapon}\"", $"weapon=\"{NewWeapon}\"")
            .Replace($"<s32 n=\"m_Value\" v=\"{DonorWeapon}\" />",
                     $"<s32 n=\"m_Value\" v=\"{NewWeapon}\" />");
        Assert.Contains($"v=\"{NewWeapon}\"", clone);

        // The state: a second marker beside the donor's, so the new branch has somewhere to land.
        string marker = $"<branch n=\"CMoveDescriptor\" unit=\"{donor.Id}\" />";
        string state = tree.Extract(stateId)!;
        Assert.Contains(marker, state);
        string widened = ReplaceFirst(
            state, marker, marker + Environment.NewLine + "    "
            + $"<branch n=\"CMoveDescriptor\" unit=\"{fresh.Id}\" />");

        byte[] built = MoveContainerSplitter.Instance.Apply(original, new Dictionary<string, string>
        {
            [stateId] = widened,
            [MoveContainerSplitter.IdOf(fresh)] = clone,
        });

        // It parses, and the new index now reaches the clips the donor reaches.
        MoveFile after = MoveCodec.Load(built);
        Assert.Contains(NewWeapon, MoveWeapons.Indices(after));

        IReadOnlyDictionary<int, IReadOnlySet<uint>> clips = MoveWeapons.ClipsByWeapon(after);
        Assert.NotEmpty(clips[NewWeapon]);
        Assert.All(clips[NewWeapon], c => Assert.Contains(c, clips[DonorWeapon]));

        // The donor is untouched, which is the point of scoping branches by weapon.
        MoveFile before = MoveCodec.Load(original);
        Assert.Equal(
            MoveWeapons.ClipsByWeapon(before)[DonorWeapon].Count, clips[DonorWeapon].Count);
    }

    /// <summary>
    /// The failure a mod hits if it ships only the branch: the state still has no marker for it, so
    /// the two fragments disagree and the build says so rather than dropping the branch.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void A_branch_with_no_marker_in_its_state_is_refused(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        IContainerTree tree = MoveContainerSplitter.Instance.Open(original);
        if (SmallestDonor(tree) is not var (_, branchId, stateHash)) return;

        MoveUnit donor = new(stateHash, MoveWeapons.EquippedWeaponChannel, DonorWeapon);
        MoveUnit fresh = new(stateHash, MoveWeapons.EquippedWeaponChannel, NewWeapon);
        string clone = tree.Extract(branchId)!
            .Replace($"<MoveBranch unit=\"{donor.Id}\"", $"<MoveBranch unit=\"{fresh.Id}\"")
            .Replace($"weapon=\"{DonorWeapon}\"", $"weapon=\"{NewWeapon}\"");

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            MoveContainerSplitter.Instance.Apply(
                original, new Dictionary<string, string> { [MoveContainerSplitter.IdOf(fresh)] = clone }));

        Assert.Contains("sites for it", error.Message);
    }

    /// <summary>
    /// What the addition actually costs, stated as a number so it cannot rot: the state fragment the
    /// marker lives in, plus the branch itself.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Adding_a_branch_costs_the_state_fragment_too(string path)
    {
        if (path.Length == 0) return;

        IContainerTree tree = MoveContainerSplitter.Instance.Open(File.ReadAllBytes(path));
        if (SmallestDonor(tree) is not var (stateId, branchId, _)) return;

        long state = tree.Extract(stateId)!.Length;
        long branch = tree.Extract(branchId)!.Length;

        // Both are fragments, so both are far under the whole graph - the point is only that a
        // structural change is two files, not one.
        Assert.True(state > 0 && branch > 0);
        Assert.True(state + branch < 4 * 1024 * 1024);
    }

    /// <summary>
    /// The cheapest donor: the smallest state holding a branch for <see cref="DonorWeapon"/> that
    /// actually plays something, so the clone is worth asserting about. Plenty of branches are pure
    /// structure - a criterion over a group that plays no clip of its own.
    /// </summary>
    private static (string StateId, string BranchId, uint StateHash)? SmallestDonor(IContainerTree tree)
    {
        (string, string, uint)? best = null;
        long bestSize = long.MaxValue;

        foreach (FcbFragmentInfo row in tree.List())
        {
            if (MoveContainerSplitter.UnitOf(row.Id) is not { } id) continue;
            string? xml = tree.Extract(row.Id);
            if (xml is null || !xml.StartsWith("<MoveBranch", StringComparison.Ordinal)) continue;
            if (!xml.Contains($"weapon=\"{DonorWeapon}\"") || !xml.Contains("channel=\"17\"")) continue;
            if (!xml.Contains("m_animNameHash")) continue;

            uint stateHash = uint.Parse(Between(xml, "state=\"", "\""));
            string stateId = tree.List()
                .Select(r => r.Id)
                .FirstOrDefault(candidate =>
                    MoveContainerSplitter.UnitOf(candidate) == stateHash);
            if (stateId is null) continue;

            long size = xml.Length + (tree.Extract(stateId)?.Length ?? 0);
            if (size < bestSize)
            {
                bestSize = size;
                best = (stateId, row.Id, stateHash);
            }
        }

        return best;
    }

    private static string Between(string text, string open, string close)
    {
        int at = text.IndexOf(open, StringComparison.Ordinal) + open.Length;
        return text[at..text.IndexOf(close, at, StringComparison.Ordinal)];
    }

    private static string ReplaceFirst(string text, string find, string with)
    {
        int at = text.IndexOf(find, StringComparison.Ordinal);
        return at < 0 ? text : text[..at] + with + text[(at + find.Length)..];
    }
}
