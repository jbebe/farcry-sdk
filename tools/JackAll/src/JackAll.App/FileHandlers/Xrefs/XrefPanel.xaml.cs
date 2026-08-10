using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JackAll.Core.Vfs;
using JackAll.Core.Xrefs;

namespace JackAll.App.FileHandlers.Xrefs;

/// <summary>
/// The Xrefs section of the Files tab's details column: what references the selected file, and what
/// it references, either one double-clickable to jump.
/// </summary>
/// <remarks>
/// Hosted outside <c>FileHandlerCatalog</c>'s type switch, unlike every other view in this folder.
/// That's deliberate: references are the one thing worth showing for a file whose *type* nothing can
/// decode, which is roughly a quarter of the game's entries - an unnamed, unsniffable blob that turns
/// out to be pulled in by three `depload.dat`s has just told you far more than a "no preview
/// available" panel could.
///
/// Bound to plain lists rather than a live collection: the panel is rebuilt from scratch on every
/// selection change (see <see cref="Show"/>), which is both simpler and cheaper than diffing two
/// unrelated files' reference sets.
/// </remarks>
public partial class XrefPanel : UserControl
{
    private MainViewModel? _vm;

    public XrefPanel()
    {
        InitializeComponent();
        IncomingGrid.PreviewKeyDown += Grid_PreviewKeyDown;
        OutgoingGrid.PreviewKeyDown += Grid_PreviewKeyDown;
    }

    /// <summary>
    /// Repopulates for <paramref name="file"/>, or clears when nothing is selected. Called from
    /// <c>MainWindow.RefreshPreview</c> alongside the type-specific view.
    /// </summary>
    public void Show(MainViewModel vm, VfsFile? file)
    {
        _vm = vm;

        if (file is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;

        if (!vm.XrefsReady)
        {
            // No lists at all while the index is building - showing two empty grids would state
            // something false about this file rather than about the index.
            StatusLine.Text = vm.XrefStatus;
            StatusLine.Visibility = Visibility.Visible;
            IncomingExpander.Visibility = Visibility.Collapsed;
            OutgoingExpander.Visibility = Visibility.Collapsed;
            return;
        }

        StatusLine.Visibility = Visibility.Collapsed;
        IncomingExpander.Visibility = Visibility.Visible;
        OutgoingExpander.Visibility = Visibility.Visible;

        IReadOnlyList<XrefRow> incoming = vm.ReferencesTo(file);
        IReadOnlyList<XrefRow> outgoing = vm.ReferencesFrom(file);

        IncomingGrid.ItemsSource = incoming;
        OutgoingGrid.ItemsSource = outgoing;
        IncomingExpander.Header = $"Referenced by ({incoming.Count:N0})";
        OutgoingExpander.Header = $"References ({outgoing.Count:N0})";

        // Collapsed when empty so two dead grids don't push the preview off screen, but the header
        // still reports the zero - "nothing references this" is an answer, not an absence.
        IncomingExpander.IsExpanded = incoming.Count > 0;
        OutgoingExpander.IsExpanded = outgoing.Count > 0;
    }

    private void Row_Activated(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { Item: XrefRow row })
        {
            Navigate(row);
        }
    }

    /// <summary>Enter follows the selected row, so the panel is usable from the keyboard once a grid
    /// has focus - the same activation the double-click gives.</summary>
    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is DataGrid { SelectedItem: XrefRow row })
        {
            Navigate(row);
            e.Handled = true;
        }
    }

    private void Navigate(XrefRow row)
    {
        if (row.CanNavigate)
        {
            _vm?.TryNavigateToReference(row.Space, row.Target);
        }
    }

    private void CopyHash_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow(sender) is { } row)
        {
            Clipboard.SetText($"{row.Target:X8}");
        }
    }

    /// <summary>
    /// Drops <c>hash:XXXXXXXX</c> into the Files tab's filter box - the token
    /// <c>MainViewModel.ParseFilter</c> already understands. Useful precisely for the rows this panel
    /// can't navigate to: it shows whether anything with that hash exists under a different view.
    /// </summary>
    private void FilterByHash_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow(sender) is { } row && _vm is not null)
        {
            _vm.FilterText = $"hash:{row.Target:X8}";
        }
    }

    /// <summary>The row a context-menu click acted on. A <see cref="ContextMenu"/> isn't in the visual
    /// tree of the control that opened it, so the grid has to be reached through
    /// <see cref="ContextMenu.PlacementTarget"/> rather than by walking up from the menu item.</summary>
    private static XrefRow? SelectedRow(object sender)
        => sender is MenuItem { Parent: ContextMenu { PlacementTarget: DataGrid grid } }
            ? grid.SelectedItem as XrefRow
            : null;
}
