using System.Windows;
using JackAll.App.FileHandlers.Xbm;
using JackAll.Tools.Xbm;

namespace JackAll.App;

/// <summary>Making a material at a path no archive has.</summary>
public partial class MainWindow
{
    private void NewMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFile is not { } source)
        {
            Warn("Pick a material first.");
            return;
        }

        XbmFile material;
        try
        {
            material = XbmFile.Parse(_vm.Read(source));
        }
        catch (Exception ex)
        {
            Warn($"Couldn't read '{source.FileName}': {ex.Message}");
            return;
        }

        IEnumerable<string> shaders = _vm.ReadByPath(ShaderSchema.GamePath) is { } descriptors
            ? ShaderSchema.Parse(descriptors).Names.Order(StringComparer.Ordinal)
            : [material.Shader];
        var dialog = new NewMaterialWindow(source.Path, material.Shader, shaders, path => _vm.FindByPath(path) is not null)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.Shader is { } shader)
        {
            material = XbmFile.Create(dialog.MaterialName, shader);
        }
        else
        {
            material.Name = dialog.MaterialName;
        }

        try
        {
            _vm.Stage(dialog.GamePath, material.Write());
        }
        catch (Exception ex)
        {
            Warn($"Couldn't stage '{dialog.GamePath}': {ex.Message}");
            return;
        }
        // A search would leave the new file out of the list it is selected in.
        _vm.FilterText = string.Empty;
        _vm.Reindex(select: dialog.GamePath);
        _vm.Status = $"{dialog.GamePath} is in your workspace. A mesh draws with it once its material list names it.";
    }
}
