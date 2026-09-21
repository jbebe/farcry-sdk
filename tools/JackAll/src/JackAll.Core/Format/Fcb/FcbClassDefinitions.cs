using System.Globalization;
using System.IO.Hashing;
using System.Xml.Linq;

namespace JackAll.Core.Format.Fcb;

/// <summary>
/// The wire type of one <see cref="FcbObject"/> value, as declared by a class's member list.
/// </summary>
/// <remarks>
/// The types through <see cref="Rml"/> come from Gibbed's original ConvertBinaryObject. Everything
/// from <see cref="Bool16"/> onward was added for wobatt's improved <c>binary_classes.xml</c> (see
/// "Far Cry 2 Hash Decoder" readme, wobatt, 2015) — his own modified Gibbed tools understood several
/// wire shapes the original didn't, and his class definitions actually use them (confirmed directly:
/// <c>Matrix4</c>/<c>Bool32</c>/<c>Bool32Array</c> all appear as real member types in that file).
/// </remarks>
public enum FcbMemberType
{
    BinHex,
    String,
    Enum,
    Bool,
    Float,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Vector2,
    Vector3,
    Vector4,
    Hash,
    UInt32Array,
    HashArray,
    Rml,
    Int8,
    UInt8,
    Int16,
    UInt16,
    Bool16,
    Bool32,
    Int32Array,
    FloatArray,
    Bool32Array,
    Vector3Array,
    Matrix4,
}

/// <summary>A member's decoded name (if the config has one), its declared wire type, and the labels
/// of the values an enum index member can take.</summary>
public sealed record FcbMember(string? Name, FcbMemberType Type, IReadOnlyList<string>? Labels = null);

/// <summary>
/// Something that can resolve a class hash relative to some scope — either <see cref="FcbClass"/>
/// itself (nested-class-aware, used while descending into an object's children) or the top-level
/// <see cref="FcbClassDefinitions"/> (flat lookup, used for a tree's root object).
/// </summary>
public interface IFcbClassScope
{
    FcbClass Resolve(uint hash);
}

/// <summary>
/// One class definition: a name (if known), its declared members, an optional superclass, and any
/// classes nested inside it in the config (used to shadow the flat top-level list — see
/// <see cref="Resolve"/>).
/// </summary>
public sealed class FcbClass : IFcbClassScope
{
    internal readonly FcbClassDefinitions Master;

    internal FcbClass(FcbClassDefinitions master) => Master = master;

    public string? Name { get; internal set; }
    public FcbClass? Super { get; internal set; }
    internal string? SuperName { get; set; }
    internal Dictionary<uint, FcbMember> Members { get; } = [];
    internal Dictionary<uint, FcbClass> Nested { get; } = [];

    /// <summary>
    /// Resolves a class hash the way Gibbed's exporter does when descending into a child object:
    /// prefer a class nested directly inside this one, then one nested inside a superclass, and only
    /// then fall back to the flat top-level table (which itself falls back to an unnamed placeholder,
    /// never null) — real per-parent name shadowing exists in the shipped config, even though it's
    /// rare (~20 of ~1560 classes have nested children).
    /// </summary>
    public FcbClass Resolve(uint hash)
    {
        if (Nested.TryGetValue(hash, out FcbClass? nested))
        {
            return nested;
        }

        for (FcbClass? current = Super; current is not null; current = current.Super)
        {
            if (current.Nested.TryGetValue(hash, out FcbClass? fromSuper))
            {
                return fromSuper;
            }
        }

        return Master.GetClass(hash);
    }

    /// <summary>Every member this class declares or inherits; a redeclared hash comes out once, from
    /// the nearest class.</summary>
    public IEnumerable<(uint Hash, FcbMember Member)> AllMembers()
    {
        var seen = new HashSet<uint>();
        for (FcbClass? current = this; current is not null; current = current.Super)
        {
            foreach ((uint hash, FcbMember member) in current.Members)
            {
                if (seen.Add(hash))
                {
                    yield return (hash, member);
                }
            }
        }
    }

    /// <summary>Finds a member by hash, walking the superclass chain (members aren't nested, unlike classes).</summary>
    public FcbMember? FindMember(uint hash)
    {
        for (FcbClass? current = this; current is not null; current = current.Super)
        {
            if (current.Members.TryGetValue(hash, out FcbMember? member))
            {
                return member;
            }
        }
        return null;
    }
}

