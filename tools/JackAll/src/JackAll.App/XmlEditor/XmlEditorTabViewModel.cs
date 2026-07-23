using System.ComponentModel;
using System.Runtime.CompilerServices;
using JackAll.Core.Format.Fcb;

namespace JackAll.App.XmlEditor;

/// <summary>
/// One open editor tab: a structured property-grid view over one <see cref="FcbObject"/>, edited
/// natively (typed fields, not text). What "saving" actually means - render to XML and stage into a
/// mod fragment's workspace overlay, or serialize straight to binary and write a real `.sav` file back
/// to disk - is entirely up to <see cref="_persist"/>, supplied by the caller; this class only owns the
/// parts genuinely shared by both (dirty/validity/busy tracking, committing edited rows into the tree).
/// Nothing is persisted until <see cref="SaveAsync"/> runs; editing is purely local to this tab.
/// </summary>
public sealed class XmlEditorTabViewModel : INotifyPropertyChanged
{
    private readonly FcbObject _root;
    private readonly Func<FcbObject, Task<string?>> _persist;

    private FcbObjectNodeView? _selectedNode;
    private string _filterText = "";
    private bool _isDirty;
    private bool _isBusy;
    private int _invalidFieldCount;

    public string Title { get; }

    /// <summary>The fragment's own <c>VfsFile.Hash</c> — what the host window keys open tabs by. 0 for
    /// a save (see <see cref="_openSaveEditors"/> in <c>MainWindow.xaml.cs</c>, keyed by path instead).</summary>
    public uint Hash { get; }

    public FcbObjectNodeView Root { get; }
    public IEnumerable<FcbObjectNodeView> RootItems => [Root];

    /// <param name="currentXml">The document's current content - already-staged edits included, if
    /// any.</param>
    /// <param name="vanillaXml">The document's true vanilla content, ignoring mods/workspace, or null
    /// when there's nothing to compare against (a mod-added container, or a save - saves have no
    /// "vanilla" counterpart at all) - see <c>GameVfs.ReadOriginalFragment</c>. Drives the "differs
    /// from vanilla" highlight/restore, kept separate from <paramref name="currentXml"/> since
    /// reopening an already-edited fragment must still diff against the true original, not against
    /// whatever was already staged.</param>
    /// <param name="persist">Turns the current (possibly just-edited) tree into persisted bytes and
    /// puts them wherever they belong, returning null on success or an error message on failure -
    /// see <see cref="SaveAsync"/>.</param>
    /// <param name="useSaveGameNameHarvest">True for the Saves tab's tree: recovers names/types
    /// <paramref name="defs"/> alone can't resolve, that only exist because <c>SaveGameXmlRenderer</c>
    /// baked them into <paramref name="currentXml"/>'s own attributes via save-specific dictionaries
    /// (see <see cref="FcbXmlNameHarvest"/>'s remarks). False for an ordinary fragment, where every name
    /// already came from <paramref name="defs"/> in the first place, so harvesting again would just be
    /// a wasted re-parse that finds nothing new.</param>
    public XmlEditorTabViewModel(
        string title, uint hash, string currentXml, string? vanillaXml, FcbClassDefinitions defs,
        Func<FcbObject, Task<string?>> persist, bool useSaveGameNameHarvest = false)
    {
        Title = title;
        Hash = hash;
        _persist = persist;
        _root = FcbXml.FromXml(currentXml);
        FcbObject? original = vanillaXml is not null ? FcbXml.FromXml(vanillaXml) : null;

        var extraNames = useSaveGameNameHarvest ? FcbXmlNameHarvest.Harvest(currentXml) : null;
        Root = FcbObjectNodeView.Build(_root, original, defs, extraNames);
    }

    public string FilterText
    {
        get => _filterText;
        set { _filterText = value; OnPropertyChanged(); FcbObjectNodeView.ApplyFilter(Root, value.Trim()); }
    }

    /// <summary>The tree's current selection. Setting this wires the node's rows for dirty/validity
    /// tracking the first time it's selected (see <see cref="FcbObjectNodeView.RowsBuilt"/>) — after
    /// that, wiring is already in place and this is just a plain property change.</summary>
    public FcbObjectNodeView? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (_selectedNode == value) return;
            _selectedNode = value;
            if (value is not null && !value.RowsBuilt)
            {
                foreach (PropertyRow row in value.Rows)
                {
                    row.Changed += OnAnyRowChanged;
                    row.FieldValidityChanged += OnFieldValidityChanged;
                }
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedRows));
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public IReadOnlyList<PropertyRow>? SelectedRows => _selectedNode?.Rows;
    public bool HasSelection => _selectedNode is not null;

    private void OnAnyRowChanged() => IsDirty = true;

    private void OnFieldValidityChanged(ScalarField field)
    {
        InvalidFieldCount += field.IsValid ? -1 : 1;
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanSave)); }
    }

    public int InvalidFieldCount
    {
        get => _invalidFieldCount;
        private set
        {
            if (_invalidFieldCount == value) return;
            _invalidFieldCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSave));
        }
    }

    /// <summary>Gates the Save button — dirty, every touched field currently valid, and not already
    /// mid-save.</summary>
    public bool CanSave => IsDirty && InvalidFieldCount == 0 && !IsBusy;

    /// <summary>
    /// Commits every touched node's rows back into the live <see cref="FcbObject"/> tree, then hands it
    /// to <see cref="_persist"/> to turn into bytes and put wherever they belong. Returns null on
    /// success, or an error message on failure — nothing is committed as "saved" (<see cref="IsDirty"/>
    /// stays true) in that case.
    /// </summary>
    public async Task<string?> SaveAsync()
    {
        if (!CanSave) return null;

        IsBusy = true;
        try
        {
            FcbObjectNodeView.CommitAllRows(Root);

            string? error = await _persist(_root);
            if (error is not null)
            {
                return error;
            }

            IsDirty = false;
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
