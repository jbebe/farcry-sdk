using System.Windows;
using System.Windows.Controls;
using JackAll.Tools.World;

namespace JackAll.App.MapEditor;

/// <summary>The toolbar's Check: the loaded world's lint findings, each a click away from its entity.</summary>
public partial class MapTabView
{
    /// <summary>The map the loaded world came from, which the check reads navmesh paths off.</summary>
    private TerrainMap? _loadedMap;

    private async void Check_Click(object sender, RoutedEventArgs e) => await Check();

    /// <summary>Runs the check and shows what it found. Returns the findings, or null when there is
    /// no world to check.</summary>
    private async Task<IReadOnlyList<WorldFinding>?> Check()
    {
        if (_vm is null || _edits is null || _archetypes is null || _loadedMap is null)
        {
            return null;
        }

        CheckButton.IsEnabled = false;
        try
        {
            IReadOnlyList<WorldFinding> findings = await _vm.CheckWorld(_edits, _loadedMap, _archetypes);
            int errors = findings.Count(f => f.Severity == LintSeverity.Error);
            CheckSummary.Text = findings.Count == 0
                ? "Nothing found"
                : $"{errors} errors, {findings.Count - errors} warnings";
            FindingsList.ItemsSource = findings.OrderByDescending(f => f.Severity).ToList();
            CheckPopup.IsOpen = true;
            return findings;
        }
        finally
        {
            CheckButton.IsEnabled = true;
        }
    }

    private void FindingsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FindingsList.SelectedItem is WorldFinding { Entity: var entity } && _edits?.World.Entities.Contains(entity) == true)
        {
            _selection.Replace([entity]);
            Reveal(entity);
        }
    }

    /// <summary>Whether a save should go ahead: yes unless the check finds errors and the user
    /// declines to save anyway.</summary>
    private async Task<bool> ConfirmSave()
    {
        IReadOnlyList<WorldFinding>? findings = await Check();
        int errors = findings?.Count(f => f.Severity == LintSeverity.Error) ?? 0;
        if (errors == 0)
        {
            CheckPopup.IsOpen = false;
            return true;
        }
        return MessageBox.Show(Window.GetWindow(this),
            $"The check found {errors} errors that will fail in game. Save anyway?", "JackAll",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}
