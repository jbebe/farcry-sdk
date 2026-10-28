using System.Text;
using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Mods;

namespace JackAll.Tools.Mft;

/// <summary>
/// What a mod layer needs for the game to draw with a built font: its <c>.mft</c> and pages in
/// <c>ui\fonts\</c>, its font and a family of the same name in every <c>fonts.mgb</c>, and its files
/// in every UI dependency list, so the game preloads them. The last two are fragments, which merge
/// with other mods' fonts.
/// </summary>
public static class FontLayer
{
    private const string Folder = @"ui\fonts";
    private static readonly string[] Aspects = ["pc", "pcwidescreen"];
    private static readonly string[] Languages = ["cze", "eng", "fre", "ger", "hun", "ita", "pol", "rus", "spa"];

    /// <summary>Each file by the path a layer stages it at under <c>mods\</c>.</summary>
    /// <param name="readGame">The game's own file at a path, or null when it has none; without the
    /// game, only the font's own files.</param>
    public static SortedDictionary<string, byte[]> Files(BuiltFont built, Func<string, byte[]?> readGame)
    {
        MftFont font = built.Font;
        string mft = $@"{Folder}\{font.Name.ToLowerInvariant()}.mft";
        var files = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { [mft] = font.Write() };
        for (int i = 0; i < built.Pages.Count; i++)
        {
            files[font.PagePath(Folder, i)] = built.Pages[i].ToXbt();
        }
        string[] preload = [.. files.Keys];

        foreach (string ui in Aspects.SelectMany(a => Languages.Select(l => $@"ui\localized\{a}\{l}\ui")))
        {
            if (readGame($@"{ui}\fonts.mgb") is { } package)
            {
                foreach ((string id, XElement section) in Register(package, font.Name, mft))
                {
                    files[$@"{ui}\fonts.mgb\{id}"] = Encoding.UTF8.GetBytes(MgbContainerSplitter.Instance.Canonicalize(id, section.ToString()));
                }
            }

            string desc = $@"{ui}\fonts.mgb.desc";
            if (readGame($@"{ui}\{UiDepLoadContainerSplitter.FileName}") is { } list && Preload(list, desc, preload) is { } resource)
            {
                string id = UiDepLoadContainerSplitter.IdOf(desc);
                files[$@"{ui}\{UiDepLoadContainerSplitter.FileName}\{id}"] =
                    Encoding.UTF8.GetBytes(UiDepLoadContainerSplitter.Instance.Canonicalize(id, resource.ToString()));
            }
        }
        return files;
    }

    /// <summary>The package's font and family lists with this font in them, in place of any of its name.</summary>
    private static IEnumerable<(string Id, XElement Section)> Register(byte[] package, string name, string mft)
    {
        IContainerTree tree = MgbContainerSplitter.Instance.Open(package);
        XElement fonts = XElement.Parse(tree.Extract("_fonts.xml")!);
        XElement stock = fonts.Elements("FONT").FirstOrDefault()
            ?? throw new InvalidDataException("This fonts.mgb has no font to take a font's type from.");
        // A package names a font file from ui\, as \Fonts\<file>.
        Replace(fonts, "FONT", name, new XElement("FONT",
            stock.Attribute("slot"), stock.Attribute("type"),
            new XAttribute("name", name), new XAttribute("file", $@"\Fonts\{Path.GetFileName(mft)}")));
        yield return ("_fonts.xml", fonts);

        XElement families = XElement.Parse(tree.Extract("_fontfamilies.xml")!);
        Replace(families, "FONTFAMILY", name, new XElement("FONTFAMILY",
            new XAttribute("name", name), new XAttribute("font", name), new XAttribute("PACKAGE", "")));
        yield return ("_fontfamilies.xml", families);
    }

    /// <summary>The list's entry for the fonts package with these files among what it preloads, or
    /// null when the list has no such entry.</summary>
    private static XElement? Preload(byte[] list, string descPath, string[] files)
    {
        if (UiDepLoadContainerSplitter.Instance.Open(list).Extract(UiDepLoadContainerSplitter.IdOf(descPath)) is not { } xml)
        {
            return null;
        }

        XElement resource = XElement.Parse(xml);
        foreach (string file in files)
        {
            resource.Elements().Where(e => (string?)e.Attribute("ID") == file).Remove();
            resource.Add(new XElement(file.EndsWith(".mft", StringComparison.Ordinal) ? "CMagmaUIResource" : "CTextureResource",
                new XAttribute("ID", file), new XAttribute("crc_ID", NameHash.Compute(file))));
        }
        return resource;
    }

    private static void Replace(XElement list, string item, string name, XElement entry)
    {
        list.Elements(item).Where(e => (string?)e.Attribute("name") == name).Remove();
        list.Add(entry);
    }
}
