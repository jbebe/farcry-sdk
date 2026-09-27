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
public partial class AiTabView : UserControl
{
    private AiTabViewModel? _model;
    private bool _initialized;

    public AiTabView()
    {
        InitializeComponent();
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true)
            {
                await LoadSectionAsync();
            }
        };
    }

    public bool IsDirty => _model?.IsDirty == true;

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
        _model?.Brains.Initialize();
        if (IsVisible)
        {
            await LoadSectionAsync();
        }
    }

    /// <summary>Stages the unsaved edits; the text is why they could not be, or null.</summary>
    public Task<string?> SaveAsync() => _model?.SaveAsync() ?? Task.FromResult<string?>(null);

    private async void Sections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == Sections)
        {
            await LoadSectionAsync();
        }
    }

    private async Task LoadSectionAsync()
    {
        if (_model is not { IsBusy: false } model || !_initialized)
        {
            return;
        }

        model.IsBusy = true;
        try
        {
            if (Sections.SelectedItem == SoldiersSection && !model.Soldiers.IsLoaded)
            {
                await model.Soldiers.LoadAsync(new Progress<string>(s => model.Status = s));
            }
            else if (Sections.SelectedItem == BehaviorsSection && !model.Behaviors.IsLoaded)
            {
                model.Behaviors.Load();
                model.Status = $"{model.Behaviors.Rows.Count} adaptive behaviours";
            }
            else if (Sections.SelectedItem == BrainsSection && model.Brains.LoadedPath is null && BrainPicker.Items.Count > 0)
            {
                BrainPicker.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            model.Status = $"Couldn't load: {ex.Message}";
        }
        finally
        {
            model.IsBusy = false;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_model is not null && await _model.SaveAsync() is { } error)
        {
            MessageBox.Show(Window.GetWindow(this), error, "Not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (_model is not { } model
            || MessageBox.Show(Window.GetWindow(this), "Throw away the AI edits made since the last save?", "Revert",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        model.IsBusy = true;
        try
        {
            if (model.Soldiers.IsDirty)
            {
                await model.Soldiers.LoadAsync(new Progress<string>(s => model.Status = s));
            }
            if (model.Behaviors.IsDirty)
            {
                model.Behaviors.Load();
            }
            if (model.Brains is { IsDirty: true, LoadedPath: { } path })
            {
                await model.Brains.LoadAsync(path);
            }
            model.Status = "Reverted to the last save";
        }
        finally
        {
            model.IsBusy = false;
            DirtyChanged?.Invoke();
        }
    }

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }

    private void SelectShown_Click(object sender, RoutedEventArgs e) => _model?.Soldiers.SelectAll(true);

    private void SelectNone_Click(object sender, RoutedEventArgs e) => _model?.Soldiers.SelectAll(false);

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
            style.Setters.Add(new Setter(TextBlock.ToolTipProperty, new Binding($"Cells[{level}].Tip")));
            style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right));

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
        if (_model is not { } model || BrainPicker.SelectedItem is not string path || path == model.Brains.LoadedPath)
        {
            return;
        }
        if (model.Brains.IsDirty
            && MessageBox.Show(Window.GetWindow(this), "Discard the unsaved edits to this brain?", "Switch brain",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            BrainPicker.SelectedItem = model.Brains.LoadedPath;
            return;
        }

        model.IsBusy = true;
        model.Status = $"Reading {path}…";
        try
        {
            await model.Brains.LoadAsync(path);
            model.Status = $"{model.Brains.NodeCount:N0} nodes in {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            model.Status = $"Couldn't read {path}: {ex.Message}";
        }
        finally
        {
            model.IsBusy = false;
        }
    }

    private void BrainTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_model is not null && e.NewValue is BrainTreeItem { Node: { } node })
        {
            _model.Brains.Selected = node;
        }
    }

    private void SearchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_model is not null && SearchList.SelectedItem is AiNode node)
        {
            _model.Brains.Selected = node;
        }
    }

    private void Link_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_model is not null && sender is ListBox { SelectedItem: BrainLinkRow link })
        {
            _model.Brains.Selected = link.Target;
        }
    }
}
