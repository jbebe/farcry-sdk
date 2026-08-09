namespace JackAll.Tools.Domino.Nodes;

/// <summary>Where a <see cref="NodeSignature"/>'s pin list came from, which is also how much to trust
/// it.</summary>
public enum SignatureOrigin
{
    /// <summary>Read verbatim from a `system\` node's own `-- DOMINO REFLECTION BOX` header - the same
    /// declaration BlackBox's palette read, so the pin set and its types are exact.</summary>
    Declared,

    /// <summary>Recovered by reading a `user\` sub-graph's generated code, because sub-graphs carry no
    /// reflection header. Control pins are structural and reliable; data pins are best-effort and
    /// untyped (see <see cref="DominoNodeCatalog"/>).</summary>
    Inferred,
}

/// <summary>
/// One node type's editor-facing interface, unified across the two kinds of box a graph can contain: a
/// `system\` library node (<see cref="SignatureOrigin.Declared"/>, straight from
/// <see cref="NodeReflection"/>) and a `user\` sub-graph used as a box
/// (<see cref="SignatureOrigin.Inferred"/>). The viewer needs one shape for both, since a graph mixes
/// them freely and a port has to be drawn either way.
/// </summary>
public sealed record NodeSignature(
    string TypePath,
    string DisplayName,
    string? Category,
    IReadOnlyList<ControlInPin> ControlIns,
    IReadOnlyList<ControlOutPin> ControlOuts,
    IReadOnlyList<DataInPin> DataIns,
    IReadOnlyList<DataOutPin> DataOuts,
    bool Stateless,
    SignatureOrigin Origin)
{
    /// <summary>The type name a node shows when it has no friendlier <see cref="NodeDisplay.Text"/> -
    /// the file's base name, e.g. `Domino/System/SetMissionBarkBankState.lua` becomes
    /// `SetMissionBarkBankState`. Sub-graph paths are dotted (`Common_MissionBriefings.BASEBRIEF_CONVO.lua`),
    /// so the last dotted segment is the graph's own name and the rest is its document.</summary>
    public static string ShortNameFor(string typePath)
    {
        string fileName = typePath.Replace('\\', '/').Split('/').LastOrDefault() ?? typePath;
        if (fileName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[..^4];
        }
        int lastDot = fileName.LastIndexOf('.');
        return lastDot >= 0 && lastDot < fileName.Length - 1 ? fileName[(lastDot + 1)..] : fileName;
    }

    /// <summary>Builds the declared signature for a `system\` node.</summary>
    public static NodeSignature FromReflection(string typePath, NodeReflection reflection) => new(
        typePath,
        string.IsNullOrWhiteSpace(reflection.Display?.Text) ? ShortNameFor(typePath) : reflection.Display!.Text,
        reflection.Display?.Category,
        reflection.ControlIns,
        reflection.ControlOuts,
        reflection.DataIns,
        reflection.DataOuts,
        reflection.Stateless,
        SignatureOrigin.Declared);
}
