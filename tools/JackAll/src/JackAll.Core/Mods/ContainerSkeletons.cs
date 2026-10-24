using System.Text;
using System.Text.RegularExpressions;
using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

/// <summary>
/// The empty container a fragment-only container is assembled onto when no archive or layer ships it
/// as a file: a new world's sector or entity library before it is packed.
/// </summary>
public static partial class ContainerSkeletons
{
    [GeneratedRegex(@"(^|\\)worldsector(?<id>\d+)\.data\.fcb$", RegexOptions.IgnoreCase)]
    private static partial Regex WorldSectorPattern();

    [GeneratedRegex(@"^worlds\\[^\\]+\\generated\\entitylibrary\.fcb$", RegexOptions.IgnoreCase)]
    private static partial Regex WorldLibraryPattern();

    /// <summary>The skeleton for <paramref name="containerPath"/>, or null for a kind that has none.</summary>
    public static FcbObject? For(string containerPath)
    {
        if (WorldSectorPattern().Match(containerPath) is { Success: true } sector)
        {
            return WorldSector(int.Parse(sector.Groups["id"].Value));
        }
        return WorldLibraryPattern().IsMatch(containerPath) ? EntityLibrary() : null;
    }

    /// <summary>A sector with nothing in it but its always-on <c>main</c> mission layer.</summary>
    public static FcbObject WorldSector(int id)
    {
        var layer = new FcbObject { TypeHash = WorldHashes.MissionLayer };
        layer.Values[WorldHashes.TextPathId] = FcbWire.NullTerminate(Encoding.UTF8.GetBytes(MissionLayers.MainName));
        layer.Values[WorldHashes.PathId] = BitConverter.GetBytes(FcbClassDefinitions.Crc32Ascii(MissionLayers.MainName));

        var root = new FcbObject { TypeHash = WorldHashes.WorldSector };
        root.Values[WorldHashes.WorldSectorId] = BitConverter.GetBytes((uint)id);
        root.Children.Add(layer);
        return root;
    }

    /// <summary>A library with no groups; an added archetype creates the group its name starts with.</summary>
    public static FcbObject EntityLibrary() => new() { TypeHash = WorldHashes.EntityLibraries };
}
