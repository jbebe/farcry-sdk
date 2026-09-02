
namespace JackAll.Core.Format;

/// <summary>
/// Registers a resource in a `depload.dat`.
/// </summary>
/// <remarks>
/// The one edit a mod needs. An animation clip is reachable only as a child of the
/// `CAnimationPackageResource` its weapon names, so a clip at a path the game never shipped has to be
/// added to that package or it will not play - see docs/docs/file-formats/depload.md.
///
/// Nothing here maintains an index by hand: <see cref="DepLoadDocument.Encode"/> re-derives the sort
/// order, every child slice and the type table, so an edit only has to say what belongs where.
/// </remarks>
public static class DepLoadEdit
{
    /// <summary>
    /// Adds <paramref name="childHash"/> to <paramref name="parentHash"/>'s dependencies, creating the
    /// parent if the file has none. A hash already listed under that parent is left alone, so building
    /// a mod twice does not list it twice.
    /// </summary>
    public static DepLoadFile AddChild(DepLoadFile file, uint parentHash, uint childHash, uint typeHash)
    {
        var child = new DepLoadChild(childHash, typeHash);
        var parents = new List<DepLoadParent>(file.Parents.Count + 1);
        bool found = false;

        foreach (DepLoadParent parent in file.Parents)
        {
            if (parent.Hash != parentHash)
            {
                parents.Add(parent);
                continue;
            }

            found = true;
            parents.Add(parent.Children.Any(existing => existing.Hash == childHash)
                ? parent
                : parent with { Children = [.. parent.Children, child] });
        }

        if (!found)
        {
            parents.Add(new DepLoadParent(parentHash, EndOfBlocks(file.Parents), [child]));
        }

        return new DepLoadFile(parents);
    }

    /// <summary>A block order past every existing one, so a new parent's children land at the end.</summary>
    internal static int EndOfBlocks(IReadOnlyList<DepLoadParent> parents)
        => parents.Count == 0 ? 0 : parents.Max(p => p.ChildIndex) + 1;
}
