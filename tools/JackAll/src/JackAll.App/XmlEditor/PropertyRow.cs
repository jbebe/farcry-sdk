using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using JackAll.Core.Format.Fcb;

namespace JackAll.App.XmlEditor;

/// <summary>A Matrix4 leaf - a fixed 4x4 grid of Float <see cref="ScalarField"/>s, labeled R#C#. Vectors
/// (Vector2/3/4) don't use this - they're a single comma-separated <see cref="ScalarField"/> instead,
/// same as any other scalar (see <see cref="FcbFieldFormat"/>'s Vector2/3/4 cases); only Matrix4 is
/// still spread across individually-labeled boxes, since a 16-number single field would be unreadable.</summary>
public sealed class VectorFieldGroup
{
    public IReadOnlyList<ScalarField> Components { get; }

    private VectorFieldGroup(IReadOnlyList<ScalarField> components) => Components = components;

    /// <summary><paramref name="values"/> is 16 floats, row-major (see <see cref="FcbValueCodec"/>).</summary>
    public static VectorFieldGroup ForMatrix(float[] values)
    {
        var fields = new ScalarField[16];
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                int i = (row * 4) + col;
                fields[i] = new ScalarField(FcbMemberType.Float, values[i], $"R{row}C{col}");
            }
        }
        return new VectorFieldGroup(fields);
    }

    public float[] ToArray() => [.. Components.Select(c => (float)c.Value)];
}

/// <summary>UInt32Array/HashArray/Int32Array/FloatArray - a resizable list of scalar items, all the
/// same <see cref="ItemType"/>.</summary>
public sealed class NumberArrayGroup(FcbMemberType itemType)
{
    public FcbMemberType ItemType { get; } = itemType;
    public ObservableCollection<ScalarField> Items { get; } = [];

    /// <summary>Raised on add/remove - not on editing an existing item's text, which is that item's
    /// own <see cref="ScalarField"/> notifying separately (see <see cref="PropertyRow.Wire"/>).</summary>
    public event Action? Changed;

    public void AddItem()
    {
        Items.Add(new ScalarField(ItemType, FcbFieldFormat.DefaultValue(ItemType)));
        Changed?.Invoke();
    }

    public void RemoveItem(ScalarField item)
    {
        if (Items.Remove(item))
        {
            Changed?.Invoke();
        }
    }
}

/// <summary>Bool32Array - a resizable list of checkboxes.</summary>
public sealed class BoolArrayGroup
{
    public ObservableCollection<BoolField> Items { get; } = [];
    public event Action? Changed;

    public void AddItem()
    {
        Items.Add(new BoolField(false));
        Changed?.Invoke();
    }

    public void RemoveItem(BoolField item)
    {
        if (Items.Remove(item))
        {
            Changed?.Invoke();
        }
    }
}

/// <summary>Vector3Array - a resizable list of Vector3 items, each one a single comma-separated
/// <see cref="ScalarField"/> (same as a top-level Vector3 value - see <see cref="FcbFieldFormat"/>).</summary>
public sealed class VectorArrayGroup
{
    public ObservableCollection<ScalarField> Items { get; } = [];
    public event Action? Changed;

    public void AddItem()
    {
        Items.Add(new ScalarField(FcbMemberType.Vector3, FcbFieldFormat.DefaultValue(FcbMemberType.Vector3)));
        Changed?.Invoke();
    }

    public void RemoveItem(ScalarField item)
    {
        if (Items.Remove(item))
        {
            Changed?.Invoke();
        }
    }
}

/// <summary>
/// One row in the property grid - one entry of an <see cref="FcbObject"/>'s <see cref="FcbObject.Values"/>.
/// Exactly one of <see cref="Scalar"/>/<see cref="Bool"/>/<see cref="Vector"/>/<see cref="NumberArray"/>/
/// <see cref="BoolArray"/>/<see cref="VectorArray"/> is non-null, matching <see cref="Type"/>'s shape -
/// <see cref="Editor"/> exposes whichever one it is for the view's implicit-DataTemplate-by-type binding.
/// </summary>
public sealed class PropertyRow : INotifyPropertyChanged
{
    public uint NameHash { get; }
    public string? Name { get; }
    public FcbMemberType Type { get; }

