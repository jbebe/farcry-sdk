using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using JackAll.Tools.Hkx;
using JackAll.Tools.World;
using JackAll.Tools.Xbg;

namespace JackAll.App.FileHandlers.Hkx;

/// <summary>A dump line, indented by its depth, as the inspector shows it.</summary>
public sealed record HkxInspectorRow(HkxDumpLine Line)
{
    public Thickness Indent => new(Line.Depth * 16, 0, 0, 0);
    public bool IsLink => Line.Target is not null;
    public bool IsPlain => !IsLink;
}

/// <summary>
/// An <c>.hkx</c> in its own tab: its collision drawn over the model beside it, its bodies, shapes and
/// constraints as a tree, and the selected object's saved values.
/// </summary>
public partial class HkxTabView : UserControl
{
    private readonly List<HkxTreeNode> _roots = [];
    private readonly Dictionary<HkxObject, int> _numbers = [];
    private readonly HkxScene _scene;
    private readonly OrbitViewport _orbit;
    private readonly List<XbgSubmesh>? _model;

    public HkxTabView(string fileName, string? path, byte[] content, Func<string, byte[]?> readByPath)
    {
        InitializeComponent();
        (_model, string modelNote) = Model(path, readByPath);
        _scene = new HkxScene(HkxViewport, _model) { ModelOpacity = HkxModelOpacity.Value };
        _orbit = new OrbitViewport(HkxViewport, tilt: true);
        _orbit.Clicked += Pick;
        HkxModelOpacity.ValueChanged += (_, e) => _scene.ModelOpacity = e.NewValue;

        try
        {
            HkxFile file = HkxFile.Parse(content);
            _numbers = HkxDump.Numbers(file);
            _roots.AddRange(HkxShapes.Read(file).Select(n => new HkxTreeNode(n) { IsExpanded = true }));
        }
        catch (Exception ex)
        {
            HkxHeader.Text = $"{path ?? fileName}\nCouldn't read this file: {ex.Message}";
            Frame([], _model);
            return;
        }

        HkxTree.ItemsSource = _roots;
        int bodies = _roots.Sum(r => r.Bodies().Count());
        int shapes = _roots.Sum(r => r.Node.Primitives().Count());
        int constraints = _roots.Sum(r => r.Children.Count(c => c.Node.Kind == HkxNodeKind.Constraint));
        HkxHeader.Text = $"{path ?? fileName}\n{Count(bodies, "body", "bodies")}, {Count(shapes, "collision shape", "collision shapes")}, "
            + $"{Count(constraints, "constraint", "constraints")}. {modelNote}";

        _scene.Show(Shown());
        Frame(Shown(), _model);
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

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        var node = e.NewValue as HkxTreeNode;
        Inspect(node?.Node.Object);
        _scene.Highlight(node is null ? new HashSet<HkxPrimitive>() : [.. node.Node.Primitives()]);
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

    private void Inspect(HkxObject? o)
    {
        HkxSelection.Text = o is null ? "" : $"#{_numbers[o]} {o.Class.Name}";
        HkxInspector.ItemsSource = o is null ? null : HkxDump.Members(o, _numbers).Select(l => new HkxInspectorRow(l)).ToList();
    }
}
