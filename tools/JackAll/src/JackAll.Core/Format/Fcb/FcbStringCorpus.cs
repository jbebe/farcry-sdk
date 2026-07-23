namespace JackAll.Core.Format.Fcb;

/// <summary>
/// A hash -> string dictionary harvested from real <see cref="FcbMemberType.String"/>-typed values in
/// one or more already-resolved <c>.fcb</c> files (entitylibrary, by default) - built to reverse
/// Hash-typed references found elsewhere (a savegame's PersistenceDB tree, most notably) that only
/// ever store the CRC32 of some name a sibling, fully class-resolved <c>.fcb</c> still spells out in
/// the clear. See reverse/dunia/savegame_format.md's "binary_classes.xml DOES resolve some savegame
/// content" section for why this is a distinct axis from resolving the save's own type/member hashes
/// directly against <c>binary_classes.xml</c>: a save's hashes are of *values* (entity names, archetype
/// ids, plan names, ...), not of the field names <c>binary_classes.xml</c> catalogs.
/// </summary>
/// <remarks>
/// Deliberately keyed only on values whose member type is genuinely declared <c>String</c> for their
/// resolved class (via <see cref="FcbClass.FindMember"/>) rather than guessing from byte shape - unlike
/// the savegame's own values (which never resolve to a known class at all, see
/// <c>SaveGameFieldCatalog</c>), entitylibrary-style files really do have a known class layout here, so
/// there's no need for the printable-ASCII heuristic that layer uses on genuinely untyped bytes.
/// </remarks>
public sealed class FcbStringCorpus
{
    private readonly Dictionary<uint, string> _byHash = [];
    private readonly HashSet<uint> _ambiguous = [];

    /// <summary>Hash → first string seen for it. A hash present in <see cref="Ambiguous"/> mapped to
    /// more than one distinct string; this dictionary still keeps whichever was seen first, since a
    /// caller doing best-effort display work is better served by *a* plausible name than none.</summary>
    public IReadOnlyDictionary<uint, string> ByHash => _byHash;

    /// <summary>Hashes for which two or more distinct strings were harvested - a genuine CRC32
    /// collision within the corpus, not a parsing artifact. Callers that need certainty rather than a
    /// best guess should treat these as unresolved.</summary>
    public IReadOnlySet<uint> Ambiguous => _ambiguous;

    public int FilesLoaded { get; private set; }

    /// <summary>
    /// Builds a corpus from every <c>.fcb</c> file found under <paramref name="directory"/>
    /// (recursively), resolving each one's classes against <paramref name="definitions"/>. A file that
    /// isn't a valid <c>.fcb</c> (wrong magic/version/flags) is skipped rather than aborting the whole
    /// harvest - the directory this is pointed at is expected to hold nothing but real game archives,
    /// but this keeps the tool useful even if that's not quite true.
    /// </summary>
    public static FcbStringCorpus BuildFromDirectory(
        string directory, FcbClassDefinitions definitions, string searchPattern = "*.fcb")
    {
        var corpus = new FcbStringCorpus();
        foreach (string file in Directory.EnumerateFiles(directory, searchPattern, SearchOption.AllDirectories))
        {
            FcbObject root;
            try
            {
                root = FcbDocument.Deserialize(File.ReadAllBytes(file));
            }
            catch (InvalidDataException)
            {
                continue;
            }

            corpus.AddTree(root, definitions);
            corpus.FilesLoaded++;
        }
        return corpus;
    }

    /// <summary>Harvests one already-parsed tree, for a caller that wants to build a corpus from
    /// something other than a plain directory scan (e.g. files pulled out of the game's VFS).</summary>
    public void AddTree(FcbObject root, IFcbClassScope scope) => Walk(root, scope);

    private void Walk(FcbObject obj, IFcbClassScope scope)
    {
        FcbClass ownClass = scope.Resolve(obj.TypeHash);

        foreach ((uint nameHash, byte[] value) in obj.Values)
        {
            if (ownClass.FindMember(nameHash)?.Type != FcbMemberType.String)
            {
                continue;
            }

            if (FcbValueCodec.TryDecode(FcbMemberType.String, value, out object decoded)
                && decoded is string { Length: > 0 } text)
            {
                Add(text);
            }
        }

        foreach (FcbObject child in obj.Children)
        {
            Walk(child, ownClass);
        }
    }

    private void Add(string text)
    {
        uint hash = FcbClassDefinitions.Crc32Ascii(text);
        if (_byHash.TryGetValue(hash, out string? existing))
        {
            if (existing != text)
            {
                _ambiguous.Add(hash);
            }
            return;
        }

        _byHash[hash] = text;
    }
}
