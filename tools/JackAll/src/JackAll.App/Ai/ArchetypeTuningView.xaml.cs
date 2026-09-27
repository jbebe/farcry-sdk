using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace JackAll.App.Ai;

/// <summary>A list of archetypes and the fields of the ticked ones; its DataContext is an <see cref="AiArchetypesModel"/>.</summary>
public partial class ArchetypeTuningView : UserControl
{
    public ArchetypeTuningView() => InitializeComponent();

    private AiArchetypesModel? Model => DataContext as AiArchetypesModel;

    private void SelectShown_Click(object sender, RoutedEventArgs e) => Model?.SelectAll(true);

    private void SelectNone_Click(object sender, RoutedEventArgs e) => Model?.SelectAll(false);

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }
}
