using System.IO;
using System.Windows.Media;
using JackAll.App.FileHandlers.Xbt;

namespace JackAll.App.Mgb;

/// <summary>
/// Turns a material's authored texture path into the archive entry it names, and decodes that for
/// preview.
/// </summary>
/// <remarks>
/// A material stores something like <c>\textures\360\360_a.png</c> - rooted at the UI package's own
/// tree, and still carrying the source artist's <c>.png</c> extension. What actually ships is
/// <c>ui\textures\360\360_a.xbt</c>. So the mapping is: drop the leading separator, prefix
/// <c>ui\</c>, and swap the extension for <c>.xbt</c>.
///
/// That rule resolves all 948 distinct material texture paths across the 500 vanilla packages -
/// checked by hashing each candidate and looking it up in <c>common.fat</c>, no misses - which is
/// what makes it worth doing as a plain string transform rather than a search.
/// </remarks>
public sealed class MgbTextureResolver(Func<string, byte[]?> readByPath)
{
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The decoded texture, or null if the path is empty, names nothing in the merged
    /// filesystem, or doesn't decode. Cached per resolver (i.e. per editor tab) because a package
    /// reuses the same handful of atlases across hundreds of materials.</summary>
    public ImageSource? Resolve(string? texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
        {
            return null;
        }
        if (_cache.TryGetValue(texturePath, out ImageSource? cached))
        {
            return cached;
        }

        ImageSource? image = Load(texturePath);
        _cache[texturePath] = image;
        return image;
    }

    /// <summary>The archive path a material's texture path names - exposed so the UI can show where
    /// it looked when nothing came back.</summary>
    public static string ToArchivePath(string texturePath)
        => Path.ChangeExtension(@"ui\" + texturePath.TrimStart('\\', '/').Replace('/', '\\'), ".xbt");

    private ImageSource? Load(string texturePath)
    {
        try
        {
            byte[]? xbt = readByPath(ToArchivePath(texturePath));
            return xbt is null ? null : XbtImage.TryDecode(xbt, out _);
        }
        catch
        {
            return null; // nothing in the merged filesystem provides it - same outcome as a bad decode
        }
    }
}
