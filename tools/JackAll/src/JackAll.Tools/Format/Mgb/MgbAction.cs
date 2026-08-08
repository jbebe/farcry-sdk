namespace JackAll.Tools.Format.Mgb;

/// <summary>
/// One entry of an <see cref="MgbActionExecuter"/>'s flat action list.
/// </summary>
/// <remarks>
/// <see cref="ActionId"/> is a raw <c>CRC32(ClassName)</c> handed straight to
/// <c>ActionServer::MakeAction</c> - not a per-file type-table slot like every other typed field in
/// this format.
///
/// No concrete opcode (<c>ActionContinue</c>, <c>ActionPushPage</c>, …) overrides <c>Visit*</c>, and
/// <c>VisitAction</c> (<c>0x0a05dd70</c>) forwards straight to <c>VisitUserData</c>, so every
/// action's payload is a plain property list whatever its opcode. A reader never needs to know which
/// opcode a hash names in order to read its bytes correctly.
/// </remarks>
public sealed class MgbAction : MgbRecord
{
    public uint ActionId;
    public MgbUserData Body = new();

    /// <summary>The opcode class name, when it is one this build knows.</summary>
    public string? OpcodeName => MgbActionNames.Resolve(ActionId);

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.NameId("ACTIONNAME", ref ActionId);
        using (c.Scope("USERDATA"))
        {
            Body.Serialize(c, ctx);
        }
    }
}

/// <summary>Resolves an action's raw <c>Id</c> hash back to an opcode name where possible.</summary>
public static class MgbActionNames
{
    private static readonly Dictionary<uint, string> ByHash = Build();

    public static string? Resolve(uint id) => ByHash.GetValueOrDefault(id);

    private static Dictionary<uint, string> Build()
    {
        string[] opcodes =
        [
            "ActionContinue", "ActionStop", "ActionPopPage", "ActionPushPage",
            "ActionGotoFrameIndex", "ActionGotoKeyFrame", "Action",
        ];
        var map = new Dictionary<uint, string>(opcodes.Length);
        foreach (string name in opcodes)
        {
            map[MgbTypeTable.Hash(name)] = name;
        }
        return map;
    }
}

/// <summary>A named group of indices into an executer's flat action list
/// (<c>VisitActionExecuterEvent</c>, <c>0x0a05e840</c>). The entries reference actions already
/// read - they are not new actions.</summary>
public sealed class MgbActionIndexGroup : MgbRecord
{
    public List<uint> Indices = [];

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        int n = c.Count("ACTIONINDEX", Indices.Count);
        if (c.IsReading)
        {
            Indices.Clear();
            for (int i = 0; i < n; i++)
            {
                Indices.Add(0);
            }
        }
        c.U32Items("ACTIONINDEX", Indices);
    }
}

/// <summary>
/// <c>VisitActionExecuter</c> (<c>0x0a05f870</c>) plus, for the eight named subtypes, the
/// <c>VisitActionExecuterEvent</c> tail. Which of the nine this is comes from the type slot the
/// owning <see cref="MgbActionCaller"/> read, not from anything in the body.
/// </summary>
public sealed class MgbActionExecuter : MgbRecord
{
    /// <summary>Which of the nine <c>Factory::MakeActionExecuter</c> classes this is. Decides
    /// whether <see cref="EventGroups"/> is on the wire at all.</summary>
    public string TypeName = "ActionExecuter";

    public List<MgbAction> Actions = [];

    /// <summary>Only present for the eight non-bare subtypes.</summary>
    public List<MgbActionIndexGroup> EventGroups = [];

    public bool HasEventTail => MgbSchema.ActionExecuterEventTypes.Contains(TypeName);

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        SerializeList(c, ctx, "ACTIONS", "ACTION", Actions);
        if (HasEventTail)
        {
            SerializeList(c, ctx, "EVENTS", "EVENT", EventGroups);
        }
    }
}

/// <summary>
/// <c>VisitActionCaller</c> (<c>0x0a05e910</c>) - the optional action handler that <c>Area</c>,
/// <c>Element</c> and <c>Keyframe</c> all read before their own fields.
/// </summary>
public sealed class MgbActionCaller : MgbRecord
{
    /// <summary>Null when the gate byte is clear, which is the common case.</summary>
    public MgbActionExecuter? Executer;

    /// <summary>Type slot the executer was built from. Preserved so a round-trip reproduces the
    /// file's own slot even when several slots resolve to the same class.</summary>
    public byte TypeSlot;

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        bool present = c.Gate("ACTIONEXECUTER", Executer is not null);
        if (!present)
        {
            if (c.IsReading)
            {
                Executer = null;
            }
            return;
        }

        using (c.Scope("ACTIONEXECUTER"))
        {
            c.TypeSlot("slot", "type", ref TypeSlot, ctx.Types);
            if (c.IsReading)
            {
                string? name = ctx.Types.NameForSlot(TypeSlot);
                // An unresolved or type-0 slot lands on bare ActionExecuter's shape - the only one
                // of the nine with no extra tail. A slot that resolves to a real class outside the
                // nine would mean Factory::MakeActionExecuter returned null and the game crashed,
                // so it cannot occur in a loadable file.
                if (name is not null && !MgbSchema.ActionExecuterTypes.Contains(name))
                {
                    throw new MgbFormatException(
                        $"ActionCaller type slot {TypeSlot} resolves to '{name}', which " +
                        $"Factory::MakeActionExecuter cannot construct, at offset {c.Position}");
                }
                Executer = new MgbActionExecuter
                {
                    TypeName = name is not null && MgbSchema.ActionExecuterTypes.Contains(name)
                        ? name
                        : "ActionExecuter",
                };
            }
            Executer!.Serialize(c, ctx);
        }
    }
}
