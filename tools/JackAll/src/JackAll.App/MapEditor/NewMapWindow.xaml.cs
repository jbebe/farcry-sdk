using System.Windows;
using System.Windows.Controls;
using JackAll.Tools.World;

namespace JackAll.App.MapEditor;

/// <summary>Asks for a new map's name and size.</summary>
public partial class NewMapWindow : Window
{
    private readonly IReadOnlySet<string> _taken;

    /// <param name="taken">Names already in use, which a new map may not take.</param>
    public NewMapWindow(IReadOnlySet<string> taken)
    {
        _taken = taken;
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += (_, _) => NameBox.Focus();
    }

    public string MapName => NameBox.Text.Trim();

    public int Side => int.Parse((string)((ComboBoxItem)SizePicker.SelectedItem).Tag);

    private void Name_TextChanged(object sender, TextChangedEventArgs e)
    {
        ProblemText.Text = MapName.Length == 0 ? ""
            : !NewWorld.IsValidName(MapName) ? "That is not a name a world can have."
            : _taken.Contains(MapName) ? "A map with that name exists."
            : "";
        OkButton.IsEnabled = MapName.Length > 0 && ProblemText.Text.Length == 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
