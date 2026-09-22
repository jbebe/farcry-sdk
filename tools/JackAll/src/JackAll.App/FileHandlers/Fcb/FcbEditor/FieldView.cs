using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.App.FileHandlers.Fcb.FcbEditor;

/// <summary>
/// One field of a merged node: its editor, which side its value comes from, and the two ways back -
/// Revert drops an override so the base shows through, Restore returns to the document's baseline.
/// </summary>
public sealed class FieldView(PropertyRow row, FieldOrigin origin) : Observable
{
    private FieldOrigin _origin = origin;
    private Action? _revert;

    public PropertyRow Row { get; } = row;

    public FieldOrigin Origin
    {
        get => _origin;
        private set
        {
            if (Set(ref _origin, value))
            {
                OnPropertyChanged(nameof(CanRevert));
            }
        }
    }

    public bool CanRevert => _origin == FieldOrigin.Overridden;

    public void Revert() => _revert?.Invoke();

    /// <summary>
    /// A field that writes into <paramref name="node"/> on every valid edit. One inherited when
    /// opened stops being an override once set back to the base's value.
    /// </summary>
    internal static FieldView Bound(
        MergedNode node, MergedField field, FcbClass cls, byte[]? baseline,
        IReadOnlyList<string>? choices, FcbEditContext context)
    {
        FcbMember? member = context.Member(cls, field.Hash, field.Value);
        PropertyRow row = PropertyRow.Build(
            field.Hash, member?.Name, member?.Type ?? FcbMemberType.BinHex, field.Value, baseline, choices, FileRefOf(member));
        var view = new FieldView(row, field.Origin);
        bool wasInherited = field.Origin == FieldOrigin.Inherited;

        row.FieldValidityChanged += context.OnValidityChanged;
        row.Changed += () =>
        {
            if (!row.IsRowValid)
            {
                return;
            }
            byte[] value = row.EncodeValue();
            if (wasInherited && field.BaseValue is { } inherited && inherited.AsSpan().SequenceEqual(value))
            {
                node.Revert(field.Hash);
                view.Origin = FieldOrigin.Inherited;
            }
            else
            {
                node.SetValue(field.Hash, value);
                view.Origin = field.BaseValue is null ? FieldOrigin.InstanceOnly : FieldOrigin.Overridden;
            }
            context.OnEdited();
        };
        view._revert = () =>
        {
            wasInherited = true;
            node.Revert(field.Hash);
            view.Origin = FieldOrigin.Inherited;
            row.Show(field.BaseValue!);
            context.OnEdited();
        };
        return view;
    }

    /// <summary>
    /// What file a member names. A sound id is a String the engine reads as an unsigned int, or one
    /// named <c>snd…</c>/<c>dissnd…</c> by the original editor's convention.
    /// </summary>
    private static FileRef FileRefOf(FcbMember? member)
        => member is null ? FileRef.None
            : member.IsPath ? FileRef.PathHash
            : member.Cpp == "unsigned_int" || member.HasPrefix("snd") || member.HasPrefix("dissnd") ? FileRef.SoundId
            : FileRef.None;
}
