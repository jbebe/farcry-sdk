using JackAll.Core.Vfs;
using JackAll.Tools.Bark;
using JackAll.Tools.Domino.Nodes;

namespace JackAll.App.FileHandlers.Domino;

/// <summary>What a Domino tab can reach in the rest of JackAll, shared by every Domino tab.</summary>
/// <param name="ReadBytes">Any game-relative path's content, or null.</param>
/// <param name="FindByPath">The file at a game-relative path, or null.</param>
/// <param name="ResolveSound">The bank or stream a sound ID lives in, or null.</param>
/// <param name="FindEntity">A placed entity's fragment row by <c>disEntityId</c>, or null.</param>
/// <param name="ShowInFiles">Selects a file in the Files tab and switches to it.</param>
/// <param name="OpenInEditor">Opens a fragment row in the XML editor.</param>
/// <param name="OpenGraph">Opens another Domino graph in its own tab.</param>
public sealed record DominoServices(
    Func<string, byte[]?> ReadBytes,
    Func<VfsFile, byte[]> Read,
    Func<string, VfsFile?> FindByPath,
    Func<uint, VfsFile?> ResolveSound,
    Func<ulong, VfsFile?> FindEntity,
    Action<VfsFile> ShowInFiles,
    Action<VfsFile> OpenInEditor,
    Action<VfsFile> OpenGraph)
{
    public string? ReadText(string path) => ReadBytes(path) is { } bytes ? AppText.DecodeUtf8(bytes) : null;

    public Lazy<DominoSoundTables> SoundTables { get; } =
        new(() => DominoSoundTables.Load(path => ReadBytes(path) is { } bytes ? AppText.DecodeUtf8(bytes) : null));

    /// <summary>Every bark bank, read in the background on first use; it takes seconds.</summary>
    public Lazy<Task<BarkBankIndex>> Barks { get; } = new(() => Task.Run(() => BarkBankIndex.Load(ReadBytes)));
}
