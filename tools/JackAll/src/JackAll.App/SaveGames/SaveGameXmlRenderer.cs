using System.Xml.Linq;
using JackAll.Core.Format.Fcb;

namespace JackAll.App.SaveGames;

/// <summary>
/// Renders a save's <c>PersistenceDB</c> tree in exactly the shape <see cref="FcbXml.RenderObject"/>
/// renders an ordinary <c>.fcb</c> file - one <c>&lt;object&gt;</c>/<c>&lt;value&gt;</c> tree, a name
/// attribute or a hash attribute (never both), typed content wherever a type is actually known - a
/// single recursive build of one coherent <see cref="XElement"/> tree, not <see cref="FcbXml.RenderObject"/>'s
/// own output with several regex text-patches layered on top afterward.
/// </summary>
/// <remarks>
/// That regex-patch approach (this class's predecessor) had two real problems, both structural, not
/// just cosmetic: (1) a patch that needed to build a multi-child element (a resolved Vector3/Vector4/
/// Matrix4 value) called <see cref="XElement"/>.ToString() on it in isolation and spliced the result into
/// the middle of an already-rendered string - <c>XElement.ToString()</c> always indents starting from
/// column zero, with no way to tell it how deep in the surrounding document it's landing, so the spliced
/// block's indentation never matched its context; (2) every patch was strictly additive by construction
/// (a regex match on an existing <c>hash="..."</c> attribute can only ever append something next to it,
/// never remove it), so a value whose name we'd actually recovered still showed <c>hash="..."</c> right
/// next to a bolted-on <c>known="..."</c>/<c>ref="..."</c>, instead of the base game's own convention of
/// <c>name="..."</c> replacing <c>hash="..."</c> outright once it's known.
///
/// <see cref="FcbXml.RenderObject"/> itself can't just be reused for this: its own class/member
/// resolution (<see cref="FcbClassDefinitions"/>) is exactly right for the ~37%/14% of a save's
/// objects/values that really do reuse a live entitylibrary component class (<c>CIgnitorComponent</c>,
/// <c>CPersistComponent</c>, ...; see reverse/dunia/savegame_format.md), but has no hook for a save's
/// own, disjoint vocabulary (<see cref="SaveGameFieldCatalog"/>'s flat <c>binary_classes.xml</c> lookup,
/// <see cref="SaveGamePersistenceTags"/>, <see cref="SaveGameCompiledFieldNames"/>). So this class re-walks
/// the <see cref="FcbObject"/> tree itself, using the same public class-scope API
/// <see cref="FcbXml.RenderObject"/> does internally (<see cref="FcbClassDefinitions.GetClass"/>/
/// <see cref="FcbClass.Resolve"/>/<see cref="FcbClass.FindMember"/>) so a genuinely-resolvable component
/// still resolves exactly as it would in a normal <c>.fcb</c>, falling through the savegame-specific
/// dictionaries (same priority order the old five-pass pipeline used) only for whatever the entitylibrary
/// scope doesn't know. The one piece actually reused from <see cref="FcbXml"/> is
/// <see cref="FcbXml.TryWriteValue"/> - the byte-to-XML encoding for each known type - so a resolved
/// Float/Vector3/Int64/... value here is byte-for-byte the same shape a real <c>.fcb</c> would show.
///
/// Read-only, like every other class in this folder: this is purely a display-time rendering of
/// whatever a save currently holds, for the Saves tab's sidebar (see <see cref="SaveDetailsViewModel"/>)
/// and as the starting XML an editor tab parses. Writing an edited save back to disk goes through
/// <c>SaveGameDocument.WriteFcbRoot</c> against the tab's own mutated <c>FcbObject</c> tree, never by
/// re-rendering or re-parsing this class's output a second time.
/// </remarks>
internal static class SaveGameXmlRenderer
{
    public static string Render(FcbObject root, FcbClassDefinitions entityDefs, FcbStringCorpus refCorpus)
    {
        FcbClass rootClass = entityDefs.GetClass(root.TypeHash);
        XElement el = WriteObject(root, rootClass, refCorpus);
        return new XDocument(el).ToString();
    }

    private static XElement WriteObject(FcbObject obj, FcbClass ownClass, FcbStringCorpus refCorpus)
    {
        var el = new XElement("object");

        string? className = ownClass.Name
            ?? (SaveGameFieldCatalog.TryResolveClassName(obj.TypeHash, out string? catName) ? catName : null)
            ?? SaveGamePersistenceTags.ByHash.GetValueOrDefault(obj.TypeHash)
            ?? SaveGameCompiledFieldNames.ByHash.GetValueOrDefault(obj.TypeHash);

        if (className is not null)
        {
            el.SetAttributeValue("type", className);
        }
        else
        {
            el.SetAttributeValue("hash", obj.TypeHash.ToString("X8"));
        }

        foreach ((uint nameHash, byte[] value) in obj.Values)
        {
            el.Add(WriteValue(nameHash, value, ownClass, refCorpus));
        }

        foreach (FcbObject child in obj.Children)
        {
            el.Add(WriteObject(child, ownClass.Resolve(child.TypeHash), refCorpus));
        }

        return el;
    }

