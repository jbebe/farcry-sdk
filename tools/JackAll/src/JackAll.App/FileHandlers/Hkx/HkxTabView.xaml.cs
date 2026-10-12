using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using JackAll.Tools.Hkx;

namespace JackAll.App.FileHandlers.Hkx;

/// <summary>A dump line, indented by its depth, as the inspector shows it.</summary>
public sealed record HkxInspectorRow(HkxDumpLine Line)
{
    public Thickness Indent => new(Line.Depth * 16, 0, 0, 0);
    public string Path => Line.Path;
    public string Value => Line.Value;
    public bool IsLink => Line.Target is not null;
    public bool IsPlain => !IsLink;
}

/// <summary>
/// An <c>.hkx</c> in its own tab: its bodies, shapes and constraints as a tree, and the selected object's
/// saved values. Read-only.
/// </summary>
public partial class HkxTabView : UserControl
{
    private readonly List<HkxTreeNode> _roots = [];
    private readonly Dictionary<HkxObject, int> _numbers = [];

    public HkxTabView(string fileName, string? path, byte[] content)
    {
        InitializeComponent();
        Title = fileName;

        HkxFile file;
        try
        {
            file = HkxFile.Parse(content);
        }
        catch (Exception ex)
        {
            HkxHeader.Text = $"{path ?? fileName}\nCouldn't read this file: {ex.Message}";
            return;
        }

        _numbers = file.Objects().Index().ToDictionary(p => p.Item, p => p.Index);
        _roots.AddRange(HkxShapes.Read(file).Select(n => new HkxTreeNode(n) { IsExpanded = true }));
        HkxTree.ItemsSource = _roots;

        int bodies = _roots.Sum(r => r.Bodies().Count());
        int shapes = _roots.Sum(r => r.Node.Primitives().Count());
        int constraints = _roots.Sum(r => r.Children.Count(c => c.Node.Object?.Class.Name == "hkpConstraintInstance"));
        HkxHeader.Text = $"{path ?? fileName}\n{Count(bodies, "body", "bodies")}, {Count(shapes, "collision shape", "collision shapes")}, "
            + Count(constraints, "constraint", "constraints");
    }

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    public string Title { get; }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => Inspect((e.NewValue as HkxTreeNode)?.Node.Object);

    private void Target_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Hyperlink)?.DataContext is not HkxInspectorRow { Line.Target: { } target })
        {
            return;
        }
        if (!_roots.Any(r => r.RevealWhere(n => n.Node.Object == target) is not null))
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
