using System.Windows;
using System.Windows.Controls;

namespace JackAll.App.Ai;

/// <summary>A list of archetypes and the fields of the ticked ones; its DataContext is an <see cref="AiArchetypesModel"/>.</summary>
public partial class ArchetypeTuningView : UserControl
{
    public ArchetypeTuningView() => InitializeComponent();

    private AiArchetypesModel? Model => DataContext as AiArchetypesModel;

    private void SelectShown_Click(object sender, RoutedEventArgs e) => Model?.SelectAll(true);

    private void SelectNone_Click(object sender, RoutedEventArgs e) => Model?.SelectAll(false);
}
