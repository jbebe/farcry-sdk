using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using JackAll.Tools.Domino.Graphs;

namespace JackAll.App.Domino;

/// <summary>
/// One Domino graph viewer tab's view: a hand-rolled, pan/zoomable Canvas of reconstructed boxes and
/// connections on the left (same pan-drag/wheel-zoom interaction as the .xbg mesh viewer's orbit
/// camera, just in 2D - see <see cref="FileHandlers.Xbg.XbgFileHandler"/>), the raw generated Lua on
/// the right via the existing read-only text viewer. Read-only: there is no write path yet, so this
/// only ever renders what <see cref="DominoTabViewModel"/> parsed at open time.
/// </summary>
public partial class DominoTabView : UserControl
{
    private static readonly Brush PersistentFill = new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFE));
    private static readonly Brush PooledFill = new SolidColorBrush(Color.FromRgb(0xFC, 0xF3, 0xE3));
    private static readonly Brush SubGraphFill = new SolidColorBrush(Color.FromRgb(0xE6, 0xF7, 0xE9));
    private static readonly Brush NodeBorderBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
    private static readonly Brush EdgeBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

    static DominoTabView()
    {
        PersistentFill.Freeze();
        PooledFill.Freeze();
        SubGraphFill.Freeze();
        NodeBorderBrush.Freeze();
        EdgeBrush.Freeze();
    }

    private Point _lastMouse;
    private bool _dragging;

    public DominoTabView(DominoTabViewModel vm)
    {
        InitializeComponent();
        SourceView.ShowPlainText(vm.SourceText);
        Build(vm);
    }

    private void Build(DominoTabViewModel vm)
    {
        if (vm.ParseError is not null)
        {
            StatusText.Text = $"Couldn't build a graph from this file: {vm.ParseError} - the source is still shown on the right.";
            return;
        }
        if (vm.Graph is null || vm.Nodes.Count == 0)
        {
            StatusText.Text = "No reconstructable box graph here (a system\\ node body, or an empty user\\ graph).";
            return;
        }

        var positionById = vm.Nodes.ToDictionary(p => p.Node.Id);

        // Edges first, so node boxes paint on top of the lines feeding into them.
        int unwired = 0, deadEnd = 0, graphExit = 0, connections = 0;
        foreach (GraphEdge edge in vm.Graph.Edges)
        {
            switch (edge.Target)
            {
                case EdgeTarget.Node
                    when edge.TargetNodeId is not null
                      && positionById.TryGetValue(edge.SourceNodeId, out var from)
                      && positionById.TryGetValue(edge.TargetNodeId, out var to):
                    DrawEdge(from, to);
                    connections++;
                    break;
                case EdgeTarget.Unwired:
                    unwired++;
                    break;
                case EdgeTarget.DeadEnd:
                    deadEnd++;
                    break;
                case EdgeTarget.GraphExit:
                    graphExit++;
                    break;
            }
        }

        foreach (PositionedNode p in vm.Nodes)
        {
            DrawNode(p);
        }

        StatusText.Text = $"{vm.Nodes.Count} boxes, {connections} connections " +
                           $"({unwired} unwired, {deadEnd} dead-end, {graphExit} exits to this graph's own pins).";
    }

    private void DrawNode(PositionedNode p)
    {
        Brush fill = p.Node.IsSubGraph ? SubGraphFill
            : p.Node.Kind == BoxInstanceKind.Persistent ? PersistentFill
            : PooledFill;

        var stack = new StackPanel { Margin = new Thickness(6) };
        stack.Children.Add(new TextBlock
        {
            Text = ShortTypeName(p.Node.NodeTypePath),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = RefLabel(p.Node),
            FontSize = 10,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 2, 0, 4),
        });
        foreach (var (name, value) in p.Node.Params.Take(6))
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"{name}: {DominoExprPreview.Short(value)}",
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var border = new Border
        {
            Width = p.Width,
            Height = p.Height,
            Background = fill,
            BorderBrush = NodeBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = stack,
            ToolTip = p.Node.NodeTypePath,
        };
        Canvas.SetLeft(border, p.X);
        Canvas.SetTop(border, p.Y);
        GraphCanvas.Children.Add(border);
    }

    private void DrawEdge(PositionedNode from, PositionedNode to)
    {
        double x1 = from.X + from.Width, y1 = from.Y + from.Height / 2;
        double x2 = to.X, y2 = to.Y + to.Height / 2;

        GraphCanvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = EdgeBrush, StrokeThickness = 1.5 });

        double angle = Math.Atan2(y2 - y1, x2 - x1);
        const double headLen = 8, headAngle = Math.PI / 7;
        GraphCanvas.Children.Add(new Polyline
        {
            Points =
            [
                new Point(x2 - headLen * Math.Cos(angle - headAngle), y2 - headLen * Math.Sin(angle - headAngle)),
                new Point(x2, y2),
                new Point(x2 - headLen * Math.Cos(angle + headAngle), y2 - headLen * Math.Sin(angle + headAngle)),
            ],
            Stroke = EdgeBrush,
            StrokeThickness = 1.5,
        });
    }

    private static string ShortTypeName(string nodeTypePath) => System.IO.Path.GetFileNameWithoutExtension(nodeTypePath);

    private static string RefLabel(GraphNode node) => node.Ref switch
    {
        InstanceBoxRef i => $"self[{i.Slot}]",
        NamedInstanceBoxRef n => $"self.{n.FieldName}",
        PooledBoxRef => $"pooled, in {node.OwnerFunction}",
        _ => node.OwnerFunction,
    };

    // ---------------------------------------------------------------- pan / zoom

    private void GraphCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        _dragging = true;
        _lastMouse = e.GetPosition(this);
        GraphCanvas.CaptureMouse();
    }

    private void GraphCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        _dragging = false;
        GraphCanvas.ReleaseMouseCapture();
    }

    private void GraphCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;

        Point pos = e.GetPosition(this);
        Vector delta = pos - _lastMouse;
        _lastMouse = pos;
        GraphTranslate.X += delta.X;
        GraphTranslate.Y += delta.Y;
    }

    // Zooms around the canvas origin rather than the cursor - simpler, and fine at this node density;
    // a cursor-centered zoom would need to adjust GraphTranslate in step with GraphScale.
    private void GraphCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = Math.Pow(1.1, e.Delta / 120.0);
        double newScale = Math.Clamp(GraphScale.ScaleX * factor, 0.1, 3.0);
        GraphScale.ScaleX = newScale;
        GraphScale.ScaleY = newScale;
    }

    private void ResetViewButton_Click(object sender, RoutedEventArgs e)
    {
        GraphScale.ScaleX = 1;
        GraphScale.ScaleY = 1;
        GraphTranslate.X = 20;
        GraphTranslate.Y = 20;
    }
}
