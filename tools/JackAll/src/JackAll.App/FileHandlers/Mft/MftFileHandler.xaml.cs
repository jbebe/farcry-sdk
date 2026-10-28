using System.Windows.Controls;
using JackAll.App.FileHandlers.Xbt;
using JackAll.Core.Format;
using JackAll.Tools.Mft;
using JackAll.Tools.Xbt;

namespace JackAll.App.FileHandlers.Mft;

/// <summary>A Magma font: what it holds, and sample text set from the pages beside it.</summary>
public partial class MftFileHandler : UserControl
{
    /// <param name="directory">The folder the font sits in, which its pages sit in too.</param>
    /// <param name="readByPath">The merged filesystem's file at a game path, or null.</param>
    public MftFileHandler(string fileName, string directory, byte[] content, Func<string, byte[]?> readByPath)
    {
        InitializeComponent();
        try
        {
            MftFont font = MftFont.Read(content);
            StatusText.Text = $"{fileName}\n\n{font.Name}: {font.CharacterCount} characters drawn at {font.Size} px "
                + $"on {font.Pages.Count} page(s), from {font.Source}";

            List<FontPage> pages = [];
            for (int i = 0; i < font.Pages.Count; i++)
            {
                string path = font.PagePath(directory, i);
                if (readByPath(path) is not { } xbt || XbtPixels.TryDecode(xbt) is not { } decoded)
                {
                    StatusText.Text += $"\n\nNo preview: its page {path} is missing or doesn't decode.";
                    return;
                }
                pages.Add(FontPage.FromRgba(decoded.Width, decoded.Height, decoded.Rgba));
            }

            (byte[] rgba, int width, int height) = MftPreview.Render(font, pages, MftPreview.SampleText);
            Preview.Source = XbtImage.ToBitmap(rgba, width, height);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't read this file: {ex.Message}";
        }
    }
}
