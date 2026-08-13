namespace JackAll.Tools.Mgb;

/// <summary>
/// Base of every record in a <c>.mgb</c> package.
/// </summary>
/// <remarks>
/// Wire data is held in public fields rather than properties. That is deliberate: this is a
/// byte-faithful DTO layer, <see cref="Serialize"/> needs to pass each field by <c>ref</c> to the
/// codec, and the editor builds its own view models on top rather than binding to the model
/// directly (the same shape the FCB editor in JackAll.App/FileHandlers/Fcb/FcbEditor already uses).
///
/// Field names are the authored names recovered from <c>magma::LoadVisitor</c>'s XML twin, in
/// PascalCase - see docs/docs/file-formats/mgb-field-names.md.
/// </remarks>
public abstract class MgbRecord
{
    /// <summary>Describes this record's wire format once, for both directions. Fields must be
    /// visited in exactly the order the engine reads them.</summary>
    public abstract void Serialize(IMgbCodec c, MgbContext ctx);

    /// <summary>A count-prefixed list of homogeneous records. The count is derived from the live
    /// collection on write, so the two can never disagree.</summary>
    protected static void SerializeList<T>(
        IMgbCodec c, MgbContext ctx, string listName, string itemName, List<T> items)
        where T : MgbRecord, new()
        => MgbRecordHelpers.List(c, ctx, listName, itemName, items);

    /// <summary>An optional sub-record behind a <c>bool</c> gate. On write the gate is derived from
    /// whether the record is present.</summary>
    protected static void SerializeOptional<T>(IMgbCodec c, MgbContext ctx, string name, ref T? item)
        where T : MgbRecord, new()
        => MgbRecordHelpers.Optional(c, ctx, name, ref item);
}

/// <summary>The list/optional helpers, also reachable from <see cref="MgbPackage"/> which is not
/// itself a record.</summary>
internal static class MgbRecordHelpers
{
    public static void List<T>(
        IMgbCodec c, MgbContext ctx, string listName, string itemName, List<T> items)
        where T : MgbRecord, new()
    {
        int n = items.Count;
        using (c.ListScope(listName, ref n))
        {
            if (c.IsReading)
            {
                items.Clear();
                for (int i = 0; i < n; i++)
                {
                    items.Add(new T());
                }
            }
            foreach (T item in items)
            {
                using (c.Item(itemName))
                {
                    item.Serialize(c, ctx);
                }
            }
        }
    }

    /// <summary>A <c>CHILDREN</c> list whose entries each carry their own type slot. On read the
    /// slot's resolved class name is handed to <paramref name="create"/>, which validates it and
    /// builds the entry; on write each entry's stored slot is re-emitted.</summary>
    public static void SlottedList<T>(
        IMgbCodec c, MgbContext ctx, string itemName, List<T> items,
        Func<T, byte> slotOf, Func<byte, string?, T> create)
        where T : MgbRecord
    {
        int n = items.Count;
        using (c.ListScope("CHILDREN", ref n))
        {
            if (c.IsReading)
            {
                items.Clear();
                for (int i = 0; i < n; i++)
                {
                    using (c.Item(itemName))
                    {
                        byte slot = 0;
                        c.TypeSlot("slot", "type", ref slot, ctx.Types);
                        T item = create(slot, ctx.Types.NameForSlot(slot));
                        item.Serialize(c, ctx);
                        items.Add(item);
                    }
                }
                return;
            }
            foreach (T item in items)
            {
                using (c.Item(itemName))
                {
                    byte slot = slotOf(item);
                    c.TypeSlot("slot", "type", ref slot, ctx.Types);
                    item.Serialize(c, ctx);
                }
            }
        }
    }

    public static void Optional<T>(IMgbCodec c, MgbContext ctx, string name, ref T? item)
        where T : MgbRecord, new()
    {
        bool present = c.Gate(name, item is not null);
        if (c.IsReading)
        {
            item = present ? new T() : null;
        }
        if (item is null)
        {
            return;
        }
        using (c.Scope(name))
        {
            item.Serialize(c, ctx);
        }
    }
}

/// <summary>
/// <c>LoadMaterial</c> (<c>0x0a0608c0</c>) and <c>LoadFontFamily</c> (<c>0x0a060f20</c>) - two
/// byte-identical functions differing only in which <c>Package::Find*</c> lookup they run
/// afterwards. A deferred reference to a resource owned by this or another package.
/// </summary>
public sealed class MgbResourceRef : MgbRecord
{
    /// <summary>When false nothing else is on the wire and the object keeps its default.</summary>
    public bool Present;

    /// <summary>The resource's name hash.</summary>
    public uint Id;

    /// <summary>The owning package's name; empty means the current package.</summary>
    public byte[] PackageName = [];

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        // Not a Gate: the owning record always writes this one as a named scope, so absence has to
        // be spelled out rather than implied by a missing element.
        c.Bool("present", ref Present);
        if (!Present)
        {
            return;
        }
        c.NameId("id", ref Id);
        c.AnsiString("PACKAGE", ref PackageName);
    }
}

