using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.Bark;
using JackAll.Tools.Mab;

namespace JackAll.Tools.SoundLabels;

/// <summary>One place a sound id is named: its category, what plays it (<see cref="Subject"/>), when
/// (<see cref="Action"/>), the file naming it, and who speaks it when that is known.</summary>
public sealed record SoundUse(uint Id, string Category, string Subject, string Action, string File, string? Speaker = null);

/// <summary>
/// Finds the sound ids a game file names, one finder per format. Ids are written as text everywhere:
/// <c>0x</c> and eight hex digits in an `.fcb` String, an `.xml` attribute or a Lua string.
/// </summary>
public static partial class SoundUses
{
    private static readonly string[] XmlNameAttributes = ["Name", "hidName", "disName", "sName", "name", "title"];

    /// <summary>The id <paramref name="text"/> spells, or null for anything else and for the two
    /// "no sound" values, 0 and 0xFFFFFFFF.</summary>
    public static uint? ParseId(ReadOnlySpan<byte> text)
    {
        text = text.TrimEnd((byte)0);
        return text.Length == 10 && text[0] == '0' && (text[1] | 0x20) == 'x'
            && uint.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint id)
            && id is not (0 or uint.MaxValue)
                ? id
                : null;
    }

    public static uint? ParseId(string text) => text.Length == 10 ? ParseId(Encoding.ASCII.GetBytes(text)) : null;

    /// <summary>Sound fields of an `.fcb` tree, each with the archetype or entity it sits in.</summary>
    public static IEnumerable<SoundUse> InFcb(byte[] content, string path, IFcbClassScope classes)
    {
        if (content.AsSpan().IndexOf("0x"u8) < 0 || FcbDocument.TryDeserialize(content) is not { } root)
        {
            return [];
        }
        var uses = new List<SoundUse>();
        Walk(root, classes, entity: "", isEntity: false, parent: "", uses, path);
        return uses;
    }

    /// <summary>An object's <c>hidName</c> names everything below it; a folder's <c>Name</c> does only
    /// until an entity's <c>hidName</c> takes over.</summary>
    private static void Walk(FcbObject obj, IFcbClassScope scope, string entity, bool isEntity, string parent,
        List<SoundUse> uses, string path)
    {
        FcbClass own = scope.Resolve(obj);
        string ownName = own.Name ?? $"#{obj.TypeHash:X8}";
        string component = parent.Length > 0 ? $"{parent}/{ownName}" : ownName;
        string named = entity;
        if (FcbEntityFields.ReadString(obj, WorldHashes.HidName) is { Length: > 0 } hid)
        {
            (named, isEntity) = (hid, true);
        }
        else if (!isEntity && FcbEntityFields.ReadString(obj, WorldHashes.Name) is { Length: > 0 } name)
        {
            named = name;
        }

        foreach ((uint hash, byte[] value) in obj.Values)
        {
            FcbMember? member = own.FindMember(hash);
            if (member is not null && member.Type != FcbMemberType.String || ParseId(value) is not { } id)
            {
                continue;
            }
            string field = member?.Name ?? $"#{hash:X8}";
            string action = SoundCategories.Field(field) is { Length: > 0 } words ? words : SoundCategories.Field(component);
            uses.Add(new SoundUse(id, SoundCategories.Of(SoundCategories.Context(named, component, field)),
                SoundCategories.Entity(named), action, path));
        }

        foreach (FcbObject child in obj.Children)
        {
            Walk(child, own, named, isEntity, ownName, uses, path);
        }
    }

    /// <summary>Sounds an animation's events play, described by the clip's path.</summary>
    public static IEnumerable<SoundUse> InAnimation(byte[] content, string path)
    {
        MabFile file;
        try
        {
            file = MabFile.Parse(content);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            return [];
        }

        (string subject, string action) = SoundCategories.Animation(path);
        string category = SoundCategories.Of(SoundCategories.Context(path));
        return
            from clip in file.Clips()
            let events = clip.Events()
            where events is not null
            from payload in MabClip.EventNodePayloads(events)
            let node = FcbDocument.TryDeserialize(payload)
            where node is not null
            from obj in node.Descendants().Prepend(node)
            from value in obj.Values.Values
            let id = ParseId(value)
            where id is not null
            select new SoundUse(id.Value, category, subject, action, path);
    }

    /// <summary>Sound attributes of a config `.xml`, each with the nearest named element above it.</summary>
    public static IEnumerable<SoundUse> InXml(byte[] content, string path)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(new MemoryStream(content));
        }
        catch (XmlException)
        {
            return [];
        }

        return
            from element in document.Descendants()
            from attribute in element.Attributes()
            let id = ParseId(attribute.Value)
            where id is not null
            let subject = element.AncestorsAndSelf()
                .Select(e => XmlNameAttributes.Select(n => e.Attribute(n)?.Value).FirstOrDefault(v => !string.IsNullOrEmpty(v)))
                .FirstOrDefault(v => v is not null) ?? ""
            let ancestry = string.Join('/', element.AncestorsAndSelf().Reverse().Select(e => e.Name.LocalName))
            let action = $"{element.Name.LocalName.ToLowerInvariant()} {SoundCategories.Field(attribute.Name.LocalName)}".Trim()
            select new SoundUse(id.Value, SoundCategories.Of(SoundCategories.Context(path, ancestry, attribute.Name.LocalName)),
                subject, action, path);
    }

    /// <summary>Sound ids a Domino script sets on its boxes. A <c>&lt;Name&gt;Start</c> field names
    /// the buddy who speaks the line.</summary>
    public static IEnumerable<SoundUse> InScript(byte[] content, string path)
    {
        string text = Encoding.UTF8.GetString(content);
        var boxTypes = BoxCreation().Matches(text).ToDictionary(m => m.Groups["box"].Value, m => m.Groups["type"].Value);
        string script = string.Join(", ", Path.GetFileNameWithoutExtension(path).Split('.').Distinct()).Replace('_', ' ');

        foreach (Match m in BoxField().Matches(text))
        {
            if (ParseId(m.Groups["id"].Value) is not { } id)
            {
                continue;
            }
            string type = m.Groups["pooled"].Success ? m.Groups["pooled"].Value : boxTypes.GetValueOrDefault(m.Groups["box"].Value, "");
            string boxType = Path.GetFileNameWithoutExtension(type);
            string field = m.Groups["field"].Value;
            string? speaker = SpeakerField().Match(field) is { Success: true } s ? s.Groups[1].Value : null;
            yield return new SoundUse(id, SoundCategories.Of(SoundCategories.Context(path, boxType, field)),
                script, $"{boxType} {SoundCategories.Field(field)}".Trim(), path, speaker);
        }
    }

    /// <summary>Every line of a bark bank, spoken by its speaker.</summary>
    public static IEnumerable<SoundUse> InBarks(byte[] content, string path) =>
        from bark in BarkBank.Read(content)
        from line in bark.Lines
        from id in line.SoundIds
        select new SoundUse(id, SoundCategories.Dialogue, bark.Mission ?? "", bark.Block, path,
            line.Speaker.Length > 0 ? line.Speaker : null);

    [GeneratedRegex("""(?<box>self\[\d+\]|self\.\w+)\s*=\s*cbox:CreateBox\("(?<type>[^"]+)"\)""")]
    private static partial Regex BoxCreation();

    [GeneratedRegex(""""(?<box>self\[\d+\]|self\.\w+|Boxes\[PathID\("(?<pooled>[^"]+)"\)\])\.(?<field>\w+)\s*=\s*"(?<id>0x[0-9A-Fa-f]{8})"""")]
    private static partial Regex BoxField();

    [GeneratedRegex("^([A-Z][a-z]+)Start$")]
    private static partial Regex SpeakerField();
}
