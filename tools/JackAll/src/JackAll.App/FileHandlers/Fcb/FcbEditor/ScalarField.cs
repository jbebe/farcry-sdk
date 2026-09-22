using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using JackAll.App.Picker;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.Fcb;

namespace JackAll.App.FileHandlers.Fcb.FcbEditor;

/// <summary>
/// One editable text-backed leaf — a top-level scalar value (Float/Int*/Hash/Enum/String/BinHex/Rml),
/// or one component of a <see cref="VectorFieldGroup"/> or one item of a
/// <see cref="NumberArrayGroup"/>. Every such field in the whole property grid, no matter how deeply
/// nested, is one of these, so parsing/validation behaves identically everywhere - see
/// <see cref="FcbFieldFormat"/>.
/// </summary>
public sealed class ScalarField : INotifyPropertyChanged
{
    private string _text;
    private bool _isValid;
    private string? _error;
    private object _value;

    public FcbMemberType Type { get; }

    /// <summary>Optional short label for a field shown alongside siblings (a vector's "X"/"Y"/"Z"/"W",
    /// or a matrix cell's row/column) - null for a plain top-level scalar, which needs none.</summary>
    public string? Label { get; }

    /// <summary>Non-null only for a "selXxx" value backed by a sibling "enumXxx" object in the data
    /// or registered by the engine - the ordered option names a dropdown
    /// shows in place of this field's plain <see cref="Text"/> box. Index i's underlying value is
    /// exactly i (see <see cref="SelectedEnumIndex"/>).</summary>
    public IReadOnlyList<string>? EnumChoices { get; }

    /// <summary>Just <c>EnumChoices is not null</c> - a plain bool is easier to trigger a Style
    /// Visibility off than a null comparison against a collection-typed binding.</summary>
    public bool HasEnumChoices => EnumChoices is not null;

    /// <summary>Raised whenever <see cref="IsValid"/> changes - the property row (and, through it, the
    /// tab's global invalid-field count that gates Save) listens to this rather than re-scanning every
    /// field on every keystroke.</summary>
    public event Action<ScalarField>? ValidityChanged;

    /// <summary>Which game file the value names, if any, so the file picker is offered.</summary>
    public FileRef FileRef { get; }

    public bool IsFilePath => FileRef != FileRef.None;

    /// <summary>The file a path hash or sound id names, when anything knows it.</summary>
    public string? ResolvedPath => FileRef switch
    {
        FileRef.PathHash when IsValid && Value is uint hash => FilePicker.PathOf(hash),
        FileRef.SoundId when TryParseSoundId(Text, out uint id) => FilePicker.SoundBankOf(id),
        _ => null,
    };

