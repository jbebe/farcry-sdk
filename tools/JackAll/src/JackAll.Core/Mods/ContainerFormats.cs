using JackAll.Core.Format.Fcb;

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
        => segment.EndsWith(FcbSuffix, StringComparison.OrdinalIgnoreCase) || IsDepLoad(segment);

    public static bool IsDepLoad(string fileName)
        => fileName.EndsWith(DepLoadSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The splitter for a container, chosen by the suffix of <paramref name="containerPath"/>.
    /// Falls back to `.fcb`, the only format a hash-addressed override is ever staged as, since such
    /// a path carries no recoverable name to match on.
    /// </summary>
    public static IContainerSplitter For(string containerPath, FcbClassDefinitions definitions)
        => IsDepLoad(containerPath)
            ? DepLoadContainerSplitter.Instance
            : new FcbContainerSplitter(definitions);
}