    /// <summary>The vanilla bytes for this same value, or null when there's nothing to compare
    /// against (a mod-added container - see <c>GameVfs.ReadOriginalFragment</c>) or this field simply
    /// didn't exist in the vanilla shape. Null means "shown like base content": never highlighted,
    /// never restorable, regardless of what the current value is.</summary>
    private readonly byte[]? _originalBytes;

    public string DisplayName => Name ?? $"hash {NameHash:X8}";

    public ScalarField? Scalar { get; private set; }
    public BoolField? Bool { get; private set; }
    public VectorFieldGroup? Vector { get; private set; }
    public NumberArrayGroup? NumberArray { get; private set; }
    public BoolArrayGroup? BoolArray { get; private set; }
    public VectorArrayGroup? VectorArray { get; private set; }

    public object Editor => (object?)Scalar ?? (object?)Bool ?? (object?)Vector ?? (object?)NumberArray ?? (object?)BoolArray ?? (object?)VectorArray
        ?? throw new InvalidOperationException("PropertyRow was never wired to an editor.");

    private bool _isChangedFromVanilla;

    /// <summary>True when this row's current value differs from <see cref="_originalBytes"/> - drives
    /// the property grid's highlight and the "Restore" button. Recomputed on every edit
    /// (<see cref="RaiseChanged"/>), so typing a value back to its original clears this immediately,
    /// not just at save.</summary>
    public bool IsChangedFromVanilla
    {
        get => _isChangedFromVanilla;
        private set
        {
            if (_isChangedFromVanilla == value) return;
            _isChangedFromVanilla = value;
            OnPropertyChanged();
            ChangedFromVanillaFlagChanged?.Invoke(this);
        }
    }

    /// <summary>Raised on any edit anywhere in this row - a keystroke, a checkbox toggle, an array
    /// add/remove - so the tab view model can mark itself dirty without polling.</summary>
    public event Action? Changed;

    /// <summary>Raised whenever any <see cref="ScalarField"/> in this row flips valid/invalid - what
    /// the tab view model's Save-blocking invalid-field count is built from.</summary>
    public event Action<ScalarField>? FieldValidityChanged;

    /// <summary>Raised whenever <see cref="IsChangedFromVanilla"/> flips - what the owning
    /// <see cref="FcbObjectNodeView"/>'s tree-wide "contains a change" indicator is built from. Not
    /// raised for this row's own initial state (nothing subscribes until after <see cref="Build"/>
    /// returns), only for edits after that.</summary>
    public event Action<PropertyRow>? ChangedFromVanillaFlagChanged;

    private PropertyRow(uint nameHash, string? name, FcbMemberType type, byte[]? originalBytes)
    {
        NameHash = nameHash;
        Name = name;
        Type = type;
        _originalBytes = originalBytes;
    }

    /// <summary>
    /// Builds the row for one value, decoding via <see cref="FcbValueCodec"/>. Falls back to a raw hex
    /// editor (<see cref="FcbMemberType.BinHex"/>) when <paramref name="declaredType"/> doesn't
    /// actually match <paramref name="rawBytes"/>'s shape - the same "config says one thing, the bytes
    /// say another" case <see cref="FcbXml.WriteValueEntry"/> falls back on, so a slightly-wrong
    /// binary_classes.xml entry never blocks editing a real value, it just shows the bytes plainly.
    /// </summary>
    public static PropertyRow Build(
        uint nameHash, string? name, FcbMemberType declaredType, byte[] rawBytes, byte[]? originalBytes)
    {
        FcbMemberType type = declaredType != FcbMemberType.BinHex && FcbValueCodec.TryDecode(declaredType, rawBytes, out _)
            ? declaredType
            : FcbMemberType.BinHex;

        FcbValueCodec.TryDecode(type, rawBytes, out object decoded);

        var row = new PropertyRow(nameHash, name, type, originalBytes);
        switch (decoded)
        {
            case bool b:
                row.Bool = new BoolField(b);
                break;
            case float[] vec when type is FcbMemberType.Vector2 or FcbMemberType.Vector3 or FcbMemberType.Vector4:
                row.Scalar = new ScalarField(type, vec);
                break;
            case float[] mat when type == FcbMemberType.Matrix4:
                row.Vector = VectorFieldGroup.ForMatrix(mat);
                break;
            case uint[] nums when type is FcbMemberType.UInt32Array or FcbMemberType.HashArray:
                row.NumberArray = FillNumberArray(type is FcbMemberType.HashArray ? FcbMemberType.Hash : FcbMemberType.UInt32, nums.Select(n => (object)n));
                break;
            case int[] nums:
                row.NumberArray = FillNumberArray(FcbMemberType.Int32, nums.Select(n => (object)n));
                break;
            case float[] nums:
                row.NumberArray = FillNumberArray(FcbMemberType.Float, nums.Select(n => (object)n));
                break;
            case bool[] bools:
                row.BoolArray = new BoolArrayGroup();
                foreach (bool value in bools)
                {
                    row.BoolArray.Items.Add(new BoolField(value));
                }
                break;
            case float[][] vectors:
                row.VectorArray = new VectorArrayGroup();
                foreach (float[] v in vectors)
                {
                    row.VectorArray.Items.Add(new ScalarField(FcbMemberType.Vector3, v));
                }
                break;
            default:
                // String, Hash, Enum, every plain integer/Float, BinHex/Rml.
                row.Scalar = new ScalarField(type, decoded);
                break;
        }

        row.Wire();
        // Silent - nothing has subscribed to ChangedFromVanillaFlagChanged yet, so this seeds
        // IsChangedFromVanilla without notifying anyone. FcbObjectNodeView already accounted for this
        // row's initial state via its own cheap byte-level pass (see FcbObjectNodeView.CountOwnChanges);
        // this call is what keeps IsChangedFromVanilla itself correct from the moment the row exists.
        row.RecomputeChangedFromVanilla();
        return row;

        NumberArrayGroup FillNumberArray(FcbMemberType itemType, IEnumerable<object> values)
        {
            var group = new NumberArrayGroup(itemType);
            foreach (object value in values)
            {
                group.Items.Add(new ScalarField(itemType, value));
            }
            return group;
        }
    }

