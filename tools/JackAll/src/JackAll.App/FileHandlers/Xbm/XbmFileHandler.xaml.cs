using System.Text;
using System.Windows.Controls;
using JackAll.Core.Format;

namespace JackAll.App.FileHandlers.Xbm;

/// <summary>
/// The file handler for .xbm materials - read-only. Shows the material name, shader template, every
/// texture slot binding (the .xbt each is wired to), and every other shader parameter (tiling, color,
/// specular power, flags) decoded by <see cref="XbmMaterial"/>. This is the missing link between the
/// .xbg mesh viewer (which only knows a submesh's material *name*) and the .xbt texture handler - once
/// this exists, resolving a mesh's actual textures is a materials-folder lookup by that name away.
/// </summary>
public partial class XbmFileHandler : UserControl
{
    public XbmFileHandler(string fileName, byte[] content)
    {
        InitializeComponent();
        Load(fileName, content);
    }

    private void Load(string fileName, byte[] content)
    {
        try
        {
            XbmMaterial material = XbmMaterial.Parse(content);

            var sb = new StringBuilder();
            sb.AppendLine(fileName);
            sb.AppendLine();
            sb.AppendLine($"Name:     {material.Name}");
            sb.AppendLine($"Template: {material.Template}");
            sb.AppendLine();

            sb.AppendLine($"--- Textures ({material.Textures.Count}) ---");
            sb.AppendLine();
            if (material.Textures.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (XbmProperty tex in material.Textures)
                {
                    sb.AppendLine($"  {tex.Key,-24} {tex.Value}");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"--- Properties ({material.Properties.Count}) ---");
            sb.AppendLine();
            foreach (XbmProperty prop in material.Properties)
            {
                sb.AppendLine($"  {prop.Key,-28} {prop.Value}");
            }

            StatusText.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't read this file: {ex.Message}";
        }
    }
}
