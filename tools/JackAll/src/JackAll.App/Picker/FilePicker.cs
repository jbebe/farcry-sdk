using System.Windows;
using JackAll.Core.Vfs;

namespace JackAll.App.Picker;

/// <summary>
/// What a caller asks the picker for. <paramref name="Extension"/> narrows the list by default,
/// <paramref name="Folder"/> limits it outright, and <paramref name="NeedsRealPath"/> leaves out files
/// with no recovered name, whose <c>_unknown\</c> path is JackAll's own and hashes to nothing.
/// </summary>
public sealed record FilePickerRequest(
    string Title,
    string? Extension = null,
    string? Folder = null,
    string? InitialPath = null,
    uint? InitialHash = null,
    bool NeedsRealPath = false);

/// <summary>Opens the archive file picker from anywhere in the app.</summary>
public static class FilePicker
{
    private static MainViewModel? _source;

    /// <summary>Points the picker at the merged filesystem. Nothing is read until it opens.</summary>
    public static void UseSource(MainViewModel vm) => _source = vm;

    /// <inheritdoc cref="MainViewModel.PathOf"/>
    public static string? PathOf(uint hash) => _source?.PathOf(hash);

    public static VfsFile? FileOf(uint hash) => _source?.FindByHash(hash);

    public static byte[] Read(VfsFile file) => _source!.Read(file);

    /// <inheritdoc cref="MainViewModel.ResolveSoundResource"/>
    public static VfsFile? ResolveSound(uint id) => _source?.ResolveSoundResource(id);

    /// <summary>The bank a sound id names, or the one that defines it.</summary>
    public static string? SoundBankOf(uint id) => ResolveSound(id)?.Path;

    /// <summary>Shows the picker over the window holding <paramref name="anchor"/>; null when cancelled.</summary>
    public static VfsFile? Show(DependencyObject anchor, FilePickerRequest request)
    {
        var window = new FilePickerWindow(new FilePickerViewModel(_source!, request)) { Owner = Window.GetWindow(anchor) };
        return window.ShowDialog() == true ? window.Result : null;
    }
}
