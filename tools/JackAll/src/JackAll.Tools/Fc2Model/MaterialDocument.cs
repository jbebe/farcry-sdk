using JackAll.Tools.Xbg;
using JackAll.Tools.Xbm;

namespace JackAll.Tools.Fc2Model;

public sealed class MaterialTexture
{
    public required string Slot { get; init; }

    public required string Path { get; init; }
}

public sealed class MaterialFloats
{
    public required string Key { get; init; }

    public required float[] Value { get; init; }
}

public sealed class MaterialInteger
{
    public required string Key { get; init; }

    public required uint Value { get; init; }
}

/// <summary>
/// A material with no Dunia bytes in it: a shader name, its texture slots and its properties.
/// </summary>
/// <remarks>
/// A standalone <c>.xbm</c> is the mesh container carrying a material chunk and an empty mesh, and
/// that empty mesh is the same in all 2,379 shipped materials - no nodes, no parts, no materials
/// list, one empty LOD, bounds memset to <c>0x7F</c>, and fixed quantisation scales. So a material
/// needs to carry none of it: the container is rebuilt from <see cref="XbmFile.Template"/> and the one word
/// nothing derives.
/// <para>
/// The three property lists are ordered, not keyed. One shipped material repeats a key inside a
/// section, so a map loses it and a writer built from a map cannot put the file back.
/// </para>
/// </remarks>
public sealed class MaterialDocument
{
    /// <summary>The one value in a material's container that nothing derives.</summary>
    public required uint HeaderWord { get; init; }

    public required string Name { get; set; }

    public required string Shader { get; init; }

    /// <summary>The material body's first field, a string the engine discards; empty in every shipped
    /// material, so these are always five zero bytes.</summary>
    public required byte[] Preamble { get; init; }

    /// <summary>The count of boolean properties, 0 in every shipped material.</summary>
    public required uint Trailing { get; init; }

    public List<MaterialTexture> Textures { get; init; } = [];

    public List<MaterialFloats> Floats { get; init; } = [];

    public List<MaterialInteger> Integers { get; init; } = [];

    public static MaterialDocument From(XbmFile material, XbgFile container)
    {
        var document = new MaterialDocument
        {
            HeaderWord = container.HeaderWords[0],
            Name = material.Name,
            Shader = material.Shader,
            Preamble = [.. material.Preamble],
            Trailing = material.BooleanCount,
        };

        foreach (XbmEntry entry in material.Entries)
        {
            switch (entry.Section)
            {
                case XbmSection.Texture:
                    document.Textures.Add(new MaterialTexture { Slot = entry.Key, Path = entry.Path });
                    break;
                case XbmSection.Float:
                    document.Floats.Add(new MaterialFloats { Key = entry.Key, Value = [.. entry.Floats] });
                    break;
                default:
                    document.Integers.Add(new MaterialInteger { Key = entry.Key, Value = entry.Integer });
                    break;
            }
        }
        return document;
    }

    public static MaterialDocument Parse(byte[] xbm)
    {
        XbmFile material = XbmFile.Parse(xbm);
        return From(material, material.Container!);
    }

    /// <summary>The material as an `.xbm`, rebuilt around the container template.</summary>
    public byte[] ToXbm()
    {
        XbmFile material = XbmFile.Create(Name, Shader, HeaderWord);
        material.Preamble = [.. Preamble];
        material.BooleanCount = Trailing;
        foreach (MaterialTexture texture in Textures)
        {
            material.Add(new XbmEntry
            {
                Section = XbmSection.Texture, Key = texture.Slot, Path = texture.Path,
            });
        }
        foreach (MaterialFloats floats in Floats)
        {
            material.Add(new XbmEntry
            {
                Section = XbmSection.Float, Key = floats.Key, Floats = [.. floats.Value],
            });
        }
        foreach (MaterialInteger integer in Integers)
        {
            material.Add(new XbmEntry
            {
                Section = XbmSection.Integer, Key = integer.Key, Integer = integer.Value,
            });
        }

        return material.Write();
    }
}
