using System.ComponentModel;
using System.Runtime.CompilerServices;
using JackAll.Core.Format.Fcb;

namespace JackAll.App.XmlEditor;

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

    /// <summary>Raised whenever <see cref="IsValid"/> changes - the property row (and, through it, the
    /// tab's global invalid-field count that gates Save) listens to this rather than re-scanning every
    /// field on every keystroke.</summary>
    public event Action<ScalarField>? ValidityChanged;

    public ScalarField(FcbMemberType type, object initialValue, string? label = null)
    {
        Type = type;
        Label = label;
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