    /// <summary>Hooks every constituent field/group's own change notifications into this row's
    /// <see cref="Changed"/>/<see cref="FieldValidityChanged"/> - called once, right after whichever
    /// editor <see cref="Build"/> chose is fully populated, so newly-added array items (which carry
    /// their own fresh <see cref="ScalarField"/>) get wired too.</summary>
    private void Wire()
    {
        switch (Editor)
        {
            case ScalarField scalar:
                WireScalar(scalar);
                break;
            case BoolField boolField:
                boolField.PropertyChanged += (_, _) => RaiseChanged();
                break;
            case VectorFieldGroup vector:
                foreach (ScalarField component in vector.Components)
                {
                    WireScalar(component);
                }
                break;
            case NumberArrayGroup numbers:
                numbers.Changed += RaiseChanged;
                numbers.Items.CollectionChanged += (_, e) => WireNewItems<ScalarField>(e, WireScalar);
                foreach (ScalarField item in numbers.Items)
                {
                    WireScalar(item);
                }
                break;
            case BoolArrayGroup bools:
                bools.Changed += RaiseChanged;
                bools.Items.CollectionChanged += (_, e) => WireNewItems(e, (BoolField f) => f.PropertyChanged += (_, _) => RaiseChanged());
                foreach (BoolField item in bools.Items)
                {
                    item.PropertyChanged += (_, _) => RaiseChanged();
                }
                break;
            case VectorArrayGroup vectors:
                vectors.Changed += RaiseChanged;
                vectors.Items.CollectionChanged += (_, e) => WireNewItems<ScalarField>(e, WireScalar);
                foreach (ScalarField item in vectors.Items)
                {
                    WireScalar(item);
                }
                break;
        }
    }