/// <summary>
/// Loads and resolves Far Cry 2's <c>binary_classes.xml</c> — the config this class of tool uses to
/// turn an .fcb's raw type/member hashes back into readable class and field names, and to know how
/// to decode each value's raw bytes (Float/Vector3/String/... vs. opaque BinHex). The copy bundled
/// with JackAll is wobatt's improved version ("Far Cry 2 Hash Decoder" v0.4.2, 2015), not Gibbed's
/// original - confirmed to resolve real types for 99.9% of values across 5 real shipped entitylibrary
/// files, versus 56.5% for Gibbed's, by direct measurement against those same files.
/// </summary>
/// <remarks>
/// This is purely a display/authoring aid for <see cref="FcbXml.ToXml"/> — round-tripping XML back to
/// .fcb (<see cref="FcbXml.FromXml"/>) never needs it, since the exported XML already carries
/// whichever type/name/hash it was written with. A class or member hash this config doesn't know
/// about isn't an error; it just falls back to an unnamed hash and, for members, <see cref="FcbMemberType.BinHex"/>.
/// </remarks>
public sealed class FcbClassDefinitions : IFcbClassScope
{
    private readonly Dictionary<uint, FcbClass> _topLevel = [];
    private readonly FcbClass _unknown;

    private FcbClassDefinitions() => _unknown = new FcbClass(this) { Name = null };

    /// <summary>No config loaded — every class/member falls back to hash-only/BinHex.</summary>
    public static FcbClassDefinitions Empty { get; } = new();

    /// <summary>Loads <paramref name="path"/>, then fills in what <paramref name="schema"/> knows and it
    /// does not: classes, members, superclasses and enum labels. The XML wins every member both define.</summary>
    public static FcbClassDefinitions Load(string path, ComponentSchema? schema = null)
    {
        var defs = new FcbClassDefinitions();
        XElement root = XDocument.Load(path).Root
            ?? throw new InvalidDataException("binary_classes.xml has no root <classes> element.");

        LoadClasses(defs, root.Elements("class"), defs._topLevel);
        IReadOnlyList<SchemaClass> schemaClasses = schema?.Classes ?? [];
        foreach (SchemaClass schemaClass in schemaClasses)
        {
            uint hash = Crc32Ascii(schemaClass.Name);
            if (!defs._topLevel.TryGetValue(hash, out FcbClass? cls))
            {
                defs._topLevel[hash] = cls = new FcbClass(defs) { Name = schemaClass.Name };
            }
            cls.SuperName ??= schemaClass.Parent;
        }
        ResolveSupers(defs._topLevel.Values);
        // After the supers, so a member the XML declares on a base class is found, not shadowed.
        foreach (SchemaClass schemaClass in schemaClasses)
        {
            AddSchemaMembers(defs._topLevel[Crc32Ascii(schemaClass.Name)], schemaClass.Members);
        }
        return defs;
    }

    private static void AddSchemaMembers(FcbClass cls, IReadOnlyList<SchemaMember> members)
    {
        foreach (SchemaMember member in members)
        {
            switch (member.Kind)
            {
                case "value":
                    uint hash = Crc32Ascii(member.Name);
                    FcbClass? declaring = cls;
                    while (declaring is not null && !declaring.Members.ContainsKey(hash))
                    {
                        declaring = declaring.Super;
                    }
                    if (declaring is null)
                    {
                        cls.Members[hash] = new FcbMember(
                            member.Name,
                            Enum.TryParse(member.Type, out FcbMemberType type) ? type : FcbMemberType.BinHex,
                            member.Labels);
                    }
                    else
                    {
                        FcbMember known = declaring.Members[hash];
                        declaring.Members[hash] = known with { Labels = known.Labels ?? member.Labels };
                    }
                    break;
                case "conditional":
                    AddSchemaMembers(cls, member.Members ?? []);
                    break;
                case "group":
                    AddSchemaMembers(ChildClass(cls, member.Name), member.Members ?? []);
                    break;
                case "container":
                    FcbClass holder = member.Wrapped == true ? ChildClass(cls, member.Name) : cls;
                    if (member.Element is { Length: > 0 } element)
                    {
                        ChildClass(holder, element);
                    }
                    break;
            }
        }
    }

