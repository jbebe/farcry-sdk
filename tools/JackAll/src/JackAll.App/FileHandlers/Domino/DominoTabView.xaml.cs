using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
        Inspector.ShowGraph(vm.Graph, vm.Twin, vm.StatusText);
        NodifyEditor.AutoFocusFirstElement = false;

        if (vm.Canvas is null)
        {
            Editor.Visibility = Visibility.Collapsed;
            _ready = true;
            return;
        }

        DataContext = vm.Canvas;
        vm.Canvas.PropertyChanged += OnCanvasPropertyChanged;
        _ready = true;

        // The editor only knows its viewport size once it has been arranged, so the initial fit has to
        // wait for the first layout pass rather than running here.
        Loaded += (_, _) => FitToScreen();
    }

    private void OnCanvasPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DominoGraphViewModel.SelectedNode))
        {
            Inspector.ShowNode(_vm.Canvas?.SelectedNode);
        }
        if (e.PropertyName is nameof(DominoGraphViewModel.SelectedNode) or nameof(DominoGraphViewModel.NodesInFocus))
        {
            UpdateFocusStatus();
        }
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
}
