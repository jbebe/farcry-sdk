using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace JackAll.App.FileHandlers.Xbm;

/// <summary>Asks for a new material's name, game path, and whether it starts as a copy or empty.</summary>
public partial class NewMaterialWindow : Window
{
    private readonly string _folder;
    private readonly Func<string, bool> _exists;
    private bool _pathFollowsName = true;

    /// <param name="source">The material selected, which a copy is made of and whose folder the path starts in.</param>
    /// <param name="exists">Whether a game path is taken already.</param>
    public NewMaterialWindow(string source, string sourceShader, IEnumerable<string> shaders, Func<string, bool> exists)
    {
        _folder = Path.GetDirectoryName(source) ?? string.Empty;
        _exists = exists;
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += (_, _) => NameBox.Focus();
        CopyOption.Content = $"A copy of {Path.GetFileName(source)}";
        ShaderPicker.ItemsSource = shaders;
        ShaderPicker.SelectedItem = sourceShader;
    }

    public string MaterialName => NameBox.Text.Trim();

    public string GamePath => PathBox.Text.Trim();

    /// <summary>The shader to start empty on, or null for a copy.</summary>
    public string? Shader => EmptyOption.IsChecked == true ? ShaderPicker.SelectedItem as string : null;

    private void Name_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_pathFollowsName)
        {
            PathBox.Text = Path.Combine(_folder, MaterialName.ToLowerInvariant() + ".xbm");
            // Setting the box raised Path_TextChanged, which takes it for a hand edit.
            _pathFollowsName = true;
        }
        Check();
    }

    private void Path_TextChanged(object sender, TextChangedEventArgs e)
    {
        _pathFollowsName = false;
        Check();
    }

    private void Check()
    {
        ProblemText.Text = MaterialName.Length == 0 ? ""
            : !GamePath.EndsWith(".xbm", StringComparison.OrdinalIgnoreCase) ? "The path has to end in .xbm."
            : _exists(GamePath) ? $"{GamePath} is a file already. Pick another path, or edit that one."
            : "";
        OkButton.IsEnabled = MaterialName.Length > 0 && ProblemText.Text.Length == 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
