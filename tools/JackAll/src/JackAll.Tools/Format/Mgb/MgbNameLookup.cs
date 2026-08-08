namespace JackAll.Tools.Format.Mgb;

/// <summary>
/// Turns the CRC32 name hashes that fill a <c>.mgb</c> back into readable names.
/// </summary>
/// <remarks>
/// Every object in this format is identified by <c>CRC32(name)</c> rather than the name itself -
/// <c>NamedObject</c> ids, <c>FullLink</c> targets, <c>AreaLink</c> references, page tags and
/// <c>UserData</c> property keys are all hashes. Without a reverse lookup the tree is a wall of hex
/// and the editor is unusable.
///
/// The package seeds most of its own dictionary: an <c>AreaInstance</c>'s <c>LABEL</c> is the name
/// of the area it points at, so hashing the labels a file already contains resolves the areas in
/// that same file. That plus the known class names covers the common cases with no external data.
/// Anything still unresolved shows as <c>#XXXXXXXX</c> rather than a guess.
///
/// Nothing here is ever trusted on its own: <see cref="MgbXml"/> writes a resolved name only after
/// re-hashing it and confirming the result matches, so a name that reached the dictionary by a
/// coincidence cannot survive into an exported document.
/// </remarks>
public sealed class MgbNameLookup
{
    private readonly Dictionary<uint, string> _names = [];

    public static MgbNameLookup For(MgbPackage package)
    {
        var lookup = new MgbNameLookup();
        foreach (string className in MgbTypeTable.KnownClassNames)
        {
            lookup.Offer(className);
        }
        lookup.Seed(package);
        return lookup;
    }

    /// <summary>Adds <paramref name="candidate"/> if its hash isn't already spoken for.</summary>
    public void Offer(string candidate)
    {
        if (candidate.Length == 0)
        {
            return;
        }
        _names.TryAdd(MgbTypeTable.Hash(candidate), candidate);
    }

    /// <summary>The name for a hash, or a <c>#</c>-prefixed hex form when it is unknown.</summary>
    public string Describe(uint hash) =>
        _names.TryGetValue(hash, out string? name) ? name : $"#{hash:X8}";

    public string? Resolve(uint hash) => _names.GetValueOrDefault(hash);

    private void Seed(MgbPackage package)
    {
        foreach (MgbMaterial material in package.Materials)
        {
            OfferPathParts(material.TexturePath);
        }
        foreach (MgbFontFamily family in package.FontFamilies)
        {
            OfferPathParts(MgbText.Ansi(family.FontName));
        }
        foreach (MgbFontSubst subst in package.FontSubsts)
        {
            Offer(MgbText.Ansi(subst.SubstName));
        }
        foreach (MgbFontRef reference in package.FontRefs)
        {
            Offer(MgbText.Ansi(reference.Name));
        }
        foreach (MgbArea area in package.Areas)
        {
            SeedElements(area.Elements);
        }
    }

    private void SeedElements(List<MgbElement> elements)
    {
        foreach (MgbElement element in elements)
        {
            switch (element.Widget)
            {
                // An instance's LABEL is the name of the area it references, so it hashes to that
                // area's own NameId - the single most productive source in a typical file.
                case MgbAreaInstance instance when instance.Label.Length > 0:
                    Offer(instance.LabelText);
                    break;
                case MgbTextBase { UseStringTable: false } text when text.String.Length > 0:
                    Offer(text.Text);
                    break;
            }
        }
    }

    private void OfferPathParts(string path)
    {
        if (path.Length == 0)
        {
            return;
        }
        Offer(path);
        int slash = path.LastIndexOfAny(['\\', '/']);
        string leaf = slash >= 0 ? path[(slash + 1)..] : path;
        Offer(leaf);
        int dot = leaf.LastIndexOf('.');
        if (dot > 0)
        {
            Offer(leaf[..dot]);
        }
    }
}
