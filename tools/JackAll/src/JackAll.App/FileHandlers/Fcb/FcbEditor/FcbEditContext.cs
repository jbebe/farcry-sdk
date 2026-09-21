using JackAll.Core.Format.Fcb;

namespace JackAll.App.FileHandlers.Fcb.FcbEditor;

/// <summary>What the views over one tree name its hashes with, and where their edits are reported.</summary>
public sealed class FcbEditContext(IFcbNames? fallback = null)
{
    public FcbClassDefinitions Definitions { get; } = FcbDefinitionsProvider.Value.Value;

    /// <summary>Raised after any write into the tree.</summary>
    public event Action? Edited;

    /// <summary>Raised whenever any field flips between parsing and not.</summary>
    public event Action<ScalarField>? ValidityChanged;

    public string? ClassName(uint hash, FcbClass cls) => FcbNames.ClassNameOf(cls, hash, fallback);

    public FcbMember? Member(FcbClass cls, uint hash, byte[] value) => FcbNames.MemberOf(cls, hash, value, fallback);

    internal void OnEdited() => Edited?.Invoke();

    internal void OnValidityChanged(ScalarField field) => ValidityChanged?.Invoke(field);
}