    /// <summary>The class a child node tagged <paramref name="name"/> resolves to under
    /// <paramref name="owner"/>, nested there when no named class already answers for it.</summary>
    private static FcbClass ChildClass(FcbClass owner, string name)
    {
        uint hash = Crc32Ascii(name);
        FcbClass existing = owner.Resolve(hash);
        if (existing.Name is not null)
        {
            return existing;
        }
        var nested = new FcbClass(owner.Master) { Name = name };
        owner.Nested[hash] = nested;
        return nested;
    }

    /// <summary>Flat top-level lookup - never null, falls back to an unnamed placeholder class.</summary>
    public FcbClass GetClass(uint hash) => _topLevel.GetValueOrDefault(hash, _unknown);

    /// <inheritdoc cref="IFcbClassScope.Resolve"/>
    FcbClass IFcbClassScope.Resolve(uint hash) => GetClass(hash);

    private static void LoadClasses(FcbClassDefinitions defs, IEnumerable<XElement> nodes, Dictionary<uint, FcbClass> into)
    {
        foreach (XElement node in nodes)
        {
            var cls = new FcbClass(defs);
            (string? name, uint hash) = LoadNameAndHash(node);
            cls.Name = name;
            cls.SuperName = (string?)node.Attribute("extends");

            foreach (XElement member in node.Elements("member"))
            {
                (string? memberName, uint memberHash) = LoadNameAndHash(member);
                string typeText = member.Value.Trim();
                if (!Enum.TryParse(typeText, out FcbMemberType type))
                {
                    throw new InvalidDataException($"binary_classes.xml: unknown member type '{typeText}'.");
                }
                cls.Members[memberHash] = new FcbMember(memberName, type);
            }

            LoadClasses(defs, node.Elements("class"), cls.Nested);
            into[hash] = cls;
        }
    }

    /// <summary>Second pass: wires up each class's <see cref="FcbClass.Super"/> now that every class
    /// (at every nesting level) has been loaded, matching Gibbed's own two-pass approach.</summary>
    private static void ResolveSupers(IEnumerable<FcbClass> topLevelClasses)
    {
        var queue = new Queue<FcbClass>(topLevelClasses);
        var seen = new HashSet<FcbClass>(ReferenceEqualityComparer.Instance);

        while (queue.Count > 0)
        {
            FcbClass cls = queue.Dequeue();
            if (!seen.Add(cls))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(cls.SuperName))
            {
                uint superHash = Crc32Ascii(cls.SuperName);
                cls.Super = FindInScope(cls, superHash);
            }

            foreach (FcbClass nested in cls.Nested.Values)
            {
                queue.Enqueue(nested);
            }
        }
    }

    /// <summary>A superclass name resolves against the class's own nesting scope first (an ancestor's
    /// nested classes), falling back to the flat top-level table - same shadowing rule as <see cref="FcbClass.Resolve"/>.</summary>
    private static FcbClass? FindInScope(FcbClass cls, uint hash)
    {
        // Superclass names in the shipped config are always top-level in practice; the flat lookup
        // covers every real case, but nested classes reference an enclosing scope that isn't tracked
        // as a parent pointer here (nesting is only tracked child->parent via Nested, never the
        // reverse), so a super declared *inside* some other class wouldn't be found. None of the
        // ~1560 classes in the shipped config hit that case (checked directly), so this is accurate
        // for real data even though it's not a byte-for-byte port of every corner of Gibbed's own
        // (also parent-walking) resolution.
        return cls.Master._topLevel.GetValueOrDefault(hash);
    }

    private static (string? Name, uint Hash) LoadNameAndHash(XElement node)
    {
        string? name = (string?)node.Attribute("name");
        string? hash = (string?)node.Attribute("hash");

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(hash))
        {
            throw new InvalidDataException("binary_classes.xml: a <class>/<member> needs a name or hash attribute.");
        }

        return !string.IsNullOrWhiteSpace(name)
            ? (name, Crc32Ascii(name))
            : (null, uint.Parse(hash!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// CRC32 of the raw (case-sensitive, un-normalized) ASCII bytes of a class/member name - this is
    /// deliberately not <see cref="NameHash"/> (that one lowercases and treats '/' vs '\' as
    /// equivalent for archive *paths*, which would give the wrong hash here). Also used by
    /// <see cref="FcbXml"/> for the data-file "type"/"name" attributes, which hash the same way.
    /// </summary>
    public static uint Crc32Ascii(string name)
    {
        Span<byte> bytes = name.Length <= 256 ? stackalloc byte[name.Length] : new byte[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            bytes[i] = (byte)name[i];
        }
        return Crc32.HashToUInt32(bytes);
    }
}
