using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JackAll.App.Picker;
using JackAll.Core.Format.Move;
using Microsoft.Win32;

namespace JackAll.App.Move;

/// <summary>
/// The Animations tab: pick a weapon and a situation, see which rule the engine picks, and change
/// what a rule plays, when it plays, or where it sits in the search.
/// </summary>
public partial class MoveTabView : UserControl
{
    private MainViewModel? _vm;
    private MoveRulesViewModel? _model;
    private MoveClipForm? _clipForm;
    private MoveTreeNode? _rawRoot;
    private string? _loadedGraph;

    public MoveTabView()
    {
        InitializeComponent();

        // The base graph opens the first time the tab is shown, so nothing is parsed for a tab never visited.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && _loadedGraph is null && GraphPicker.Items.Count > 0)
            {
                GraphPicker.SelectedIndex = 0;
            }
        };
    }

    public bool IsDirty => _model?.IsDirty == true;

    public event Action? DirtyChanged;

    /// <summary>Binds the tab to its view model; called as the window is built, before anything is loaded.</summary>
    public void Attach(MainViewModel vm)
    {
        _vm = vm;
        _model = new MoveRulesViewModel(vm);
        _model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MoveRulesViewModel.IsDirty))
            {
                DirtyChanged?.Invoke();
            }
            else if (e.PropertyName == nameof(MoveRulesViewModel.SelectedRule))
            {
                RefreshRaw();
                if (_model.SelectedRule is { } row)
                {
                    RulesGrid.ScrollIntoView(row);
                }
            }
            else if (e.PropertyName == nameof(MoveRulesViewModel.ClipForm))
            {
                WatchClipForm();
            }
        };
        DataContext = _model;
        WatchClipForm();
    }

    /// <summary>Follows the clip form the model holds now; a graph load replaces it.</summary>
    private void WatchClipForm()
    {
        if (_clipForm is not null)
        {
            _clipForm.PropertyChanged -= ClipForm_PropertyChanged;
        }
        _clipForm = _model?.ClipForm;
        if (_clipForm is not null)
        {
            _clipForm.PropertyChanged += ClipForm_PropertyChanged;
        }
        ShowClip();
    }

    private void ClipForm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MoveClipForm.Path))
        {
            ShowClip();
        }
    }

    /// <summary>Previews the bank the clip path names, or says why it cannot.</summary>
    private void ShowClip()
    {
        string path = _clipForm?.Path ?? string.Empty;
        if (_vm is null || path.Length == 0)
        {
            ClipPlayer.Fail("Name a bank to preview it.");
        }
        else if (_vm.ReadByPath(path) is not { } bytes)
        {
            ClipPlayer.Fail($"No file at {path}.");
        }
        else
        {
            ClipPlayer.Load(Path.GetFileName(path), bytes, bank => _vm.FindRigs(path, bank));
        }
    }

    /// <summary>Lists the graphs once the VFS is loaded and its paths become discoverable.</summary>
    public void Initialize()
    {
        _model?.Initialize();
        if (IsVisible && GraphPicker.Items.Count > 0)
        {
            GraphPicker.SelectedIndex = 0;
        }
    }

    /// <summary>Stages the unsaved edits; the text is why they could not be, or null.</summary>
    public Task<string?> SaveAsync() => _model?.SaveAsync() ?? Task.FromResult<string?>(null);

    private async void GraphPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_model is null || GraphPicker.SelectedItem is not string path || path == _loadedGraph)
        {
            return;
        }

        if (IsDirty && MessageBox.Show(Window.GetWindow(this), "Discard the unsaved animation edits?",
                "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            GraphPicker.SelectedItem = _loadedGraph;
            return;
        }

        _loadedGraph = path;
        await _model.LoadAsync(path);
    }

    private void SituationDone_Click(object sender, RoutedEventArgs e) => SituationToggle.IsChecked = false;

    private void OpenGoTo_Click(object sender, RoutedEventArgs e) => _model?.OpenGoTo();

    private void RulesGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e) => SituationToggle.IsChecked = false;

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (await SaveAsync() is { } error)
        {
            MessageBox.Show(Window.GetWindow(this), error, "Not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Revert_Click(object sender, RoutedEventArgs e) => _model?.Revert();

    private void ResetSituation_Click(object sender, RoutedEventArgs e) => _model?.ResetSituation();

    private void ApplyClip_Click(object sender, RoutedEventArgs e) => _model?.ApplyClip();

    private void AddCondition_Click(object sender, RoutedEventArgs e) => _model?.AddCondition();

    private void UpdateCondition_Click(object sender, RoutedEventArgs e) => _model?.UpdateCondition();

    private void RemoveCondition_Click(object sender, RoutedEventArgs e) => _model?.RemoveCondition();

    private void Duplicate_Click(object sender, RoutedEventArgs e) => _model?.Duplicate();

    private void Earlier_Click(object sender, RoutedEventArgs e) => _model?.Move(-1);

    private void Later_Click(object sender, RoutedEventArgs e) => _model?.Move(+1);

    private void CloneWeapon_Click(object sender, RoutedEventArgs e) => _model?.CloneWeapon();

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_model?.SelectedRule is { } rule
            && MessageBox.Show(Window.GetWindow(this), $"Delete rule {rule.Number}? The state then never plays it.",
                "Delete rule", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _model.Delete();
        }
    }

    private void BrowseClip_Click(object sender, RoutedEventArgs e)
    {
        if (_model is not null
            && FilePicker.Show(this, new FilePickerRequest(
                "Pick an animation bank", Extension: "mab", InitialPath: _model.ClipForm.Path, NeedsRealPath: true)) is { } file)
        {
            _model.ClipForm.Path = file.Path;
        }
    }

    private void RawToggle_Changed(object sender, RoutedEventArgs e) => RefreshRaw();

    private void RefreshRaw()
    {
        if (RawToggle.IsChecked != true || _model?.SelectedRule?.Rule.Node is not { } node || _model.Channels is not { } channels)
        {
            _rawRoot = null;
            RawTree.ItemsSource = null;
            FieldGrid.ItemsSource = null;
            return;
        }

        _rawRoot = MoveTreeNode.Build(node, channels);
        _rawRoot.IsExpanded = true;
        RawTree.ItemsSource = new[] { _rawRoot };
        FieldGrid.ItemsSource = MoveTreeNode.Fields(node, channels);
        MoveTreeNode.Filter(_rawRoot, RawSearch.Text.Trim());
    }

    private void RawTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is MoveTreeNode node && _model?.Channels is { } channels)
        {
            FieldGrid.ItemsSource = MoveTreeNode.Fields(node.Target, channels);
        }
    }

    private void RawSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (_rawRoot is not null)
        {
            MoveTreeNode.Filter(_rawRoot, RawSearch.Text.Trim());
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_model?.File is not { } file)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Filter = "XML documents (*.xml)|*.xml",
            FileName = Path.GetFileNameWithoutExtension(GraphPicker.SelectedItem as string ?? "movemgr") + ".xml",
        };
        if (dialog.ShowDialog() == true)
        {
            File.WriteAllText(dialog.FileName, MoveXml.ToXml(file, new MoveLabels(_model.Channels?.Named, _model.PathOf)));
        }
    }
}
