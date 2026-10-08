namespace JackAll.Core.Mods;

/// <summary>What two layers collided on.</summary>
public enum ConflictKind
{
    /// <summary>Both changed the same part of one fragment differently.</summary>
    Fragment,

    /// <summary>Both shipped a whole file, with different bytes.</summary>
    File,

    /// <summary>A fragment replaced an entry another layer had changed in its whole-file copy of the
    /// container, which happens whatever the load order.</summary>
    Overlaid,
}

/// <summary>
/// One place where the build kept one layer's version and dropped another's. A
/// <see cref="ConflictKind.File"/> names the file as its <see cref="Container"/> and has no fragment.
/// </summary>
public readonly record struct ModConflict(
    string Container, string WinningLayer, IReadOnlyList<string> OverruledLayers,
    ConflictKind Kind = ConflictKind.Fragment, string FragmentId = "", bool IsNewEntry = false)
{
    /// <summary>Where inside the fragment the layers disagreed, as paths of names and types below its
    /// root - <c>Entity/Components/CEventComponent/hidLinks/Link[1]/InputEvent</c>. Empty when the
    /// merge cannot say.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    /// <summary>Where the fragment sits, as one staged path.</summary>
    public string DisplayPath => $"{Container}\\{FragmentId}";

    /// <summary><see cref="Paths"/> as a phrase to follow the fragment's name, empty when there are none.</summary>
    public string AtPaths => Paths.Count == 0 ? "" : $" at {string.Join(", ", Paths)}";

    /// <summary><see cref="Kind"/> as the JSON output spells it.</summary>
    public string KindName => Kind.ToString().ToLowerInvariant();

    /// <summary>The collision and what to do about it, as one sentence for a build log.</summary>
    public string Describe()
    {
        string overruled = string.Join(", ", OverruledLayers);
        return Kind switch
        {
            ConflictKind.File =>
                $"'{WinningLayer}' replaced '{overruled}''s copy of '{Container}' - whole files are not merged, "
                + "so reorder the mods to keep the other copy.",
            ConflictKind.Overlaid =>
                $"'{WinningLayer}' overrode '{overruled}''s own edit of '{DisplayPath}' - a fragment lands on "
                + "top of a whole-file copy of its container, whatever the load order.",
            _ when IsNewEntry =>
                $"'{WinningLayer}' and '{overruled}' both add '{DisplayPath}' with different content{AtPaths}, "
                + "so only the higher-priority mod's survived. Reorder the mods, or hand-merge it in JackAll.App.",
            _ =>
                $"'{WinningLayer}' overrode '{overruled}' inside '{DisplayPath}'{AtPaths} by load order - their edits "
                + "genuinely conflicted, so only the higher-priority mod's change survived. Reorder the mods, "
                + "or hand-merge it in JackAll.App.",
        };
    }
}
