using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using JackAll.App.FileHandlers.Fcb.FcbEditor;
using JackAll.Tools.Hkx;
using JackAll.Tools.World;
using JackAll.Tools.Xbg;

namespace JackAll.App.FileHandlers.Hkx;

/// <summary>A dump line, indented by its depth, as the inspector shows it: as text, a link, or an editor.</summary>
public sealed record HkxInspectorRow(HkxDumpLine Line, PropertyRow? Row)
{
    public Thickness Indent => new(Line.Depth * 16, 0, 0, 0);
    public object? Editor => Row?.Editor;
    public bool IsEditable => Row is not null;
    public bool IsLink => Line.Target is not null;
    public bool IsPlain => !IsLink && !IsEditable;
}

/// <summary>
/// An <c>.hkx</c> in its own tab: its collision drawn over the model beside it, its bodies, shapes and
/// constraints as a tree, and the selected object's saved values, which can be edited, undone and saved.
/// </summary>
public partial class HkxTabView : UserControl, ISavableTab
{
    private readonly HkxEditSession? _session;
    private readonly Func<byte[], bool> _store;
    private readonly HkxScene _scene;
    private readonly OrbitViewport _orbit;
    private readonly List<XbgSubmesh>? _model;
    private readonly string _modelNote;
    private List<HkxTreeNode> _roots = [];
    private Dictionary<HkxObject, int> _numbers = [];
    private HkxObject? _inspected;
    private bool _writing;

    public HkxTabView(string fileName, string? path, byte[] content, Func<string, byte[]?> readByPath, Func<byte[], bool> store)
    {
        InitializeComponent();
        Title = path ?? fileName;
        _store = store;
        (_model, _modelNote) = Model(path, readByPath);
        _scene = new HkxScene(HkxViewport, _model) { ModelOpacity = HkxModelOpacity.Value };
        _orbit = new OrbitViewport(HkxViewport, tilt: true);
        _orbit.Clicked += Pick;
        _gizmo = new HkxGizmo(HkxGizmos);
        StartGizmo();
        HkxModelOpacity.ValueChanged += (_, e) => _scene.ModelOpacity = e.NewValue;

        try
        {
            _session = new HkxEditSession(content);
        }
        catch (Exception ex)
        {
            HkxHeader.Text = $"{Title}\nCouldn't read this file: {ex.Message}";
            Frame([], _model);
            return;
        }

        _session.Changed += Rebuild;
        _session.History.Changed += RefreshButtons;
        Rebuild(reparsed: true);
        Frame(Shown(), _model);
    }

    public string Title { get; }

    public bool IsDirty => _session?.IsDirty ?? false;

    public event Action? DirtyChanged;

    /// <summary>Saves the edits to the workspace; the text is why they were not, or null.</summary>
    public string? Save()
    {
        try
        {
            return _session is null || _session.Save(_store) ? null : "The edits were not saved.";
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    public Task<string?> SaveAsync() => Task.FromResult(Save());

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.S)
        {
            SaveOrWarn();
            e.Handled = true;
        }
        else if (GizmoKey(e.Key))
        {
            e.Handled = true;
        }
        // A text box keeps its own Ctrl+Z.
        else if (_session is not null && Keyboard.FocusedElement is not TextBox && UndoChrome.IsUndoKey(e.Key, out bool redo))
        {
            if (redo)
            {
                _session.History.Redo();
            }
            else
            {
                _session.History.Undo();
            }
            e.Handled = true;
        }
    }

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    /// <summary>The model filed beside the <c>.hkx</c> under the same name, at its finest level of detail.</summary>
    private static (List<XbgSubmesh>? Model, string Note) Model(string? path, Func<string, byte[]?> readByPath)
    {
        string? modelPath = path is null ? null : Path.ChangeExtension(path, ".xbg");
        if (modelPath is null || readByPath(modelPath) is not { } bytes)
        {
            return (null, "No model beside it.");
        }
        try
        {
            XbgModel model = WorldModels.Triangulate(modelPath, bytes);
            return (WorldModels.SubmeshesAt(model, model.LodLevels[0]), $"Drawn over {Path.GetFileName(modelPath)}.");
        }
        catch (Exception ex)
        {
            return (null, $"Couldn't read {Path.GetFileName(modelPath)}: {ex.Message}");
        }
    }