    private static XElement WriteValue(uint nameHash, byte[] value, FcbClass ownClass, FcbStringCorpus refCorpus)
    {
        // 1) a real, class-scoped entitylibrary member - the same resolution + encoding FcbXml.RenderObject
        //    itself uses for an ordinary .fcb (walks ownClass's own superclass chain).
        FcbMember? member = ownClass.FindMember(nameHash);
        if (member is not null && TryBuildTyped(nameHash, member.Name, member.Type, value, out XElement? typed))
        {
            return typed;
        }

        // 2) binary_classes.xml's flat (non-class-scoped) member list - a coincidentally-matching name
        //    declared by some unrelated class, only trusted once its declared type's byte length actually
        //    matches this specific value (see SaveGameFieldCatalog's remarks).
        bool catalogHasMember = SaveGameFieldCatalog.TryResolveMember(nameHash, out string? catalogName, out FcbMemberType catalogType);
        if (catalogHasMember && TryBuildTyped(nameHash, catalogName, catalogType, value, out XElement? catalogTyped))
        {
            return catalogTyped;
        }

        // 3) name-only dictionaries (decompiled structural tags, then the string-table dictionary attack)
        //    - a real name, but no verified wire type, so this only ever feeds the name/hash attribute
        //    choice below, never a type.
        string? name = SaveGamePersistenceTags.ByHash.GetValueOrDefault(nameHash)
            ?? SaveGameCompiledFieldNames.ByHash.GetValueOrDefault(nameHash)
            ?? (catalogHasMember ? catalogName : null); // catalog knew the name but rejected the type

        var el = new XElement("value");
        if (name is not null)
        {
            el.SetAttributeValue("name", name);
        }
        else
        {
            el.SetAttributeValue("hash", nameHash.ToString("X8"));
        }

        // 4) content-sniffed guesses for whatever's still opaque. Printable-ASCII+NUL reads as String -
        //    the same type FcbXml.TryWriteValue itself would produce for a declared String member, so
        //    this still matches the base .fcb shape even though the name/type pairing wasn't declared
        //    anywhere. A still-BinHex 4-byte payload gets checked against the entitylibrary string corpus
        //    as a candidate Hash reference - additive only (a `ref` attribute has no equivalent in the
        //    base .fcb format; there's no declared type to replace the way name replaces hash above).
        if (SaveGameValueDecoder.TryDecodeReadableString(value, out string text))
        {
            el.SetAttributeValue("type", nameof(FcbMemberType.String));
            el.Value = text;
            return el;
        }

        el.SetAttributeValue("type", nameof(FcbMemberType.BinHex));
        el.Value = Convert.ToHexString(value);

        if (value.Length == 4)
        {
            uint asHash = BitConverter.ToUInt32(value, 0);
            if (SaveGameReferenceResolver.TryResolve(refCorpus, asHash, out string? refName))
            {
                el.SetAttributeValue("ref", refName);
            }
        }

        return el;
    }

    /// <summary>Builds a <c>&lt;value&gt;</c> element the same way <c>FcbXml</c>'s own writer would for
    /// a genuinely class-resolved member - <see langword="false"/> (leaving <paramref name="element"/>
    /// null) if <paramref name="type"/> is unknown or <paramref name="value"/>'s length doesn't match
    /// what it requires, so the caller can fall through to a weaker resolution instead of showing
    /// something wrong.</summary>
    private static bool TryBuildTyped(
        uint hash, string? name, FcbMemberType type, byte[] value,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out XElement? element)
    {
        element = null;
        if (type == FcbMemberType.BinHex)
        {
            return false;
        }

        // A declared String member is trustworthy for an ordinary .fcb (the class assignment itself is
        // known-good there), but not here: a savegame hash matching some binary_classes.xml member
        // declared "String" is only proof the *name* coincides (see SaveGameFieldCatalog's remarks), not
        // that these particular bytes really are text. FcbXml.TryWriteValue's own String case only
        // checks for a trailing NUL, then blindly UTF8-decodes everything before it - arbitrary binary
        // data can satisfy that and still contain raw control bytes XElement can't serialize at all
        // (0x00 itself throws). Gate on the same printable-ASCII check the opaque-BinHex fallback below
        // already uses, rather than let a wrong guess crash the whole render.
        if (type == FcbMemberType.String && !SaveGameValueDecoder.TryDecodeReadableString(value, out _))
        {
            return false;
        }

        var el = new XElement("value");
        if (name is not null)
        {
            el.SetAttributeValue("name", name);
        }
        else
        {
            el.SetAttributeValue("hash", hash.ToString("X8"));
        }

        if (!FcbXml.TryWriteValue(el, type, value))
        {
            return false;
        }

        el.SetAttributeValue("type", type.ToString());
        element = el;
        return true;
    }
}
