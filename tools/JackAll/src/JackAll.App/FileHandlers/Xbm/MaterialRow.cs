using System.Windows.Media;
using JackAll.App.Picker;
using JackAll.Core.Vfs;

namespace JackAll.App.FileHandlers.Xbm;

/// <summary>One property of a material, set or not; writing <see cref="Text"/> edits the material.</summary>
public sealed class MaterialRow(string key, string text, Action<string, string> set, Func<string, ImageSource?> thumbnail)
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
    public VfsFile? Texture => IsTexture && text.Length > 0 ? FilePicker.FileOfPath(text) : null;

    public ImageSource? Thumbnail => IsTexture && text.Length > 0 ? thumbnail(text) : null;

    public bool HasTexture => Texture is not null;

    public bool HasHint => Hint is not null;

    public bool HasSwatch => Swatch is not null;

    public bool HasProblem => Problem is not null;
}

public sealed record MaterialGroup(string Header, IReadOnlyList<MaterialRow> Rows);
