namespace JackAll.Core.Xrefs;

/// <summary>
/// Tells a game-relative path apart from any other string an extractor runs into.
/// </summary>
/// <remarks>
/// This gate is the whole difference between a useful index and a garbage one. The `.fcb` corpus has
/// ~600 distinct <c>String</c>-typed members and most of them are *not* paths - display names, signal
/// names, bone tags, enum-ish category words. Hashing those as if they were paths would add hundreds
/// of thousands of edges pointing at hashes no file will ever have.
///
/// The rule is deliberately extension-driven rather than separator-driven: a real reference always
/// names a concrete asset, and the game only ever ships the 43 extensions listed in
/// <see cref="KnownExtensions"/> (counted directly across all 182,699 entries of the shipped
/// filelist, <c>tools/JackAll/assets/fc2.hashlist</c>). A separator-only rule would let
/// "<c>Bad\Signal\Name</c>" through, and an "anything with a dot" rule would let every float-looking
/// or versioned string through.
/// </remarks>
public static class ReferencePaths
{
    /// <summary>
    /// Every file extension that appears in Far Cry 2's own filelist. Counted from
    /// <c>fc2.hashlist</c>'s 182,699 entries, so this is the complete set for shipped data rather
    /// than a hand-picked sample - anything outside it isn't a path the engine could resolve.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "fcb", "xbt", "sdat", "zsr", "srl", "pso", "spk", "nvm", "mab", "sbao", "vso", "xbg", "hkx",
        "xbm", "lfe", "bank", "pfe", "mgb", "desc", "lua", "xml", "ambx", "rs", "fx", "rtx",
        "skeleton", "apm", "rml", "lfa", "dat", "console", "bin", "bik", "mft", "mask", "txt",
        "root", "raw", "pub", "nomad", "ini", "bao", "banklist",
    };

    /// <summary>The longest extension in <see cref="KnownExtensions"/> ("banklist"/"skeleton"), so
    /// the scan below knows how far back from the end a dot can still start a real one.</summary>
    private const int MaxExtensionLength = 8;

    /// <summary>
    /// Whether <paramref name="value"/> is plausibly a game-relative asset path: printable ASCII, a
    /// sane length, and ending in one of <see cref="KnownExtensions"/>.
    /// </summary>
    public static bool LooksLikeGamePath(string? value)
    {
        // 260 is the engine's own path ceiling; anything past it can't be a real entry, and the
        // lower bound just skips "a.fx"-length strings that are far more likely to be coincidence.
        if (value is not { Length: >= 5 and <= 260 })
        {
            return false;
        }

        int dot = value.LastIndexOf('.');
        if (dot <= 0 || dot == value.Length - 1 || value.Length - dot - 1 > MaxExtensionLength)
        {
            return false;
        }

        // A path with a separator is already strong evidence; a bare "foo.xbt" is accepted too
        // (plenty of members hold a filename relative to an implied folder), which is precisely why
        // the extension check below has to be exact rather than "looks extension-ish".
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c < 0x20 || c > 0x7E)
            {
                return false;
            }
        }

        return KnownExtensions.Contains(value[(dot + 1)..]);
    }
}
