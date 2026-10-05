using System.Globalization;
using System.Text;
using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Rml;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.Core.Legacy;

/// <summary>
/// The files of an imported legacy layer, each compared with its base-game counterpart: as changes
/// for an analysis, or rebuilt with only some of them for a pick. One resolution of what the two
/// sides of a unit are serves both, so a pick can only ever apply what the analysis listed.
/// </summary>
/// <remarks>
/// A unit is a staged file under the layer's <c>mods\</c> folder: a whole archive file or one
/// fragment of a splitting container. Its address is that path with forward slashes.
/// </remarks>
public sealed class LegacyUnits(string layerRoot, GameVfs vfs, NameDatabase names, FcbClassDefinitions definitions)
{
    private readonly Dictionary<uint, IContainerTree?> _containers = [];

    private string ModsRoot => Path.Combine(layerRoot, "mods");

    /// <summary>Every unit the layer stages, as addresses.</summary>
    public IEnumerable<string> All()
        => Directory.Exists(ModsRoot)
            ? Directory.EnumerateFiles(ModsRoot, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(ModsRoot, file).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
            : [];

    /// <summary>Every change in one unit. Each is marked whole when applying them all would not
    /// rebuild the mod's version, so only taking the unit outright is faithful.</summary>
    public List<LegacyChange> Compare(string unit)
    {
        switch (Resolve(unit))
        {
            case StringSides strings:
                return [.. strings.Edits
                    .Select(edit => (edit, before: strings.Vanilla(edit)))
                    .Where(e => e.before != e.edit.Value)
                    .Select(e => new LegacyChange(ChangeKind.Field, unit, $"{unit}#{e.edit.Section}/{e.edit.Key}",
                        e.before, e.edit.Value))];

            case XmlSides xml:
                (List<XmlChange> changes, bool exact) = XmlTreeDiff.Diff(xml.Vanilla, xml.Mod, xml.Expand);
                string hint = NameOf(xml.Mod);
                return [.. changes
                    .Where(c => !IsRounding(c))
                    .Select(c => new LegacyChange(c.Kind, unit, $"{unit}#{c.Path}", c.Old, c.New, !exact, hint))];

            case TextSides text:
                string? header = HeaderOf(text.Mod);
                return [.. TextHunks.Diff(text.Vanilla, text.Mod)
                    .Select(h => new LegacyChange(ChangeKind.Text, unit, $"{unit}@L{h.Line}", h.Old, h.New, Hint: header))];

            case OpaqueSides { Vanilla: null } added:
                return [new LegacyChange(ChangeKind.New, unit, unit, null, $"{added.Mod.Length} bytes", true, added.Hint)];

            case OpaqueSides changed:
                return [new LegacyChange(ChangeKind.File, unit, unit,
                    $"{changed.Vanilla!.Length} bytes", $"{changed.Mod.Length} bytes", true, changed.Hint)];

            default:
                throw new InvalidOperationException(unit);
        }
    }

    /// <summary>The unit's base-game version with only the changes <paramref name="take"/> accepts,
    /// by address, in the form a layer stages it.</summary>
    public byte[] Merge(string unit, Func<string, bool> take)
        => Resolve(unit) switch
        {
            StringSides strings => Encoding.UTF8.GetBytes(OasisStringsPatch.Render(
                strings.Edits.Where(edit => take($"{unit}#{edit.Section}/{edit.Key}")))),
            XmlSides xml => xml.Render(XmlTreeDiff.Merge(xml.Vanilla, xml.Mod, path => take($"{unit}#{path}"), xml.Expand)),
            TextSides text => text.Encode(TextHunks.Merge(text.Vanilla, text.Mod, line => take($"{unit}@L{line}"))),
            OpaqueSides opaque => opaque.Mod,
            _ => throw new InvalidOperationException(unit),
        };

    /// <summary>The mod's own version of the unit, as staged.</summary>
    public byte[] Staged(string unit) => File.ReadAllBytes(PathOf(unit));

    private string PathOf(string unit) => Path.Combine(ModsRoot, unit.Replace('/', Path.DirectorySeparatorChar));

    private abstract record Sides;

    private sealed record StringSides(IReadOnlyList<OasisStringEdit> Edits, Func<OasisStringEdit, string?> Vanilla) : Sides;

    private sealed record XmlSides(
        XElement Vanilla, XElement Mod, Func<XElement, byte[]> Render, Func<XElement, XElement?>? Expand = null) : Sides;

    private sealed record TextSides(string Vanilla, string Mod, Func<string, byte[]> Encode) : Sides;

    private sealed record OpaqueSides(byte[]? Vanilla, byte[] Mod, string? Hint) : Sides;

    private Sides Resolve(string unit)
    {
        string path = unit.Replace('/', '\\');
        byte[] mod = Staged(unit);
        string leaf = Path.GetFileName(path);

        if (OasisStringsPatch.IsPatchDocument(leaf))
        {
            IContainerTree? table = Container(NameHash.Compute(OasisStringsPatch.TablePathOf(path)), OasisStringsPatch.TablePathOf(path));
            return new StringSides(
                OasisStringsPatch.Parse(Encoding.UTF8.GetString(mod)),
                edit => table?.Extract(StringTableContainerSplitter.IdOf(edit)) is { } xml
                    ? OasisStringsPatch.FragmentFromXml(xml).Value
                    : null);
        }

        if (ContainerFormats.ContainerPathOf(path) is { } containerPath)
        {
            string fragmentId = path[(containerPath.Length + 1)..];
            XElement modXml = XElement.Parse(Encoding.UTF8.GetString(mod));
            Func<XElement, byte[]> render = merged => Encoding.UTF8.GetBytes(merged.ToString());

            if (!ContainerFormats.HasComparableOriginal(containerPath, fragmentId))
            {
                return Layout(containerPath, modXml);
            }

            return Container(HashOf(containerPath), containerPath)?.Extract(fragmentId) is { } vanillaXml
                ? new XmlSides(XElement.Parse(vanillaXml), modXml, render, NestedRml)
                : new OpaqueSides(null, mod, NameOf(modXml));
        }

        byte[]? vanilla = vfs.ReadOriginal(HashOf(path));
        if (vanilla is null)
        {
            return new OpaqueSides(null, mod, IsText(mod) ? HeaderOf(Latin(mod).Text) : null);
        }

        if (RmlDocument.TryDeserialize(vanilla, out XElement? vanillaRml) && RmlDocument.TryDeserialize(mod, out XElement? modRml))
        {
            return new XmlSides(vanillaRml, modRml, RmlDocument.Serialize);
        }

        if (IsText(vanilla) && IsText(mod))
        {
            (string before, byte[] bom) = Latin(vanilla);
            (string after, _) = Latin(mod);
            if (TryParseXml(before) is { } vanillaDoc && TryParseXml(after) is { Root: { } modRoot })
            {
                return new XmlSides(vanillaDoc.Root!, modRoot, merged => [.. bom, .. Encoding.Latin1.GetBytes(
                    (vanillaDoc.Declaration is { } declaration ? declaration + Environment.NewLine : string.Empty) + merged)]);
            }

            return new TextSides(before, after, merged => [.. bom, .. Encoding.Latin1.GetBytes(merged)]);
        }

        return new OpaqueSides(vanilla, mod, null);
    }

    /// <summary>
    /// A sector's layout states only what the mod moved. Its base declares the mod's populated layers
    /// with no entities in them, so every entity placed, layer removed and fragment deleted is a change
    /// of its own; an entity is named after its fragment, since a layer often mixes several features.
    /// A layer left with no entities by a pick is dropped rather than declared empty.
    /// </summary>
    private XmlSides Layout(string containerPath, XElement mod)
    {
        Dictionary<string, string> names = EntityNames(containerPath);
        var named = new XElement(mod);
        foreach (XElement entity in named.Descendants("entity"))
        {
            if ((string?)entity.Attribute("id") is { } id && names.TryGetValue(id, out string? name))
            {
                entity.SetAttributeValue("name", name);
            }
        }

        static string KeyOf(XElement layer) => $"{(string?)layer.Attribute("under")}|{(string?)layer.Attribute("path")}";
        List<XElement> populated = [.. named.Elements("layer").Where(l => l.Elements("entity").Any())];
        var populatedKeys = populated.Select(KeyOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var vanilla = new XElement(named.Name, populated.Select(layer =>
            new XElement(layer.Name, layer.Attributes(), layer.Elements().Where(child => child.Name != "entity"))));

        return new XmlSides(vanilla, named, merged =>
        {
            merged.Descendants("entity").Attributes("name").Remove();
            merged.Elements("layer")
                .Where(layer => !layer.Elements("entity").Any() && populatedKeys.Contains(KeyOf(layer)))
                .Remove();
            return Encoding.UTF8.GetBytes(merged.ToString());
        });
    }

    /// <summary>Fragment labels by entity id, from the base container and the mod's staged fragments:
    /// a placed entity's fragment is <c>&lt;label&gt;.&lt;entity id&gt;.xml</c>.</summary>
    private Dictionary<string, string> EntityNames(string containerPath)
    {
        IEnumerable<string> ids = Container(HashOf(containerPath), containerPath)?.List().Select(row => row.Id) ?? [];
        string staged = Path.Combine(ModsRoot, containerPath);
        if (Directory.Exists(staged))
        {
            ids = ids.Concat(Directory.EnumerateFiles(staged).Select(Path.GetFileName).OfType<string>());
        }

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            string stem = Path.GetFileNameWithoutExtension(Path.GetFileName(id));
            int dot = stem.LastIndexOf('.');
            if (dot > 0 && ulong.TryParse(stem[(dot + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                names[stem[(dot + 1)..]] = stem[..dot];
            }
        }

        return names;
    }

    private IContainerTree? Container(uint hash, string containerPath)
    {
        if (!_containers.TryGetValue(hash, out IContainerTree? tree))
        {
            try
            {
                tree = vfs.ReadOriginal(hash) is { } bytes
                    ? ContainerFormats.For(containerPath, definitions, names).Open(bytes)
                    : null;
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
            {
                tree = null;
            }

            _containers[hash] = tree;
        }

        return tree;
    }

    /// <summary>The archive hash a staged path names: spelled out under <c>_hash\</c>, else the
    /// path's own.</summary>
    private static uint HashOf(string path)
    {
        const string prefix = "_hash\\";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? uint.Parse(Path.GetFileNameWithoutExtension(path[prefix.Length..]), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : NameHash.Compute(path);
    }

    /// <summary>
    /// A float the mod's editor wrote back with fewer digits: the same number to within single
    /// precision. Left out of the changes, so a pick keeps the base game's own value.
    /// </summary>
    private static bool IsRounding(XmlChange change)
        => change.Kind == ChangeKind.Field
           && double.TryParse(change.Old, NumberStyles.Float, CultureInfo.InvariantCulture, out double before)
           && double.TryParse(change.New, NumberStyles.Float, CultureInfo.InvariantCulture, out double after)
           && Math.Abs(before - after) <= 1e-6 * Math.Max(1, Math.Abs(before));

    /// <summary>
    /// The .rml document inside an <c>.fcb</c> Rml value that JackAll's XML keeps as hex because its
    /// encoding does not round-trip byte for byte. A merge that changes it writes it back decoded,
    /// which the fcb encoder re-encodes the base game's way.
    /// </summary>
    private static XElement? NestedRml(XElement leaf)
    {
        if (leaf.Name.LocalName != "value" || (string?)leaf.Attribute("type") != "Rml" || leaf.Value.Length == 0)
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(leaf.Value.Trim());
        }
        catch (FormatException)
        {
            return null;
        }

        return bytes.Length > 1 && bytes[^1] == 0 && RmlDocument.TryDeserialize(bytes[..^1], out XElement? padded) ? padded
            : RmlDocument.TryDeserialize(bytes, out XElement? bare) ? bare
            : null;
    }

    private static bool IsText(byte[] bytes) => bytes.Length > 0 && !bytes.AsSpan().Contains((byte)0);

    /// <summary>Byte-faithful text: Latin-1 maps every byte to one char and back, whatever the file's
    /// real encoding, so a merge reproduces untouched bytes exactly.</summary>
    private static (string Text, byte[] Bom) Latin(byte[] bytes)
    {
        byte[] bom = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? [0xEF, 0xBB, 0xBF] : [];
        return (Encoding.Latin1.GetString(bytes, bom.Length, bytes.Length - bom.Length), bom);
    }

    private static XDocument? TryParseXml(string text)
    {
        if (!text.TrimStart().StartsWith('<'))
        {
            return null;
        }

        try
        {
            return XDocument.Parse(text);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>What a fragment calls itself: its archetype or entity name.</summary>
    private static string NameOf(XElement root)
        => root.DescendantsAndSelf("value")
            .FirstOrDefault(v => (string?)v.Attribute("name") is "Name" or "hidName")?.Value
            ?? XmlTreeDiff.Label(root);

    /// <summary>A script's opening comment, which is often where a mod says what the file is for.</summary>
    private static string? HeaderOf(string text)
    {
        string first = text.TrimStart().Split('\n', 2)[0].Trim();
        return first.StartsWith("--", StringComparison.Ordinal) || first.StartsWith("//", StringComparison.Ordinal)
               || first.StartsWith("<!--", StringComparison.Ordinal)
            ? first[..Math.Min(first.Length, 160)]
            : null;
    }
}