    private void WireScalar(ScalarField field)
    {
        field.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScalarField.Text))
            {
                RaiseChanged();
            }
        };
        field.ValidityChanged += f => FieldValidityChanged?.Invoke(f);
    }

    private static void WireNewItems<T>(NotifyCollectionChangedEventArgs e, Action<T> wire)
    {
        if (e.NewItems is null) return;
        foreach (T item in e.NewItems)
        {
            wire(item);
        }
    }

    private void RaiseChanged()
    {
        Changed?.Invoke();
        RecomputeChangedFromVanilla();
    }

    /// <summary>True when every field in this row currently parses (arrays: every item) - the same
    /// check <c>XmlEditorTabViewModel</c> aggregates across the whole tab to gate Save.</summary>
    private bool IsRowValid => Editor switch
    {
        ScalarField scalar => scalar.IsValid,
        BoolField => true,
        VectorFieldGroup matrix => matrix.Components.All(c => c.IsValid),
        NumberArrayGroup numbers => numbers.Items.All(f => f.IsValid),
        BoolArrayGroup => true,
        VectorArrayGroup vectors => vectors.Items.All(f => f.IsValid),
        _ => false,
    };

    private void RecomputeChangedFromVanilla()
    {
        if (_originalBytes is null)
        {
            IsChangedFromVanilla = false; // nothing to compare against - "shown like base content"
            return;
        }

        // An invalid edit can't be encoded to compare byte-for-byte, but it's definitely not still the
        // original value either - treat it as changed rather than silently reporting "unchanged".
        IsChangedFromVanilla = !IsRowValid || !_originalBytes.AsSpan().SequenceEqual(EncodeValue());
    }

    /// <summary>Sets this row back to its vanilla value - only meaningful while
    /// <see cref="IsChangedFromVanilla"/> is true (there's nothing to restore otherwise). Setting
    /// individual fields' own <c>Text</c>/<c>Value</c> already raises every event needed to update
    /// validity/dirty state; the array cases replace <c>Items</c> directly (a restore can change an
    /// array's length, which no single item's own change can) and so call <see cref="RaiseChanged"/>
    /// explicitly at the end, since nothing else would.</summary>
    public void RestoreOriginal()
    {
        if (_originalBytes is null || !FcbValueCodec.TryDecode(Type, _originalBytes, out object original))
        {
            return;
        }

        switch (Editor)
        {
            case ScalarField scalar:
                scalar.Text = FcbFieldFormat.Format(Type, original);
                break;

            case BoolField boolField:
                boolField.Value = (bool)original;
                break;

            case VectorFieldGroup matrix:
                float[] cells = (float[])original;
                for (int i = 0; i < matrix.Components.Count; i++)
                {
                    matrix.Components[i].Text = FcbFieldFormat.Format(FcbMemberType.Float, cells[i]);
                }
                break;

            case NumberArrayGroup numbers:
                ReplaceItems(numbers.Items, OriginalArrayValues(original), v => new ScalarField(numbers.ItemType, v));
                RaiseChanged();
                break;

            case BoolArrayGroup bools:
                ReplaceItems(bools.Items, ((bool[])original).Cast<object>(), v => new BoolField((bool)v));
                RaiseChanged();
                break;

            case VectorArrayGroup vectors:
                ReplaceItems(vectors.Items, ((float[][])original).Cast<object>(), v => new ScalarField(FcbMemberType.Vector3, v));
                RaiseChanged();
                break;
        }
    }

    private static IEnumerable<object> OriginalArrayValues(object decoded) => decoded switch
    {
        uint[] values => values.Cast<object>(),
        int[] values => values.Cast<object>(),
        float[] values => values.Cast<object>(),
        _ => throw new InvalidOperationException("Unreachable."),
    };

    private static void ReplaceItems<T>(ObservableCollection<T> items, IEnumerable<object> values, Func<object, T> makeItem)
    {
        items.Clear();
        foreach (object value in values)
        {
            items.Add(makeItem(value));
        }
    }

    /// <summary>Packs this row's current (already-validated) value back to raw bytes for
    /// <see cref="FcbObject.Values"/> - only ever called once every field across the whole tab is
    /// confirmed valid (see <c>XmlEditorTabViewModel.SaveAsync</c>).</summary>
    public byte[] EncodeValue() => Editor switch
    {
        ScalarField scalar => FcbValueCodec.Encode(Type, scalar.Value),
        BoolField boolField => FcbValueCodec.Encode(Type, boolField.Value),
        VectorFieldGroup matrix => FcbValueCodec.Encode(Type, matrix.ToArray()),
        NumberArrayGroup numbers when Type == FcbMemberType.Int32Array
            => FcbValueCodec.Encode(Type, numbers.Items.Select(f => (int)f.Value).ToArray()),
        NumberArrayGroup numbers when Type == FcbMemberType.FloatArray
            => FcbValueCodec.Encode(Type, numbers.Items.Select(f => (float)f.Value).ToArray()),
        NumberArrayGroup numbers
            => FcbValueCodec.Encode(Type, numbers.Items.Select(f => (uint)f.Value).ToArray()),
        BoolArrayGroup bools => FcbValueCodec.Encode(Type, bools.Items.Select(f => f.Value).ToArray()),
        VectorArrayGroup vectors => FcbValueCodec.Encode(Type, vectors.Items.Select(f => (float[])f.Value).ToArray()),
        _ => throw new InvalidOperationException("Unreachable."),
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