/// <summary><c>VisitFullLink</c> (<c>0x0a0604d0</c>): a typed list of object-id references. A count
/// of zero returns immediately - no type byte, no ids - which is easy to get wrong.</summary>
public sealed class MgbFullLink : MgbRecord
{
    /// <summary>Type slot of the referenced class (<c>LASTOBJECTTYPE</c>). Only present when
    /// <see cref="Ids"/> is non-empty.</summary>
    public byte TypeSlot;

    public List<uint> Ids = [];

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        int count = c.Count("IDS", Ids.Count, MgbCountWidth.U16);
        if (count == 0)
        {
            if (c.IsReading)
            {
                Ids.Clear();
            }
            return;
        }
        c.TypeSlot("slot", "LASTOBJECTTYPE", ref TypeSlot, ctx.Types);
        if (c.IsReading)
        {
            Ids.Clear();
            for (int i = 0; i < count; i++)
            {
                Ids.Add(0);
            }
        }
        c.NameIdItems("IDS", Ids);
    }
}

/// <summary><c>VisitStringResourceExternalId</c> (<c>0x0a05feb0</c>): a localised-string reference,
/// authored as <c>TABLEID</c> + <c>RESOURCEID</c>.</summary>
public sealed class MgbStringResourceExternalId : MgbRecord
{
    public uint TableId;
    public uint ResourceId;

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.NameId("TABLEID", ref TableId);
        c.NameId("RESOURCEID", ref ResourceId);
    }
}

/// <summary>One property of a <see cref="MgbUserData"/> record.</summary>
/// <remarks>
/// <c>VisitUserData</c>'s payload dispatch is a switch whose default consumes nothing, so any tag
/// outside the payload-bearing set below is legal, payload-less content - never an error. Treating
/// an unknown tag as a failure was, historically, the single highest-impact bug in this decoder.
/// </remarks>
public sealed class MgbProperty : MgbRecord
{
    public const uint TagUInt32 = 0x02;
    public const uint TagFloat = 0x07;
    public const uint TagBool = 0x0C;
    public const uint TagString = 0x10;
    public const uint TagFullLinkA = 0x11;
    public const uint TagFullLinkB = 0x12;
    public const uint TagStringResource = 0x13;
    public const uint TagFullLinkC = 0x15;

    /// <summary>The property name's hash.</summary>
    public uint Key;

    public uint TypeTag;

    public uint ScalarValue;          // tags 0x02 and 0x07 (the latter as raw float bits)
    public bool BoolValue;            // tag 0x0c
    public byte[] StringValue = [];   // tag 0x10
    public MgbFullLink? Link;         // tags 0x11 / 0x12 / 0x15
    public MgbStringResourceExternalId? StringResource; // tag 0x13

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.NameId("key", ref Key);
        c.U32("type", ref TypeTag);
        switch (TypeTag)
        {
            case TagUInt32:
                c.U32("value", ref ScalarValue);
                break;
            case TagFloat:
                // The one field whose kind is decided by a sibling rather than by its own name.
                c.F32Bits("value", ref ScalarValue);
                break;
            case TagBool:
                c.Bool("value", ref BoolValue);
                break;
            case TagString:
                c.AnsiString("value", ref StringValue);
                break;
            case TagFullLinkA:
            case TagFullLinkB:
            case TagFullLinkC:
                Link ??= new MgbFullLink();
                using (c.Scope("LINK"))
                {
                    Link.Serialize(c, ctx);
                }
                break;
            case TagStringResource:
                StringResource ??= new MgbStringResourceExternalId();
                using (c.Scope("STRINGRESOURCE"))
                {
                    StringResource.Serialize(c, ctx);
                }
                break;
            default:
                // Every other tag - enumerated in the engine's switch or not - carries nothing.
                break;
        }
    }
}

/// <summary><c>VisitUserData</c> (<c>0x0a062c90</c>): the generic property system, used by
/// <c>Package</c>, <c>Area</c>, <c>Element</c> and every <c>Action</c>.</summary>
public sealed class MgbUserData : MgbRecord
{
    /// <summary>From the inherited <c>VisitNamedObject</c>: the object's name hash.</summary>
    public uint NameId;

    public List<MgbProperty> Properties = [];

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.NameId("name", ref NameId);
        SerializeList(c, ctx, "PROPERTIES", "PROPERTY", Properties);
    }
}

/// <summary><c>VisitAreaLink</c> (<c>0x0a0601c0</c>): a reference to an area in this or another
/// package, with the timing strategy that drives it.</summary>
public sealed class MgbAreaLink : MgbRecord
{
    /// <summary>Type slot of a <c>TimingStrategy</c> subclass (<c>TIMING</c>).</summary>
    public byte TimingSlot;

    public uint Package;

    /// <summary><c>AREA</c>; null when the gate byte is clear.</summary>
    public uint? Area;

    public bool IsUsingDuplicatedArea;

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.TypeSlot("slot", "TIMING", ref TimingSlot, ctx.Types);
        c.NameId("PACKAGE", ref Package);
        c.OptionalNameId("AREA", ref Area);
        c.Bool("ISUSINGDUPLICATEDAREA", ref IsUsingDuplicatedArea);
    }
}
