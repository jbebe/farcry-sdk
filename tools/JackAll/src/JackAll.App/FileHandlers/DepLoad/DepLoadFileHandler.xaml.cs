using System.Windows.Controls;
using JackAll.Core.Format;

namespace JackAll.App.FileHandlers.DepLoad;

/// <summary>
/// The container's own row preview for a `_depload.dat`. Deliberately just a summary: the file
/// splits per resource, so each of its entries is a row in the tree underneath it - browsable,
/// diffable and mirrorable one at a time, exactly as a splitting `.fcb`'s archetypes are.
/// </summary>
public partial class DepLoadFileHandler : UserControl
{
    public DepLoadFileHandler(string fileName, byte[] content)
    {
        InitializeComponent();
        Load(fileName, content);
    }

    private void Load(string fileName, byte[] content)
    {
        DepLoadFile depLoad;
        try
        {
            depLoad = DepLoadDocument.Decode(content);
        }
        catch (Exception ex)
        {
            HeaderText.Text = $"{fileName}\n\nCouldn't read this file: {ex.Message}";
            return;
        }

        int children = depLoad.Parents.Sum(p => p.Children.Count);
        HeaderText.Text =
            $"{fileName}\n\n{depLoad.Parents.Count:N0} resources, {children:N0} dependencies between them.";

        TypesText.Text = string.Join("\n", depLoad.Parents
            .SelectMany(p => p.Children)
            .GroupBy(c => c.TypeHash)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count(),8:N0}  {DepLoadTypes.NameOf(g.Key) ?? $"unknown type {g.Key:X8}"}"));
    }
}
