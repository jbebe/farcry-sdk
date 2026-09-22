using JackAll.Tools.Mab;
using JackAll.Tools.Skeleton;

namespace JackAll.Tools.Fc2Model;

/// <summary>The rigs a bank drives: its own, and one per prop it names, by the prop's name.</summary>
public sealed record BankRigs(SkeletonFile? Owner, Dictionary<string, SkeletonFile> Participants);

/// <summary>
/// Finds the animation banks that move a given model.
/// </summary>
/// <remarks>
/// Nothing in a mesh names its animation, and the folders do not line up either: the ak47's banks
/// sit under <c>animations/weapons/primary/ak47</c>, but the spas12's sit under
/// <c>franchi_spas12</c> and the m16's under <c>m-16</c>. Mirroring the model's own folder finds the
/// banks for only 12 of the 49 shipped weapon folders, so it is not a rule.
/// <para>
/// What is a rule is the other direction: a bank's tag records name each thing it animates besides
/// the skeleton, by the model's file stem. Asking the banks is exact, and it finds more than the
/// folder would - 94 banks name <c>ak47</c> against the 62 filed beside it, the rest being
/// locomotion banks that carry the rifle while the character runs.
/// </para>
/// </remarks>
public static class ClipSearch
{
    /// <summary>
    /// Which of these banks name this model, in the order they were given.
    /// </summary>
    /// <remarks>
    /// A bank that cannot be parsed is passed over rather than failing the search: this runs over
    /// every bank in an install to answer a question about one model, so one bad file should not
    /// cost the answer.
    /// </remarks>
    public static List<string> For(
        string modelPath, IEnumerable<string> bankPaths, Func<string, byte[]?> read)
    {
        string model = Stem(modelPath);
        List<string> found = [];
        foreach (string path in bankPaths)
        {
            if (read(path) is not { } bytes)
            {
                continue;
            }

            try
            {
                if (MabFile.Parse(bytes).Participants()
                    .Any(participant => participant.Name.Equals(model, StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add(path);
                }
            }
            catch (InvalidDataException)
            {
                continue;
            }
        }
        return found;
    }

    /// <summary>
    /// The rig a bank's own clip plays on, and the rig of each prop its tag table names.
    /// </summary>
    /// <remarks>
    /// Every shipped bank sits under some folder's <c>animations</c> tree, and that folder holds
    /// its rig: <c>characters\_common</c> for the human banks, an animal's own folder for the rest.
    /// Where a folder holds several, <c>pelvis_ref</c> is the one every human shares. A prop's rig
    /// is named after the prop, <c>ak47_ref.skeleton</c> for <c>ak47</c>.
    /// </remarks>
    public static BankRigs RigsFor(
        string bankPath, MabClip bank, IEnumerable<string> rigPaths, Func<string, byte[]?> read)
    {
        List<(string Path, string Key)> rigs = [.. rigPaths.Select(p => (p, p.Replace('\\', '/').ToLowerInvariant()))];
        SkeletonFile? Load(string? path) => path is not null && read(path) is { } bytes ? SkeletonFile.Parse(bytes) : null;

        string bank_ = bankPath.Replace('\\', '/').ToLowerInvariant();
        int cut = bank_.IndexOf("/animations/", StringComparison.Ordinal);
        string folder = cut < 0 ? "" : bank_[..(cut + 1)];
        List<string> own = [.. rigs
            .Where(r => folder.Length > 0 && r.Key.StartsWith(folder) && r.Key.IndexOf('/', folder.Length) < 0)
            .OrderBy(r => r.Key.EndsWith("/pelvis_ref.skeleton") ? 0 : 1)
            .Select(r => r.Path)];

        Dictionary<string, SkeletonFile> props = new(StringComparer.OrdinalIgnoreCase);
        foreach (MabParticipant participant in bank.Participants().Where(p => p.IsPrimary))
        {
            string suffix = $"/{participant.Name.ToLowerInvariant()}{Fc2ModelBuilder.RigSuffix}";
            if (!props.ContainsKey(participant.Name)
                && Load(rigs.FirstOrDefault(r => r.Key.EndsWith(suffix)).Path) is { } rig)
            {
                props[participant.Name] = rig;
            }
        }
        return new BankRigs(Load(own.FirstOrDefault()), props);
    }

    private static string Stem(string gamePath)
    {
        string normalised = gamePath.Replace('\\', '/');
        int at = normalised.LastIndexOf('/');
        string file = at < 0 ? normalised : normalised[(at + 1)..];
        int dot = file.LastIndexOf('.');
        return dot < 0 ? file : file[..dot];
    }
}
