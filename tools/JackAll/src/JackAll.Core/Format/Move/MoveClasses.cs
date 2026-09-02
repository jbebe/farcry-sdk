namespace JackAll.Core.Format.Move;

/// <summary>The serializer's feature flags, as carried by the file's dwFileFormat word.</summary>
public static class MoveFlags
{
    /// <summary>Serialize definitions rather than live channel values.</summary>
    public const uint Definitions = 0x10000;

    /// <summary>Authoring build: names and GUIDs inline. <c>CMoveMgr::CreateFromStream</c> rejects it.</summary>
    public const uint Named = 0x20000;

    /// <summary>Include the state graph, the transition matrix and criteria payloads.</summary>
    public const uint StateGraph = 0x40000;
}

/// <summary>
/// Every class the MOVE subsystem can serialize, keyed by the FourCC its
/// <c>GetSerializationClassID</c> returns. Stored little-endian, so they read reversed in a dump -
/// <c>CMoveMgr</c>'s 'MvMg' appears as <c>gMvM</c>.
/// </summary>
public static class MoveClasses
{
    private static readonly Dictionary<uint, string> ById = new()
    {
        [0x4D764D67] = "CMoveMgr",
        [0x4D76534D] = "CMoveStateMachine",
        [0x4D765643] = "CMoveValueContainer",
        [0x4D765664] = "CMoveValueDef",
        [0x4D764253] = "CMoveBaseState",
        [0x4D765354] = "CMoveState",
        [0x4D765379] = "CSyncState",
        [0x4D76444E] = "CDoNothing",
        [0x4D764466] = "CMoveDefinition",
        [0x4D764772] = "CMoveGroup",
        [0x4D436D74] = "CMoveComment",
        [0x4D537452] = "CMoveStateRef",
        [0x4C537452] = "CLayeredStateRef",
        [0x4C795354] = "CLayeredState",
        [0x4C794178] = "CLayeredAxialBlend",
        [0x4C795061] = "CLayeredParameter",
        [0x506C4D53] = "CPlayerMoveState",
        [0x466B5354] = "CFrankensteinState",
        [0x466B5061] = "CFrankensteinParameter",
        [0x42534147] = "CAxialBlendAnimGroup",
        [0x41416E63] = "CAnimTechAnchor",
        [0x41744174] = "CAnimTechAttach",
        [0x4174494B] = "CAnimTechIKPath",
        [0x4174506F] = "CAnimTechPossession",
        [0x41526167] = "CAnimTechRagdoll",
        [0x416E5061] = "CMoveDefParameter",
        [0x53794465] = "CSyncDefinition",
        [0x53795061] = "CSyncDefParameter",
        [0x54434C70] = "CTimeControlledLayeredParameter",
        [0x54434D70] = "CTimeControlledMoveParameter",
        [0x4E494C73] = "CNotInterruptibleLink",
        [0x544C4173] = "CTransitionLink",
        [0x4D454944] = "CMoveCriteriaEntityIDEqual",
        [0x43494E45] = "CMoveCriteriaEntityIDNotEqual",
        [0x4D434545] = "CMoveCriteriaEnumEqual",
        [0x43454E45] = "CMoveCriteriaEnumNotEqual",
        [0x4D455543] = "TMoveCriteriaEqual<uint8>",
        [0x4D4E4543] = "TMoveCriteriaNotEqual<uint8>",
        [0x4D634549] = "TMoveCriteriaEqual<int>",
        [0x4D4E4549] = "TMoveCriteriaNotEqual<int>",
        [0x4D634542] = "TMoveCriteriaEqual<bool>",
        [0x4D4E4542] = "TMoveCriteriaNotEqual<bool>",
        [0x4D634949] = "TMoveCriteriaIntv<int>",
        [0x4D634946] = "TMoveCriteriaIntv<float>",
        [0x4D634941] = "TMoveCriteriaIntv<CAngle>",
        [0x4D635049] = "TMoveCriteriaPerc<int>",
        [0x4D635046] = "TMoveCriteriaPerc<float>",
    };

    private static readonly Dictionary<string, uint> ByName =
        ById.ToDictionary(p => p.Value, p => p.Key);

    public static string? Name(uint id) => ById.GetValueOrDefault(id);

    public static uint Id(string name) => ByName.TryGetValue(name, out uint id)
        ? id
        : throw new MoveFormatException($"'{name}' is not a MOVE class");
}
