using System.Windows.Media;
using JackAll.App.FileHandlers.Xbt;
using JackAll.App.Picker;
using JackAll.Core.Format;
using JackAll.Core.Vfs;

namespace JackAll.App.FileHandlers.Xbm;

/// <summary>One property of a material, set or not; writing <see cref="Text"/> edits the material.</summary>
public sealed class MaterialRow(string key, string text, Action<string, string> set)
{
    public string Key => key;

    public string Text
    {
        get => text;
        set => set(key, value);
    }

    public bool IsTexture { get; init; }

    /// <summary>Why the row is empty, with the shader's default when it names one.</summary>
    public string? Hint { get; init; }

    public Brush? Swatch { get; init; }

    public string? Problem { get; init; }

    /// <summary>The texture this row names, when the game or the workspace has it.</summary>
    public VfsFile? Texture => IsTexture && text.Length > 0 ? FilePicker.FileOf(NameHash.Compute(text)) : null;

    public ImageSource? Thumbnail => Texture is { Type.Extension: "xbt" } file ? XbtImage.Thumbnail(file, FilePicker.Read) : null;
}

public sealed record MaterialGroup(string Header, IReadOnlyList<MaterialRow> Rows);