    // Builds the tree, scene and header again from the file. After a fresh parse every object is new, so what was
    // expanded and inspected is carried over by object number; otherwise by the objects themselves.
    private void Rebuild(bool reparsed)
    {
        HkxFile file = _session!.File;
        Dictionary<HkxObject, int> numbers = _numbers;
        HkxObject? inspected = _inspected;
        HashSet<string> shown = [.. _roots.SelectMany(r => r.Bodies()).Where(b => b.IsShown).Select(b => b.Node.Label)];
        bool first = _roots.Count == 0;

        _numbers = HkxDump.Numbers(file);
        Dictionary<int, HkxObject>? byNumber = reparsed ? _numbers.ToDictionary(p => p.Value, p => p.Key) : null;
        HkxObject? Carry(HkxObject? o) => o is null || byNumber is null ? o : numbers.TryGetValue(o, out int n) ? byNumber.GetValueOrDefault(n) : null;
        HashSet<HkxObject> open = [.. All(_roots).Where(n => n.IsExpanded).Select(n => Carry(n.Node.Object)).OfType<HkxObject>()];

        _roots = [.. HkxShapes.Read(file).Select(n => new HkxTreeNode(n))];
        foreach (HkxTreeNode node in All(_roots))
        {
            node.IsExpanded = first ? node.Node.Kind == HkxNodeKind.Variant : node.Node.Object is { } o && open.Contains(o);
            if (!first && node.IsBody)
            {
                node.IsShown = shown.Contains(node.Node.Label);
            }
        }
        HkxTree.ItemsSource = _roots;

        int bodies = _roots.Sum(r => r.Bodies().Count());
        int shapes = _roots.Sum(r => r.Node.Primitives().Count());
        int constraints = _roots.Sum(r => r.Children.Count(c => c.Node.Kind == HkxNodeKind.Constraint));
        HkxHeader.Text = $"{Title}\n{Count(bodies, "body", "bodies")}, {Count(shapes, "collision shape", "collision shapes")}, "
            + $"{Count(constraints, "constraint", "constraints")}. {_modelNote}";
        _scene.Show(Shown());

        HkxObject? again = Carry(inspected);
        if (again is null || HkxTreeNode.Reveal(_roots, n => n.Node.Object == again) is null)
        {
            Target(null);
            Inspect(again);
        }
        DirtyChanged?.Invoke();
        RefreshButtons();
    }

    private static IEnumerable<HkxTreeNode> All(IEnumerable<HkxTreeNode> nodes) => nodes.SelectMany(n => All(n.Children).Prepend(n));

    private IEnumerable<HkxPrimitive> Shown()
        => _roots.SelectMany(r => r.Bodies()).Where(b => b.IsShown).SelectMany(b => b.Node.Primitives());

    private void Frame(IEnumerable<HkxPrimitive> primitives, IReadOnlyList<XbgSubmesh>? model)
    {
        List<Vector3> points = [.. primitives.SelectMany(p => p.ModelVertices())];
        if (model is { Count: > 0 })
        {
            (Vector3 min, Vector3 max) = XbgModel.Bounds(model);
            points.AddRange([min, max]);
        }
        if (points.Count > 0)
        {
            (Vector3 lo, Vector3 hi) = XbgModel.Bounds(points);
            _orbit.Frame(lo, hi);
        }
    }

    private void Pick(Point at)
    {
        if (_scene.PrimitiveAt(at) is { } picked)
        {
            HkxTreeNode.Reveal(_roots, n => n.Node.Primitive == picked);
        }
    }

    private void RefreshButtons()
    {
        UndoChrome.Refresh(HkxUndo, HkxRedo, _session!.History);
        HkxSave.IsEnabled = IsDirty;
    }

    private void SaveOrWarn()
    {
        if (Save() is { } error)
        {
            MessageBox.Show(Window.GetWindow(this), error, "Not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => _session?.History.Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => _session?.History.Redo();

    private void Save_Click(object sender, RoutedEventArgs e) => SaveOrWarn();

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        var node = e.NewValue as HkxTreeNode;
        _scene.Highlight(node is null ? new HashSet<HkxPrimitive>() : [.. node.Node.Primitives()]);
        Target(node);
        Inspect(node?.Node.Object);
    }

    private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HkxTree.SelectedItem is HkxTreeNode node)
        {
            Frame(node.Node.Primitives(), null);
        }
    }

    private void Shown_Click(object sender, RoutedEventArgs e) => _scene.Show(Shown());

    private void ResetView_Click(object sender, RoutedEventArgs e) => Frame(Shown(), _model);

    private void Target_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Hyperlink)?.DataContext is HkxInspectorRow { Line.Target: { } target }
            && HkxTreeNode.Reveal(_roots, n => n.Node.Object == target) is null)
        {
            Inspect(target);
        }
    }

    // Lists an object's values, unless the inspector is writing one, whose rows and focus then stay.
    private void Inspect(HkxObject? o)
    {
        if (_writing)
        {
            return;
        }
        _inspected = o;
        HkxSelection.Text = o is null ? "" : $"#{_numbers[o]} {o.Class.Name}";
        HkxInspector.ItemsSource = o is null ? null
            : HkxDump.Members(o, _numbers).Select(line => new HkxInspectorRow(line, HkxFields.For(o, line, bytes => Write(o, line, bytes)))).ToList();
    }

    private void Write(HkxObject o, HkxDumpLine line, byte[] bytes)
    {
        _writing = true;
        try
        {
            _session!.Edit($"set {line.Path}", () => o.SetRaw(line.FullPath, bytes), mergeKey: $"#{_numbers[o]} {line.FullPath}");
        }
        finally
        {
            _writing = false;
        }
    }
}
