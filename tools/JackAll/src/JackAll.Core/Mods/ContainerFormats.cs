using JackAll.Core.Format.Fcb;
using JackAll.Core.Naming;

namespace JackAll.Core.Mods;

/// <summary>
/// Which path segments name a splitting container, and which splitter handles each.
/// </summary>
/// <remarks>
/// Recognition is static and decoding is not: <see cref="ModPathHashing"/> has to classify a staged
/// path long before any game data is available, while splicing needs the `.fcb` class definitions.
/// Keeping the two apart is what lets path classification stay a pure function.
/// </remarks>
public static class ContainerFormats
{
    /// <summary>
    /// A `depload.dat`'s whole filename ending, not just its extension: a bare `.dat` is also what
    /// the archives themselves are called. The one definition of the name — every other place that
    /// recognises one of these files reads it from here.
    /// </summary>
    public const string DepLoadSuffix = "_depload.dat";

    private const string FcbSuffix = ".fcb";

    public static bool IsContainerSegment(string segment)
        => segment.EndsWith(FcbSuffix, StringComparison.OrdinalIgnoreCase)
           || IsDepLoad(segment)
           || IsMoveGraph(segment)
           || IsStringTable(segment);

    public static bool IsDepLoad(string fileName)
        => fileName.EndsWith(DepLoadSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A MOVE animation graph, named outright rather than by suffix.
    /// </summary>
    /// <remarks>
    /// This function sees one path segment, never the directory, so it cannot key on
    /// <c>graphics\move\</c> the way <c>assets/engine-roots.tsv</c> does - and a bare <c>.bin</c> is
    /// far too broad, since the shipped particle libraries are 24 <c>.bin</c> files totalling 164 MB.
    /// An explicit set is therefore both the exact rule and the honest one. The <c>*named.bin</c>
    /// twins are excluded: they set <c>dwFileFormat &amp; 0x20000</c>, which
    /// <c>CMoveMgr::CreateFromStream</c> rejects, so no engine will ever load one.
    /// </remarks>
    public static bool IsMoveGraph(string fileName) => MoveContainerSplitter.IsMoveGraph(fileName);

    /// <summary>
    /// A localised string table, named outright for the same reason a MOVE graph is.
    /// </summary>
    /// <remarks>
    /// A bare <c>.rml</c> would be far too broad: the shipped archives hold 69 of them, and the 24
    /// <c>*_deploadnewparticles.rml</c> particle libraries among them are a different container with
    /// a different key.
    /// </remarks>
    public static bool IsStringTable(string fileName)
        => fileName.Equals(StringTableContainerSplitter.FileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The splitter for a container, chosen by the suffix of <paramref name="containerPath"/>.
    /// Falls back to `.fcb`, the only format a hash-addressed override is ever staged as, since such
    /// a path carries no recoverable name to match on.
    /// </summary>
    public static IContainerSplitter For(
        string containerPath, FcbClassDefinitions definitions, NameDatabase? names = null)
    {
        if (IsDepLoad(containerPath))
        {
            return names is null
                ? DepLoadContainerSplitter.Instance
                : new DepLoadContainerSplitter(names);
        }

        // The remaining two are matched on their filename, so a container path ending in one is
        // unambiguous. Neither takes a NameDatabase: a MOVE state name is not a game path, so the
        // hashlist cannot resolve one and the splitter reads its own bundled table instead, while a
        // section carries its name in the file.
        string fileName = Path.GetFileName(containerPath);
        if (IsMoveGraph(fileName))
        {
            return MoveSplitter.Value;
        }

        return IsStringTable(fileName)
            ? StringTableContainerSplitter.Instance
            : new FcbContainerSplitter(definitions);
    }

    /// <summary>
    /// The MOVE splitter, with names loaded once. <see cref="BundledAssets"/> walks the filesystem to
    /// find the table, and <see cref="For"/> is called per container during a build.
    /// </summary>
    private static readonly Lazy<MoveContainerSplitter> MoveSplitter = new(() =>
    {
        try
        {
            return new MoveContainerSplitter(BundledAssets.LoadMoveNames());
        }
        catch (IOException)
        {
            // Labels are decoration; the number binds. A missing or unreadable table is not a reason
            // to fail a build.
            return MoveContainerSplitter.Instance;
        }
    });

    /// <summary>The container part of a staged fragment path, or null when it names no fragment -
    /// how a caller outside this assembly tells which format a fragment row belongs to.</summary>
    public static string? ContainerPathOf(string stagedPath) => ModPathHashing.ContainerPathOf(stagedPath);
}
