using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;
using Loretta.CodeAnalysis.Lua.Syntax;
using Nodify;

namespace JackAll.App.FileHandlers.Domino;

/// <summary>
/// One Domino graph viewer tab: a nodify canvas of the reconstructed boxes, their ports and their
/// control/data wiring on the left, an inspector and the generated Lua on the right.
///
/// Read-only. The canvas deliberately binds no <c>PendingConnection</c>, which is what stops nodify
/// offering to draw new wires; nodes stay draggable because rearranging one to read it better is
/// useful and costs nothing (positions aren't persisted, so a reopen re-runs the auto-layout).
/// </summary>
public partial class DominoTabView : UserControl
{
    private readonly DominoTabViewModel _vm;
    private readonly DominoValueActions? _values;

    /// <summary>False until the constructor has finished wiring up. The focus dropdown declares
    /// <c>SelectedIndex="0"</c>, so WPF raises its SelectionChanged during
    /// <see cref="InitializeComponent"/> - before the rest of this view exists.</summary>
    private bool _ready;

    public DominoTabView(DominoTabViewModel vm)
    {
        _vm = vm;
        InitializeComponent();

        SourceView.ShowPlainText(vm.SourceText);
        StatusText.Text = vm.StatusText;
        _values = vm.Services is { } services ? new DominoValueActions(services, Inspector.Play, SelectUses) : null;
        Inspector.ShowGraph(vm.Graph, vm.Twin, vm.StatusText, _values, vm.Findings);
        Inspector.ProblemActivated += GoToProblem;
        PreviewKeyDown += OnPreviewKeyDown;
        Editor.AddHandler(PreviewMouseRightButtonDownEvent, new MouseButtonEventHandler(Editor_RightButtonDown), true);
        Editor.AddHandler(MouseRightButtonUpEvent, new MouseButtonEventHandler(Editor_RightButtonUp), true);
        NodifyEditor.AutoFocusFirstElement = false;

        if (vm.Canvas is null)
        {
            Editor.Visibility = Visibility.Collapsed;
            _ready = true;
            return;
        }

        DataContext = vm.Canvas;
        vm.Canvas.PropertyChanged += OnCanvasPropertyChanged;
        AnnotatePorts(vm.Canvas.Nodes);
        var barking = vm.Canvas.Nodes
            .Where(n => RefsOf(n.Node).Any(r => r.Kind is ValueRefKind.Bark or ValueRefKind.BarkBank))
            .ToList();
        if (_values is not null && barking.Count > 0)
        {
            _values.LoadBarks(() =>
            {
                AnnotatePorts(barking);
                ShowSelection();
            });
        }
        _ready = true;

        // Node heights are only known once nodify has rendered them, so the layout is redone from the
        // measured sizes after the first pass, and the initial fit waits for that. LayoutUpdated fires
        // for the whole window, so the handler only listens while this tab is shown.
        Loaded += (_, _) =>
        {
            if (!_measured)
            {
                Editor.LayoutUpdated += ApplyMeasuredLayout;
            }
        };
        Unloaded += (_, _) => Editor.LayoutUpdated -= ApplyMeasuredLayout;
    }

    private bool _measured;

    private void ApplyMeasuredLayout(object? sender, EventArgs e)
    {
        if (_vm.Canvas?.TryApplyMeasuredLayout() == true)
        {
            _measured = true;
            Editor.LayoutUpdated -= ApplyMeasuredLayout;
            Dispatcher.BeginInvoke(FitToScreen, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnCanvasPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DominoGraphViewModel.SelectedNode))
        {
            ShowSelection();
        }
        if (e.PropertyName is nameof(DominoGraphViewModel.SelectedNode) or nameof(DominoGraphViewModel.NodesInFocus))
        {
            UpdateFocusStatus();
        }
    }

    private void ShowSelection()
    {
        DominoNodeViewModel? selected = _vm.Canvas?.SelectedNode;
        Inspector.ShowNode(selected, _vm.Canvas, selected is null ? [] : NodeActionsFor(selected), RefsOf(selected?.Node));
    }

    /// <summary>Hops per entry in the focus dropdown; 0 turns it off.</summary>
    private static readonly int[] FocusHopOptions = [0, 1, 2, 3, 5];