    public ScalarField(
        FcbMemberType type, object initialValue, string? label = null, IReadOnlyList<string>? enumChoices = null,
        FileRef fileRef = FileRef.None)
    {
        Type = type;
        Label = label;
        EnumChoices = enumChoices;
        FileRef = fileRef;
        _text = FcbFieldFormat.Format(type, initialValue);
        _value = initialValue;
        _isValid = true;
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            // Revalidate() (which updates Value/IsValid) has to run before this fires - a listener
            // reacting to the Text change (PropertyRow.RaiseChanged -> RecomputeChangedFromVanilla,
            // which reads Value via EncodeValue()) would otherwise see the *previous* parsed value,
            // one edit stale, and never get another chance to recompute once typing stops.
            Revalidate();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedEnumIndex));
            OnPropertyChanged(nameof(ResolvedPath));
            if (IsColour)
            {
                OnPropertyChanged(nameof(Swatch));
                OnPropertyChanged(nameof(Red));
                OnPropertyChanged(nameof(Green));
                OnPropertyChanged(nameof(Blue));
            }
        }
    }

    public bool IsValid
    {
        get => _isValid;
        private set
        {
            if (_isValid == value) return;
            _isValid = value;
            OnPropertyChanged();
            ValidityChanged?.Invoke(this);
        }
    }

    public string? Error
    {
        get => _error;
        private set { _error = value; OnPropertyChanged(); }
    }

    /// <summary>The last successfully parsed value - stale (the previous good value) while
    /// <see cref="IsValid"/> is false, since <see cref="FcbValueCodec.Encode"/> is never called on an
    /// invalid field in the first place (Save stays disabled until every field is valid again).</summary>
    public object Value => _value;

    /// <summary>
    /// Two-way dropdown view of <see cref="Text"/> for an <see cref="EnumChoices"/>-backed field - the
    /// plain UInt32 value already *is* the option's index (see <c>FcbNodeViews.FindEnumChoices</c>'s
    /// remarks), so this just routes through the same Text/Revalidate pipeline every other field uses,
    /// formatted/parsed as a plain integer exactly like an ordinary UInt32 box would be.
    /// </summary>
    /// <remarks>
    /// Null (nothing selected) whenever the current value doesn't land on one of the known choices -
    /// happens transiently while <see cref="IsValid"/> is false (an in-progress edit, before the
    /// dropdown replaces free text entry) or if the raw data holds an index past the end of the list
    /// (unseen in practice, but not impossible for a hand-edited or third-party-tool-written value).
    /// Deliberately doesn't invent a fake selection for either case - same as a plain ComboBox's own
    /// null SelectedItem.
    /// </remarks>
    public int? SelectedEnumIndex
    {
        get => IsValid && Value is uint index && index < (uint)(EnumChoices?.Count ?? 0) ? (int)index : null;
        set
        {
            if (value is { } index)
            {
                Text = index.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>A Vector3 that is an RGB colour from 0 to 1, shown with a swatch and channel sliders.</summary>
    public bool IsColour { get; init; }

    public System.Windows.Media.Brush? Swatch
        => IsColour && Value is float[] c
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(Byte(c[0]), Byte(c[1]), Byte(c[2])))
            : null;

    public double Red { get => Channel(0); set => SetChannel(0, value); }

    public double Green { get => Channel(1); set => SetChannel(1, value); }

    public double Blue { get => Channel(2); set => SetChannel(2, value); }

    private double Channel(int index) => Value is float[] c ? c[index] : 0;

    /// <summary>Goes through <see cref="Text"/>, so a slider edit is an ordinary edit.</summary>
    private void SetChannel(int index, double value)
    {
        if (Value is float[] c)
        {
            float[] next = (float[])c.Clone();
            next[index] = (float)Math.Round(value, 3);
            Text = FcbFieldFormat.Format(Type, next);
        }
    }

    private static byte Byte(float channel) => (byte)Math.Clamp(channel * 255f, 0f, 255f);

    /// <summary>A sound id as the data writes it: <c>0x</c> and eight uppercase hex digits.</summary>
    public static string SoundIdText(uint id) => $"0x{id:X8}";

    private static bool TryParseSoundId(string text, out uint id)
    {
        text = text.Trim();
        return uint.TryParse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text,
            NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
    }

    private void Revalidate()
    {
        if (FcbFieldFormat.TryParse(Type, _text, out object parsed, out string? error))
        {
            _value = parsed;
            Error = null;
            IsValid = true;
        }
        else
        {
            Error = error;
            IsValid = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>What game file a field's value names.</summary>
public enum FileRef
{
    None,

    /// <summary>A String holding an archive path.</summary>
    Path,

    /// <summary>A Hash holding an archive path's CRC32.</summary>
    PathHash,

    /// <summary>A String holding a sound id, which names <c>soundbinary\&lt;id:08x&gt;.spk</c>.</summary>
    SoundId,
}

/// <summary>A plain Bool/Bool16/Bool32 leaf - no text parsing, so no invalid state is possible; a
/// checkbox is always either checked or not.</summary>
public sealed class BoolField(bool initialValue) : INotifyPropertyChanged
{
    private bool _value = initialValue;

    public bool Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
