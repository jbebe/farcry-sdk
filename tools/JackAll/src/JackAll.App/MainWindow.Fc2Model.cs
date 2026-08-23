using JackAll.Core.Vfs;
using JackAll.Tools.Fc2Model;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace JackAll.App;

/// <summary>
/// The <c>.fc2model</c> handlers: pack a model out for a 3D editor, and stage back what came in.
/// </summary>
/// <remarks>
/// This is the whole App side of the model pipeline, which is deliberately small - the format work
/// lives in <see cref="Fc2ModelBuilder"/> and <see cref="Fc2ModelApplier"/>, and everything here
/// does is pick files, report, and route the result through the workspace.
/// </remarks>
public partial class MainWindow
{
    private void ExportPack_Click(object sender, RoutedEventArgs e) => ExportPack(withClips: false);

    private void ExportPackWithClips_Click(object sender, RoutedEventArgs e) => ExportPack(withClips: true);

    /// <summary>
    /// Writes the selected model and its closure out as one pack.
    /// </summary>
    /// <remarks>
    /// Finding the clips means reading every animation bank in the install, which is why it is a
    /// separate button rather than always on - a bank names the models it moves, and nothing in a
    /// mesh names its banks, so there is no cheaper way to ask.
    /// </remarks>
    private void ExportPack(bool withClips)
    {
        if (_vm.SelectedFile is not { } model)
        {
            Warn("Pick a model first.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export as .fc2model",
            FileName = Path.GetFileNameWithoutExtension(model.FileName) + Fc2ModelBundle.Extension,
            Filter = $"Model pack|*{Fc2ModelBundle.Extension}|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            List<string>? clips = withClips ? _vm.FindClips(model) : null;
            Fc2ModelBundle bundle = _vm.BuildPack(model, clips);
            bundle.Save(dialog.FileName);

            int banks = bundle.Manifest.Clips.Count;
            _vm.Status = $"Packed {bundle.Manifest.Entries.Count} files for {model.FileName}"
                + (banks > 0 ? $", including {banks} animation bank(s)." : ".");
        }
        catch (Exception ex)
        {
            Warn($"Couldn't pack '{model.FileName}': {ex.Message}");
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    /// <summary>
    /// Stages a pack's edits into the workspace, listing every game file it will change first.
    /// </summary>
    /// <remarks>
    /// The list is not a formality. A pack carries the materials and textures a model shares with
    /// others, and while editing a shared one is refused outright, the count of files an apply
    /// touches is the only thing that tells a user their edit reached more than the model they
    /// thought they were changing.
    /// </remarks>
    private void ApplyPack_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Apply .fc2model",
            Filter = $"Model pack|*{Fc2ModelBundle.Extension}|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        Fc2ModelBundle bundle;
        List<Fc2ModelOutput> outputs;
        try
        {
            bundle = Fc2ModelBundle.Load(dialog.FileName);
            outputs = MainViewModel.PlanPack(bundle);
        }
        catch (Exception ex)
        {
            Warn($"Couldn't read that pack: {ex.Message}");
            return;
        }

        if (outputs.Count == 0)
        {
            Warn($"'{Path.GetFileName(dialog.FileName)}' holds no edits, so there is nothing to apply.\n\n"
                 + "A pack only asks for a file to be written once an editor has changed it.");
            return;
        }

        if (MessageBox.Show(
                this,
                $"Applying this pack will stage {outputs.Count} file(s) into your workspace:\n\n"
                + string.Join(Environment.NewLine, outputs.Take(12).Select(o => o.Path))
                + (outputs.Count > 12 ? $"{Environment.NewLine}… and {outputs.Count - 12} more" : "")
                + "\n\nContinue?",
                "Apply .fc2model",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            int written = _vm.ApplyPack(bundle);
            _vm.Status = $"Staged {written} file(s) from {Path.GetFileName(dialog.FileName)}. "
                + "Deploy mods to make it so in-game.";
        }
        catch (Exception ex)
        {
            Warn($"Couldn't apply that pack: {ex.Message}");
            return;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        RescanMods_Click(sender, e);
    }
}
