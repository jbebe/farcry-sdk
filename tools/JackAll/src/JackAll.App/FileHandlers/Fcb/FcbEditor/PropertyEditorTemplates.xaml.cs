using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using JackAll.App.Audio;
using JackAll.App.FileHandlers.Audio;
using JackAll.App.Picker;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.Fcb;

namespace JackAll.App.FileHandlers.Fcb.FcbEditor;

/// <summary>The value editors a <see cref="PropertyRow"/> shows, the field row around them, and the
/// buttons both carry. Merged into every view that edits fields.</summary>
public partial class PropertyEditorTemplates : ResourceDictionary
{
    public PropertyEditorTemplates()
    {
        InitializeComponent();
    }

    private void RevertField_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FieldView field)
        {
            field.Revert();
        }
    }

    private void RestoreField_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FieldView field)
        {
            field.Row.RestoreOriginal();
        }
    }

    /// <summary>Picks the file a field names and writes it the way the field stores it: a path, a path's
    /// hash, or a sound bank's id.</summary>
    private void PickFile_Click(object sender, RoutedEventArgs e)
    {
        var button = (FrameworkElement)sender;
        if (button.DataContext is not ScalarField field)
        {
            return;
        }

        string? current = field.FileRef == FileRef.Path ? field.Text : field.ResolvedPath;
        string extension = Path.GetExtension(current ?? string.Empty).TrimStart('.');
        FilePickerRequest request = field.FileRef == FileRef.SoundId
            ? new("Pick a sound bank", Extension: "spk", Folder: "soundbinary", InitialPath: current, NeedsRealPath: true)
            : new("Pick a file",
                Extension: extension.Length > 0 ? extension : null,
                InitialPath: current,
                InitialHash: field.FileRef == FileRef.PathHash && field.IsValid ? (uint)field.Value : null,
                NeedsRealPath: field.FileRef == FileRef.Path);
        if (FilePicker.Show(button, request) is not { } file)
        {
            return;
        }

        // A bank is named after its id, so the picked file's name is the id to store.
        field.Text = field.FileRef switch
        {
            FileRef.SoundId when uint.TryParse(Path.GetFileNameWithoutExtension(file.Path), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out uint id) => ScalarField.SoundIdText(id),
            FileRef.SoundId => field.Text,
            FileRef.PathHash => FcbFieldFormat.Format(FcbMemberType.Hash, file.EngineHash),
            _ => file.Path,
        };
    }

    /// <summary>Plays what the field names now, in a player opened under the field on first use.</summary>
    private void PlaySound_Click(object sender, RoutedEventArgs e)
    {
        var button = (FrameworkElement)sender;
        if (button.DataContext is not ScalarField { SoundId: { } id } || button.Tag is not ContentControl host)
        {
            return;
        }

        if (host.Content is not AudioPreviewPanel player)
        {
            host.Content = player = new AudioPreviewPanel { Margin = new Thickness(0, 4, 0, 0) };
        }
        player.Play(() => SoundPreview.SoundIdToTempWavAsync(id, FilePicker.ResolveSound, FilePicker.Read));
    }

    private void AddNumberArrayItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is NumberArrayGroup group)
        {
            group.AddItem();
        }
    }

    private void RemoveNumberArrayItem_Click(object sender, RoutedEventArgs e)
    {
        var button = (FrameworkElement)sender;
        if (button.Tag is ScalarField item && TreeViewBehaviors.FindAncestorDataContext<NumberArrayGroup>(button) is { } group)
        {
            group.RemoveItem(item);
        }
    }

    private void AddBoolArrayItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is BoolArrayGroup group)
        {
            group.AddItem();
        }
    }

    private void RemoveBoolArrayItem_Click(object sender, RoutedEventArgs e)
    {
        var button = (FrameworkElement)sender;
        if (button.Tag is BoolField item && TreeViewBehaviors.FindAncestorDataContext<BoolArrayGroup>(button) is { } group)
        {
            group.RemoveItem(item);
        }
    }

    private void AddVectorArrayItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is VectorArrayGroup group)
        {
            group.AddItem();
        }
    }

    private void RemoveVectorArrayItem_Click(object sender, RoutedEventArgs e)
    {
        var button = (FrameworkElement)sender;
        if (button.Tag is ScalarField item && TreeViewBehaviors.FindAncestorDataContext<VectorArrayGroup>(button) is { } group)
        {
            group.RemoveItem(item);
        }
    }
}
