using JackAll.Core.Mods;
using JackAll.Tools.World;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows;

namespace JackAll.App;

/// <summary>The Mods tab's handlers: adding/removing/reordering mods, legacy import, and building
/// or reverting the game's patch archives.</summary>
public partial class MainWindow
{
    /// <summary>Opens the selected mod's containing folder - the workspace's own staging folder for
    /// that row, or the folder holding the zip for any other.</summary>
    private void OpenModLocation_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedMod is not { } mod) return;

        string folder = mod.IsWorkspace
            ? AppConfig.WorkspaceDir
            : Path.GetDirectoryName(((ZipModLayer)mod.Layer).ZipPath)!;

        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void AddMod_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add a mod",
            Filter = "Mod archives (*.zip)|*.zip",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        foreach (string path in dialog.FileNames)
        {
            try
            {
                var layer = new ZipModLayer(path);

                if (layer.Hashes.Count == 0 && layer.FragmentOverrides.Count == 0
                    && layer.PluginPaths.Count == 0)
                {
                    Warn($"'{Path.GetFileName(path)}' has no files this game recognises.\n\n" +
                         "A mod zip should contain the game's own folder structure - for example " +
                         "worlds\\world1\\generated\\… - optionally under a mods\\ folder, and/or an " +
                         "FCSE plugin under a plugins\\ folder.");
                    continue;
                }

                // The workspace row is pinned last, so new mods go in above it.
                int insertAt = _vm.Mods.Count(m => !m.IsWorkspace);
                _vm.Mods.Insert(insertAt, new ModRow(layer, isWorkspace: false));
            }
            catch (Exception ex)
            {
                Warn($"Couldn't read '{Path.GetFileName(path)}': {ex.Message}");
            }
        }

        _vm.Reindex();
        _vm.SaveConfig();
    }

    /// <summary>
    /// "Import legacy…": picks a zip carrying a full replacement patch.dat/patch.fat (the old
    /// build_patch.bat-style workflow) and stages only what it actually changed relative to the base
    /// game straight into the workspace - see <see cref="MainViewModel.ImportLegacyMod"/>. Unlike
    /// <see cref="AddMod_Click"/>, this doesn't add a new row to the Mods grid: the result becomes your
    /// own workspace edits, ready to zip up and share once you're happy with it.
    /// </summary>
    private async void ImportLegacy_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import a legacy mod (a zip containing patch.dat and patch.fat)",
            Filter = "Mod archives (*.zip)|*.zip",
        };
        if (dialog.ShowDialog(this) != true) return;

        _vm.Busy = true;
        try
        {
            LegacyImportResult result = await _vm.ImportLegacyMod(dialog.FileName);

            string fragmentsNote = result.FragmentsImported > 0
                ? $" + {result.FragmentsImported:N0} entity-library fragments"
                : string.Empty;
            _vm.Status =
                $"Imported '{Path.GetFileName(dialog.FileName)}': {result.Imported:N0} changed files{fragmentsNote} " +
                $"staged to your workspace ({result.Skipped:N0} of {result.TotalEntries:N0} were identical to the " +
                "base game and left out). Open workspace to review, then zip it up to share.";
        }
        catch (Exception ex)
        {
            Warn($"Couldn't import that mod: {ex.Message}");
        }
        finally
        {
            _vm.Busy = false;
        }
    }

    private void RescanMods_Click(object sender, RoutedEventArgs e) => _vm.RescanMods();

    /// <summary>Reports archetype edits a later entity library overrides - the silent failure the
    /// library's replace-by-name rule makes easy, since the edited file really did change.</summary>
    private async void LintArchetypes_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.IsEnabled = false;
        try
        {
            IReadOnlyList<DeadEdit> dead = await _vm.LintArchetypes();
            if (dead.Count == 0)
            {
                _vm.Status = "No dead archetype edits - every edited archetype is the copy the game reads.";
                return;
            }

            const int shown = 10;
            IEnumerable<string> lines = dead
                .GroupBy(d => d.Source)
                .Select(byLayer =>
                    $"{byLayer.Key}:{Environment.NewLine}"
                    + string.Join(
                        Environment.NewLine,
                        byLayer.Take(shown).Select(d => $"  {d.Archetype} - overridden by {d.WinningPath}"))
                    + (byLayer.Count() > shown
                        ? $"{Environment.NewLine}  ... and {byLayer.Count() - shown:N0} more"
                        : ""));

            MessageBox.Show(
                this,
                $"{dead.Count:N0} archetype edit(s) change the file but nothing in game:"
                + $"{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}"
                + $"{Environment.NewLine}{Environment.NewLine}"
                + "Move the edit into the library that wins, or drop the archetype from the later one.",
                "JackAll", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void RemoveMod_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMod() is not { IsWorkspace: false } row)
        {
            Warn("Pick a mod to remove. (The workspace holds your own edits and can't be removed - " +
                 "switch it off instead.)");
            return;
        }

        _vm.Mods.Remove(row);
        _vm.Reindex();
        _vm.SaveConfig();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelectedMod(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelectedMod(+1);

    private void MoveSelectedMod(int delta)
    {
        if (SelectedMod() is not { IsWorkspace: false } row) return;

        int from = _vm.Mods.IndexOf(row);
        int to = from + delta;

        // The workspace is always last; nothing may be moved past it.
        int lastMovable = _vm.Mods.Count(m => !m.IsWorkspace) - 1;
        if (to < 0 || to > lastMovable) return;

        _vm.Mods.Move(from, to);
        ModGrid.SelectedItem = row;
        _vm.Reindex();
        _vm.SaveConfig();
    }

    private async void BuildAndApply_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Install is null) return;

        _vm.Busy = true;
        _vm.Status = "Building patch.dat…";
        try
        {
            BuildResult result = await _vm.BuildPatch();

            string pluginsNote = result.PluginsDeployed + result.PluginsRemoved > 0
                ? $", {result.PluginsDeployed:N0} plugin file(s) deployed"
                : string.Empty;
            _vm.Status =
                $"Built patch.dat - {result.TotalEntries:N0} files "
                + $"({result.OverriddenEntries:N0} replaced, {result.AddedEntries:N0} added, "
                + $"{MainViewModel.FormatSize(result.OutputBytes)}{pluginsNote}). Launch the game to see it.";

            _vm.SaveConfig();
        }
        catch (Exception ex)
        {
            _vm.Status = "Build failed - the game's files were not changed.";
            MessageBox.Show(this,
                $"{ex.Message}\n\nYour game is untouched: the new patch is written to a temporary " +
                "file and only swapped in once it's complete.",
                "Build failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _vm.Busy = false;
        }
    }

    private void RestoreVanilla_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Install is not { } install) return;

        if (!install.HasVanillaBackup)
        {
            Warn("There's no backup yet - nothing has been built, so the game is already unmodded.");
            return;
        }

        if (MessageBox.Show(this,
                "Remove every mod from the game and put its original files back?\n\n" +
                "Your mods and your workspace stay exactly where they are here in JackAll - this only un-applies them from the game.",
                "Remove all mods", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        install.RestoreVanilla();
        _vm.ReloadPatchArchive();

        IReadOnlyList<string> patchMismatches = VanillaHashesProvider.Value.Value
            .FindMismatches(install.DataDir, install.PatchArchiveRelativePaths());
        if (patchMismatches.Count > 0)
        {
            Warn("All mods were removed, but the restored patch.dat/patch.fat still don't match the " +
                 "known hash for a clean 1.03 Far Cry 2. This can happen if the backup JackAll made " +
                 "was already modded before this tool ever saw it, or if your game is a different " +
                 "version. Verifying game files in Steam is the safest way back to a truly clean install.");
        }

        _vm.Status = "All mods removed - the game's original files are back. Your mods are still listed here.";
    }

    private ModRow? SelectedMod() => ModGrid.SelectedItem as ModRow;

    private void ModGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _vm.SelectedMod = ModGrid.SelectedItem as ModRow;
}
