using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace JackAll.App.Domino;

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

    public DominoTabView(DominoTabViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        SourceView.ShowPlainText(vm.SourceText);
        StatusText.Text = vm.StatusText;
        Inspector.ShowGraph(vm.Graph, vm.Twin, vm.StatusText);

        if (vm.Canvas is null)
        {
            Editor.Visibility = Visibility.Collapsed;
            return;
        }

        DataContext = vm.Canvas;
        vm.Canvas.PropertyChanged += OnCanvasPropertyChanged;

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
