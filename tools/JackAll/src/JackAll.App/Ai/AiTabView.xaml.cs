using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using JackAll.Tools.Ai;

namespace JackAll.App.Ai;

/// <summary>
/// The AI tab: tune soldier archetypes, the odds of optional behaviours, and the task parameters of
/// the brain workspaces. Each section loads the first time it is shown.
/// </summary>
public partial class AiTabView : UserControl, ISavableTab
{
    private AiTabViewModel _model = null!;
    private bool _initialized;

    public AiTabView()
    {
        InitializeComponent();
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true)
            {
                await ShowSectionAsync();
            }
        };
    }

    public bool IsDirty => _model.IsDirty;

    public event Action? DirtyChanged;

    /// <summary>Binds the tab to its view model; called as the window is built, before anything is loaded.</summary>
    public void Attach(MainViewModel vm)
    {
        _model = new AiTabViewModel(vm);
        _model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AiTabViewModel.IsDirty))
            {
                DirtyChanged?.Invoke();
            }
        };
        _model.Behaviors.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AiBehaviorsModel.Rows))
            {
                BuildLevelColumns();
            }
        };
        DataContext = _model;
    }

    /// <summary>Called once the VFS is loaded and the game's files become discoverable.</summary>
    public async Task InitializeAsync()
    {
        _initialized = true;
        if (IsVisible)
        {
            await ShowSectionAsync();
        }
    }

    public Task<string?> SaveAsync() => _model.SaveAsync();

    private async void Sections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == Sections)
        {
            await ShowSectionAsync();
        }
    }

    private async Task ShowSectionAsync()
    {
        if (!_initialized)
        {
            return;
        }
        AiSection section = Sections.SelectedItem == WeaponsSection ? AiSection.Weapons
            : Sections.SelectedItem == BehaviorsSection ? AiSection.Behaviors
            : Sections.SelectedItem == BrainsSection ? AiSection.Brains
            : AiSection.Soldiers;
        await _model.ShowAsync(section);
        if (section == AiSection.Brains && _model.Brains.LoadedPath is null && BrainPicker.Items.Count > 0)
        {
            BrainPicker.SelectedIndex = 0;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (await _model.SaveAsync() is { } error)
        {
            MessageBox.Show(Window.GetWindow(this), error, "Not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this), "Throw away the AI edits made since the last save?", "Revert",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
        {
            await _model.RevertAsync();
        }
    }

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }


    /// <summary>One column per progression level, bound to that level's cell of each row.</summary>
    private void BuildLevelColumns()
    {
        while (BehaviorGrid.Columns.Count > 1)
        {
            BehaviorGrid.Columns.RemoveAt(1);
        }
        for (int level = 0; level < AdaptiveBehaviors.Levels; level++)
        {
            var changed = new DataTrigger { Binding = new Binding($"Cells[{level}].IsChanged"), Value = true };
            changed.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
            var style = new Style(typeof(TextBlock)) { Triggers = { changed } };
            style.Setters.Add(new Setter(ToolTipProperty, new Binding($"Cells[{level}].Tip")));
            style.Setters.Add(new Setter(HorizontalAlignmentProperty, HorizontalAlignment.Right));

            BehaviorGrid.Columns.Add(new DataGridTextColumn
            {
                Header = level.ToString(CultureInfo.InvariantCulture),
                Binding = new Binding($"Cells[{level}].Value") { StringFormat = "0.#" },
                ElementStyle = style,
                Width = 44,
            });
        }
    }

    private void Fill_Click(object sender, RoutedEventArgs e)
    {
        if (FillBehavior.SelectedItem is BehaviorRow row
            && int.TryParse(FillFrom.Text, out int from)
            && double.TryParse(FillValue.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            row.Fill(Math.Clamp(from, 0, AdaptiveBehaviors.Levels - 1), value);
        }
    }

    private async void BrainPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BrainPicker.SelectedItem is not string path || path == _model.Brains.LoadedPath)
        {
            return;
        }
        if (_model.Brains.IsDirty
            && MessageBox.Show(Window.GetWindow(this), "Discard the unsaved edits to this brain?", "Switch brain",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            BrainPicker.SelectedItem = _model.Brains.LoadedPath;
            return;
        }
        await _model.LoadBrainAsync(path);
    }

    private void BrainTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is BrainTreeItem { Node: { } node })
        {
            _model.Brains.Selected = node;
        }
    }

    private void SearchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchList.SelectedItem is AiNode node)
        {
            _model.Brains.Selected = node;
        }
    }

    private void Link_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: BrainLinkRow link })
        {
            _model.Brains.Selected = link.Target;
        }
    }
}
