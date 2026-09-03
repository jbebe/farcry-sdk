using System.Xml.Linq;

namespace JackAll.Core.Format.Rml;

/// <summary>One string a mod changes, and where in the table it lives.</summary>
public readonly record struct OasisStringEdit(string Section, string Key, string Value);

/// <summary>
/// A mod's localization edits as one sparse document - the whole of what it says about
/// `oasisstrings.rml`, in a file a person can read end to end.
/// </summary>
/// <remarks>
/// The table is one 946 KB file per language that every localization mod has to touch, so the
/// override unit has to be the individual string: anything coarser makes two mods that rename
/// different weapons collide over text neither of them edited. One file per string would be the
/// obvious way to express that and the wrong one - a retranslation is 11,394 strings, and nobody
/// wants to read that as 11,394 files. So the *file* is per mod and the *override* is per string:
/// this document is expanded into one fragment per edit when a layer is scanned, and from there the
/// ordinary fragment machinery merges, attributes and reports conflicts per string.
///
/// A value is an attribute rather than element text so that a newline inside it is written
/// <c>&amp;#xA;</c> and cannot be altered by an editor, a final-newline setting, or git's line-ending
/// conversion. 200 of the shipped strings contain one.
/// </remarks>
public static class OasisStringsPatch
{
    /// <summary>The name a layer stages its edits under, beside where the table itself sits.</summary>
    public const string FileName = "oasisstrings.fragment.xml";

    /// <summary>The table it patches - <see cref="FileName"/> with this in its place.</summary>
    public const string TableFileName = "oasisstrings.rml";

    private const string RootElement = "oasisstrings";
    private const string SectionElement = "section";
    private const string StringElement = "string";
    private const string NameAttribute = "name";
    private const string SectionAttribute = "section";
    private const string KeyAttribute = "enum";
    private const string ValueAttribute = "value";

    /// <summary>Two spaces, matching the `oasisstrings.xml` twin the game ships beside the table.</summary>
    private const string IndentChars = "  ";

    /// <summary>Whether this path's leaf is a patch document.</summary>
    public static bool IsPatchDocument(string fileName)
        => fileName.Equals(FileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The table a patch document at <paramref name="patchPath"/> edits.</summary>
    public static string TablePathOf(string patchPath)
        => string.Concat(patchPath.AsSpan(0, patchPath.Length - FileName.Length), TableFileName);

    /// <summary>The patch document that edits the table at <paramref name="tablePath"/>.</summary>
    public static string PatchPathOf(string tablePath)
        => string.Concat(tablePath.AsSpan(0, tablePath.Length - TableFileName.Length), FileName);

    /// <summary>
    /// Every string in <paramref name="mine"/> that <paramref name="vanilla"/> does not already say -
    /// the edits a patch document has to state, and nothing else.
    /// </summary>
    /// <remarks>
    /// The one definition of "what did this table change", shared by <c>rml fragments</c> and the
    /// legacy importer so a mod converted either way lands on the same document.
    /// </remarks>
    public static IReadOnlyList<OasisStringEdit> Changed(
        IEnumerable<OasisStringEdit> mine, IEnumerable<OasisStringEdit> vanilla)
    {
        var before = new Dictionary<(string, string), string>();
        foreach (OasisStringEdit edit in vanilla)
        {
            before[(edit.Section, edit.Key)] = edit.Value;
        }

        return [.. mine.Where(edit =>
            !before.TryGetValue((edit.Section, edit.Key), out string? was) || was != edit.Value)];
    }

    /// <summary>Every edit the document states, in the order it states them.</summary>
    public static IReadOnlyList<OasisStringEdit> Parse(string xml)
    {
        XElement root = XDocument.Parse(xml).Root
            ?? throw new InvalidDataException("The localization patch is empty.");

        var edits = new List<OasisStringEdit>();
        foreach (XElement section in root.Elements(SectionElement))
        {
            string name = (string?)section.Attribute(NameAttribute) is { Length: > 0 } named
                ? named
                : throw new InvalidDataException(
                    $"A <{SectionElement}> in the localization patch has no {NameAttribute}, so its "
                    + "strings name no place in the table.");

            foreach (XElement entry in section.Elements(StringElement))
            {
                edits.Add(EditOf(entry, name));
            }
        }

        return edits;
    }

    /// <summary>The document a layer stages: every edit, grouped under the section it belongs to.</summary>
    public static string Render(IEnumerable<OasisStringEdit> edits)
        => FragmentXml.Render(
            new XElement(RootElement,
                edits.GroupBy(e => e.Section, StringComparer.Ordinal)
                    .Select(g => new XElement(SectionElement,
                        new XAttribute(NameAttribute, g.Key),
                        g.Select(e => new XElement(StringElement,
                            new XAttribute(KeyAttribute, e.Key),
                            new XAttribute(ValueAttribute, e.Value)))))),
            IndentChars);

    /// <summary>
    /// One edit on its own, the unit the fragment machinery merges and attributes. It carries its
    /// section, because a fragment travels apart from the document that grouped it.
    /// </summary>
    public static string FragmentToXml(OasisStringEdit edit)
        => FragmentXml.Render(
            new XElement(StringElement,
                new XAttribute(SectionAttribute, edit.Section),
                new XAttribute(KeyAttribute, edit.Key),
                new XAttribute(ValueAttribute, edit.Value)),
            IndentChars);

    /// <summary>Reads back what <see cref="FragmentToXml"/> wrote.</summary>
    public static OasisStringEdit FragmentFromXml(string xml)
    {
        XElement element = XDocument.Parse(xml).Root
            ?? throw new InvalidDataException("The localization fragment is empty.");

        return EditOf(element, (string?)element.Attribute(SectionAttribute) is { Length: > 0 } section
            ? section
            : throw new InvalidDataException(
                $"<{element.Name.LocalName}> has no {SectionAttribute}, so it names no place in the table."));
    }

    private static OasisStringEdit EditOf(XElement element, string section)
    {
        if (element.Name.LocalName != StringElement)
        {
            throw new InvalidDataException(
                $"<{element.Name.LocalName}> is not a <{StringElement}>, which is the only thing a "
                + "localization patch may change.");
        }

        string key = (string?)element.Attribute(KeyAttribute) is { Length: > 0 } named
            ? named
            : throw new InvalidDataException(
                $"A <{StringElement}> in section '{section}' has no {KeyAttribute}, so it names no string.");

        return new OasisStringEdit(section, key, (string?)element.Attribute(ValueAttribute) ?? string.Empty);
    }
}