    private void FocusSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _vm.Canvas is null)
        {
            return;
        }
        int index = Math.Clamp(FocusSelector.SelectedIndex, 0, FocusHopOptions.Length - 1);
        _vm.Canvas.FocusHops = FocusHopOptions[index];
        UpdateFocusStatus();
    }

    private void UpdateFocusStatus()
    {
        if (!_ready || _vm.Canvas is not { } canvas)
        {
            return;
        }
        FocusStatus.Text = canvas.FocusHops <= 0
            ? string.Empty
            : canvas.SelectedNode is null
                ? "select a box"
                : $"{canvas.NodesInFocus} of {canvas.Nodes.Count} boxes";
    }

    /// <summary>A supplier chip stands in for a hub wire that isn't drawn; clicking it selects the box
    /// that actually produced the value, so nothing is unreachable.</summary>
    private void SupplierChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DominoConnectorViewModel chip })
        {
            _vm.Canvas?.SelectSupplier(chip);
        }
    }

    private void FitButton_Click(object sender, RoutedEventArgs e) => FitToScreen();

    private void FitToScreen()
    {
        if (_vm.Canvas is null || _vm.Canvas.Nodes.Count == 0)
        {
            return;
        }
        Editor.FitToScreen();
    }

    // ------------------------------------------------------------------ actions

    private readonly Dictionary<GraphNode, IReadOnlyList<ValueRef>> _refs = new(ReferenceEqualityComparer.Instance);

    /// <summary>What a box's parameter values name, worked out once per box.</summary>
    private IReadOnlyList<ValueRef> RefsOf(GraphNode? node)
    {
        if (node is null || _vm.Graph is not { } graph)
        {
            return [];
        }
        if (!_refs.TryGetValue(node, out IReadOnlyList<ValueRef>? refs))
        {
            _refs[node] = refs = DominoValueRefs.For(node, graph);
        }
        return refs;
    }

    /// <summary>Puts each parameter's value, and what it names, on its port's tooltip.</summary>
    private void AnnotatePorts(IEnumerable<DominoNodeViewModel> boxes)
    {
        foreach (DominoNodeViewModel vm in boxes)
        {
            if (vm.Node is not { } node || _vm.Graph is not { } graph)
            {
                continue;
            }

            IReadOnlyList<ValueRef> refs = RefsOf(node);
            foreach (DominoConnectorViewModel port in vm.Input)
            {
                if (port.Kind != PortKind.Data || !node.Params.TryGetValue(port.Name, out ExpressionSyntax? expr))
                {
                    continue;
                }
                string value = $"= {DominoExprPreview.Full(expr)}";
                port.Value = _values?.Explain(expr, refs.FirstOrDefault(r => r.Pin == port.Name), graph) is { } explained
                    ? $"{value}\n{explained}"
                    : value;
            }
        }
    }

    /// <summary>What can be done with a box as a whole, for the inspector and the context menu.</summary>
    private List<DominoAction> NodeActionsFor(DominoNodeViewModel vm)
    {
        if (vm.Node is not { } node)
        {
            return [];
        }

        var actions = new List<DominoAction>();
        if (node.IsSubGraph && _values?.OpenGraphAction(node.NodeTypePath) is { } open)
        {
            actions.Add(open);
        }
        if (LuaRangeOf(node) is { } range)
        {
            actions.Add(new DominoAction("Show in Lua", () => ShowInLua(range.Start, range.Length)));
        }
        actions.Add(DominoValueActions.Copy("Copy type path", node.NodeTypePath));
        if (node.OriginalName is { } name)
        {
            actions.Add(DominoValueActions.Copy("Copy name", name));
        }
        if (node.Params.Count > 0)
        {
            actions.Add(DominoValueActions.Copy("Copy parameters", string.Join(Environment.NewLine, node.Params
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key} = {DominoExprPreview.Full(p.Value)}"))));
        }
        return actions;
    }

    /// <summary>Where the box is configured in the Lua: its parameter assignments that share a block
    /// with the first one, or else the first statement that touches it.</summary>
    private (int Start, int Length)? LuaRangeOf(GraphNode node)
    {
        var statements = node.Params.Values
            .Select(e => e.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault())
            .OfType<StatementSyntax>()
            .OrderBy(st => st.SpanStart)
            .ToList();
        if (statements.Count > 0)
        {
            var block = statements.Where(st => st.Parent == statements[0].Parent).ToList();
            return (block[0].SpanStart, block[^1].Span.End - block[0].SpanStart);
        }
        return node.SourcePositions.Count > 0 ? LineAt(node.SourcePositions[0]) : null;
    }

    /// <summary>The rest of the source line starting at <paramref name="start"/>.</summary>
    private (int Start, int Length) LineAt(int start)
    {
        int end = _vm.SourceText.IndexOfAny(['\r', '\n'], start);
        return (start, (end < 0 ? _vm.SourceText.Length : end) - start);
    }

    /// <summary>Goes to what a clicked problem is about: its box on the canvas, else its statement.</summary>
    private void GoToProblem(DominoFinding finding)
    {
        DominoNodeViewModel? node = finding.NodeId is null ? null : _vm.Canvas?.Nodes.FirstOrDefault(n => n.Node?.Id == finding.NodeId);
        if (node is not null)
        {
            Reveal(node);
        }
        else if (finding.Position is { } position)
        {
            (int start, int length) = LineAt(position);
            ShowInLua(start, length);
        }
    }

    private void ShowInLua(int start, int length)
    {
        SideTabs.SelectedItem = LuaTab;
        Dispatcher.BeginInvoke(() => SourceView.Select(start, length), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Selects every box whose value names the same thing as <paramref name="value"/>.</summary>
    private void SelectUses(ValueRef value)
    {
        if (_vm.Canvas is not { } canvas)
        {
            return;
        }

        static ValueRefKind Family(ValueRefKind kind) => kind == ValueRefKind.Bark ? ValueRefKind.BarkBank : kind;
        var uses = canvas.Nodes
            .Where(n => RefsOf(n.Node).Any(r => Family(r.Kind) == Family(value.Kind) && r.Value == value.Value))
            .ToList();
        if (uses.Count == 0)
        {
            return;
        }

        Reveal(uses[0]);
        foreach (DominoNodeViewModel other in uses.Skip(1))
        {
            other.IsSelected = true;
        }
        FindStatus.Text = $"{uses.Count} boxes use {value.Value}";
    }

    private void Reveal(DominoNodeViewModel node)
    {
        _vm.Canvas!.SelectedNode = node;
        Editor.BringIntoView(new Point(node.Location.X + node.ActualSize.Width / 2, node.Location.Y + node.ActualSize.Height / 2));
    }

    private void Node_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DominoNodeViewModel { Node: { } node } })
        {
            return;
        }

        string? graphPath = node.IsSubGraph
            ? node.NodeTypePath
            : RefsOf(node).FirstOrDefault(r => r.Kind == ValueRefKind.Graph)?.Value;
        if (graphPath is not null && _values?.OpenGraphAction(graphPath) is { } open)
        {
            open.Run();
            e.Handled = true;
        }
    }

    private Point _rightPressedAt;

    private void Editor_RightButtonDown(object sender, MouseButtonEventArgs e) => _rightPressedAt = e.GetPosition(this);

    // Listened for on the editor with handled events included, and the box found by position: nodify
    // handles a right-click on an unselected box, and the release may come from whatever captured it.
    private void Editor_RightButtonUp(object sender, MouseButtonEventArgs e)
    {
        Vector moved = e.GetPosition(this) - _rightPressedAt;
        if (Math.Abs(moved.X) > SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(moved.Y) > SystemParameters.MinimumVerticalDragDistance
            || BoxUnder(Editor.InputHitTest(e.GetPosition(Editor))) is not { DataContext: DominoNodeViewModel { Node: { } node } vm } element)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = element };
        foreach (DominoAction action in NodeActionsFor(vm))
        {
            menu.Items.Add(MenuItemFor(action));
        }

        IReadOnlyList<ValueRef> values = _values is null ? [] : RefsOf(node);
        if (values.Count > 0)
        {
            menu.Items.Add(new Separator());
        }
        foreach (ValueRef value in values)
        {
            var item = new MenuItem { Header = new TextBlock { Text = $"{value.Pin}: {value.Value}" } };
            foreach (DominoAction action in _values!.For(value))
            {
                item.Items.Add(MenuItemFor(action));
            }
            menu.Items.Add(item);
        }

        // After nodify has finished with the click and let go of the mouse, or the menu closes at once.
        Dispatcher.BeginInvoke(() => menu.IsOpen = true, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>The element a click landed in that stands for a whole box, or null.</summary>
    private static FrameworkElement? BoxUnder(IInputElement? source)
    {
        for (DependencyObject? at = source as DependencyObject; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (at is FrameworkElement { DataContext: DominoNodeViewModel } box)
            {
                return box;
            }
            if (at is NodifyEditor)
            {
                return null;
            }
        }
        return null;
    }

    // A TextBlock header, because a string header reads an underscore as an access key.
    private static MenuItem MenuItemFor(DominoAction action)
    {
        var item = new MenuItem { Header = new TextBlock { Text = action.Label } };
        item.Click += (_, _) => action.Run();
        return item;
    }

    // ------------------------------------------------------------------ find

    private List<DominoNodeViewModel> _findMatches = [];
    private int _findIndex = -1;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && _vm.Canvas is not null)
        {
            FindBox.Focus();
            FindBox.SelectAll();
            e.Handled = true;
        }
    }

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = FindBox.Text.Trim();
        _findIndex = -1;
        _findMatches = query.Length == 0 || _vm.Canvas is null
            ? []
            : [.. _vm.Canvas.Nodes
                .Where(n => n.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || n.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || n.Node?.Params.Values.Any(v => DominoExprPreview.Full(v).Contains(query, StringComparison.OrdinalIgnoreCase)) == true)
                .OrderBy(n => n.Location.X)
                .ThenBy(n => n.Location.Y)];
        FindStatus.Text = query.Length == 0 ? "" : _findMatches.Count == 0 ? "no match" : $"{_findMatches.Count} matches";
    }

    /// <summary>Enter steps to the next match, Shift+Enter back, Escape clears.</summary>
    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            FindBox.Clear();
            Editor.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _findMatches.Count > 0)
        {
            int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            _findIndex = (_findIndex + step + _findMatches.Count) % _findMatches.Count;
            Reveal(_findMatches[_findIndex]);
            FindStatus.Text = $"{_findIndex + 1} of {_findMatches.Count}";
            e.Handled = true;
        }
    }
}
