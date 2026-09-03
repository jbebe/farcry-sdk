using System.Xml.Linq;
using JackAll.Core.Naming;

namespace JackAll.Core.Format;

/// <summary>
/// The editable form of a `depload.dat`: one element per parent, its dependencies nested inside,
/// each tagged with its resource class.
/// </summary>
/// <remarks>
/// Shaped after the `_depload.xml` twins the game ships beside its binaries, so a decoded file reads
/// the same way the originals do. It differs from them in three places the binary forces:
///
///   - A parent is always &lt;Resource&gt;, because the format stores no class for a parent - only
///     children carry a type. Resolving one from elsewhere in the same file works for ~60% of them
///     and would make a parent's tag depend on unrelated entries, which fragment ids cannot afford.
///   - Parents are written in CRC order, matching the binary's own parents array, so two versions of
///     a file diff cleanly.
///   - `childIndex` carries the block order, which the twins have no way to express.
///
/// `crc_ID` is what the file stores and what wins; `ID` is the resolved path, decoration on the way
/// out and a way to name a new entry without hashing it by hand on the way in.
/// </remarks>
public static class DepLoadXml
{
    private const string ParentElement = "Resource";
    private const string IdAttribute = "ID";
    private const string HashAttribute = "crc_ID";
    private const string TypeAttribute = "crc_Type";
    private const string BlockAttribute = "childIndex";

    /// <summary>Block order is assigned by whoever places the parent, never read off one element.</summary>
    private const int ChildIndexPlaceholder = 0;

    public static string Decode(byte[] content, NameDatabase? names = null)
        => ToXml(DepLoadDocument.Decode(content), names);

    public static byte[] Encode(string xml) => DepLoadDocument.Encode(FromXml(xml));

    public static string ToXml(DepLoadFile file, NameDatabase? names = null)
    {
        var root = new XElement("depload");
        foreach (DepLoadParent parent in file.Parents.OrderBy(p => p.Hash))
        {
            XElement element = ParentToElement(parent, names);
            element.SetAttributeValue(BlockAttribute, parent.ChildIndex);
            root.Add(element);
        }

        return Render(root);
    }

    /// <summary>
    /// One parent as a standalone document - the unit a mod stages to override or add a single
    /// dependency list.
    /// </summary>
    /// <remarks>
    /// A fragment deliberately carries no <c>childIndex</c>. That is a whole-file layout detail which
    /// shifts whenever anything earlier in the file changes, so including it would make every
    /// fragment churn against edits that have nothing to do with it. Splicing keeps the block order
    /// the container already had, or appends for a parent it did not.
    /// </remarks>
    public static string FragmentToXml(DepLoadParent parent, NameDatabase? names = null)
        => Render(ParentToElement(parent, names));

    /// <summary>Reads back what <see cref="FragmentToXml"/> wrote. The block order is the caller's to
    /// assign, so the result carries a placeholder.</summary>
    public static DepLoadParent FragmentFromXml(string xml)
        => ToParent(XDocument.Parse(xml).Root
            ?? throw new InvalidDataException("The depload fragment is empty."));

    private static XElement ParentToElement(DepLoadParent parent, NameDatabase? names)
    {
        var element = new XElement(ParentElement);
        Name(element, parent.Hash, names);

        foreach (DepLoadChild child in parent.Children)
        {
            string? className = DepLoadTypes.NameOf(child.TypeHash);
            var childElement = new XElement(className ?? ParentElement);
            Name(childElement, child.Hash, names);
            if (className is null)
            {
                childElement.SetAttributeValue(TypeAttribute, child.TypeHash);
            }
            element.Add(childElement);
        }

        return element;
    }

    private static DepLoadParent ToParent(XElement element)
    {
        var children = new List<DepLoadChild>();
        foreach (XElement childElement in element.Elements())
        {
            children.Add(new DepLoadChild(HashOf(childElement), TypeOf(childElement)));
        }

        return new DepLoadParent(HashOf(element), ChildIndexPlaceholder, children);
    }

    /// <summary>Renders a document the way the shipped twins are written: tab-indented.</summary>
    private static string Render(XElement root) => FragmentXml.Render(root, "\t");

    public static DepLoadFile FromXml(string xml)
    {
        XElement root = XDocument.Parse(xml).Root
            ?? throw new InvalidDataException("The depload document is empty.");

        var parents = new List<DepLoadParent>();
        int nextBlock = 0;
        foreach (XElement element in root.Elements())
        {
            DepLoadParent parent = ToParent(element);
            int block = (int?)element.Attribute(BlockAttribute) ?? nextBlock;
            nextBlock = Math.Max(nextBlock, block) + 1;
            parents.Add(parent with { ChildIndex = block });
        }

        return new DepLoadFile(parents);
    }

    /// <summary>
    /// Labels an entry with its path, but only one that hashes back to the entry - a hashlist row
    /// whose name disagrees with its key would otherwise write an <c>ID</c> that
    /// <see cref="HashOf"/> rejects on the way back in.
    /// </summary>
    private static void Name(XElement element, uint hash, NameDatabase? names)
    {
        if (names is not null && names.TryResolve(hash, out string path) && NameHash.Compute(path) == hash)
        {
            element.SetAttributeValue(IdAttribute, path);
        }
        element.SetAttributeValue(HashAttribute, hash);
    }

    /// <summary>
    /// A resource's CRC32: <c>crc_ID</c> when present, otherwise hashed from <c>ID</c> so a new entry
    /// can be written as a plain path. Both present and disagreeing is an error rather than a silent
    /// preference, because that is what a mistyped path looks like.
    /// </summary>
    private static uint HashOf(XElement element)
    {
        string? id = (string?)element.Attribute(IdAttribute);
        uint? stored = (uint?)element.Attribute(HashAttribute);

        if (id is null)
        {
            return stored ?? throw new InvalidDataException(
                $"<{element.Name.LocalName}> has neither {IdAttribute} nor {HashAttribute}, so it names nothing.");
        }

        uint hashed = NameHash.Compute(id);
        if (stored is not null && stored != hashed)
        {
            throw new InvalidDataException(
                $"<{element.Name.LocalName} {IdAttribute}=\"{id}\"> hashes to {hashed}, but its " +
                $"{HashAttribute} says {stored}. Drop {HashAttribute} to accept the path, or fix the path.");
        }
        return hashed;
    }

    /// <summary>A child's type hash, from its class-name tag or from an explicit <c>crc_Type</c>.</summary>
    private static uint TypeOf(XElement element)
    {
        string className = element.Name.LocalName;
        if (className != ParentElement)
        {
            return DepLoadTypes.Hash(className);
        }

        return (uint?)element.Attribute(TypeAttribute) ?? throw new InvalidDataException(
            $"<{ParentElement}> needs a {TypeAttribute}, since its tag names no resource class. "
            + "Tag it with a class name instead, e.g. <CAnimationResource>.");
    }
}
