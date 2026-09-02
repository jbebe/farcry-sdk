using System.Windows.Controls;
using JackAll.App.FileHandlers.DepLoad;
using JackAll.App.FileHandlers.Fcb;
using JackAll.App.FileHandlers.Mgb;
using JackAll.App.FileHandlers.Rml;
using JackAll.App.FileHandlers.Sbao;
using JackAll.App.FileHandlers.Sdat;
using JackAll.App.FileHandlers.Spk;
using JackAll.App.FileHandlers.Text;
using JackAll.App.FileHandlers.Xbg;
using JackAll.App.FileHandlers.Xbm;
using JackAll.App.FileHandlers.Xbt;
using JackAll.Core.Mods;
using JackAll.Core.Vfs;

namespace JackAll.App.FileHandlers;

/// <summary>
/// Picks and builds the preview view for a file's type, if any. New handlers are added here: a
/// case below that constructs the handler's UserControl.
/// </summary>
public static class FileHandlerCatalog
{
    /// <summary>
    /// Above this (either side of a diff, or a plain file), the text/diff views refuse to render the
    /// content at all - a multi-megabyte string is what actually hurts here: the AvalonEdit editor
    /// laying it out, and (for a diff) <c>DiffTextBuilder</c> running a line diff over it. Shared by
    /// <see cref="BuildTextHandler"/>, <see cref="BuildLauncherPreview"/>, and (via this constant)
    /// <see cref="FcbFileHandler"/>'s own content-only-fcb diff, so the limit reads the same everywhere
    /// rather than three independently-chosen numbers.
    /// </summary>
    internal const int MaxPreviewBytes = 500 * 1024;

    /// <summary>
    /// Builds the view for <paramref name="file"/>, or null if no handler covers its type.
    /// <paramref name="readContent"/> is only invoked once a handler is found to actually need it.
    /// <paramref name="replaceContent"/> lets a handler stage an edited replacement into the workspace.
    /// <paramref name="openEditor"/> is only used by the fragment case, to hand off to the host
    /// window's tab-based XML editor rather than anything embedded in this compact preview column.
    /// <paramref name="readOriginal"/> is used by the text, fragment, fcb and rml cases, to diff a
    /// modded file against its
    /// base game version (see <see cref="BuildTextHandler"/>, <see cref="BuildLauncherPreview"/>,
    /// <see cref="FcbFileHandler"/> and <see cref="RmlFileHandler"/>). <paramref name="resolveSoundId"/>
    /// and <paramref name="navigateTo"/> are used by the spk case, for a row's cross-reference to a
    /// different bank entirely (see <see cref="SpkFileHandler"/>); it takes a *sound id*, not a path
    /// hash - the two are different spaces, and resolving one as the other silently finds nothing.
    /// <paramref name="openDominoEditor"/>
    /// is the domino\user\ case's counterpart to <paramref name="openEditor"/>, handing off to the
    /// graph-reconstruction tab, and <paramref name="openMgbEditor"/> is the mgb case's, handing off
    /// to the Magma UI package editor tab (see <see cref="MgbFilePreviewHandler"/>).
    /// </summary>
    public static UserControl? CreateView(
        VfsFile file, Func<byte[]> readContent, Action<byte[]> replaceContent, Action openEditor,
        Func<byte[]?> readOriginal, Func<uint, VfsFile?> resolveSoundId, Action<VfsFile> navigateTo,
        Action openDominoEditor, Action openMgbEditor)
        => file switch
        {
            // A depload fragment is a dependency list, not an `.fcb` value tree, so it gets the plain
            // text/diff view rather than the FCB value editor - which would have nothing to parse.
            { IsFragment: true } when IsDepLoadFragment(file) => BuildTextHandler(file, readContent, readOriginal),
            // Checked before the plain "xml" case below - a fragment's own VfsFile.Type.Extension is
            // also "xml" (see GameVfs.MergeFragments), but it needs the dedicated editor tab, not the
            // generic read-only text viewer.
            { IsFragment: true } => BuildLauncherPreview(file, readContent, readOriginal, openEditor,
                "This is one piece of a splitting .fcb - open it in the XML editor to browse and edit its structure.",
                "Open value editor…", "xml",
                "No changes from the base game - not shown here since a fragment can be huge. Open in FCB Editor to browse it."),
            // Only user\ graphs reconstruct into a box graph worth viewing - a system\ node's own body
            // is just a small hand-written function, already well served by the plain text/diff view.
            { Type.Extension: "lua" } when file.Directory.StartsWith(@"domino\user", StringComparison.OrdinalIgnoreCase)
                => BuildLauncherPreview(file, readContent, readOriginal, openDominoEditor,
                    "A Domino mission-graph script - open it in the graph editor to see it as boxes and connections instead of generated Lua.",
                    "Open in Domino Editor…", "lua"),
            // "desc" is a known-path .mgb.desc (Path.GetExtension only keeps the last segment);
            // "mgb.desc" is the same file content-sniffed by its "<package>" root (see
            // FileTypeSniffer.IdentifyByContent) when no filelist entry named it. Both are plain XML.
            { Type.Extension: "xml" or "lua" or "desc" or "mgb.desc" } => BuildTextHandler(file, readContent, readOriginal),
            { Type.Extension: "xbt" } => new XbtFileHandler(file.FileName, readContent(), replaceContent),
            { Type.Extension: "xbg" } => new XbgFileHandler(file.FileName, readContent()),
            { Type.Extension: "xbm" } => new XbmFileHandler(file.FileName, readContent()),
            { Type.Extension: "sbao" } => new SbaoFileHandler(file.FileName, readContent(), replaceContent),
            { Type.Extension: "fcb" } => new FcbFileHandler(file, readContent(), replaceContent, readOriginal),
            { Type.Extension: "rml" } => new RmlFileHandler(file, readContent(), replaceContent, readOriginal),
            { Type.Extension: "sdat" } => new SdatFileHandler(file.FileName, readContent()),
            { Type.Extension: "spk" } => new SpkFileHandler(file.FileName, readContent(), replaceContent, resolveSoundId, navigateTo),
            { Type.Extension: "mgb" } => new MgbFilePreviewHandler(readContent(), openMgbEditor),
            // Matched by filename suffix, not bare extension - "dat" alone is also the archive-container
            // extension, so this must not fire for anything else that happens to carry a literal .dat
            // extension in the VFS content tree. Sibling "_deploadnewparticles.rml" files are unaffected
            // (routed by their own "rml" extension, checked above).
            { Type.Extension: "dat" } when ContainerFormats.IsDepLoad(file.FileName)
                => new DepLoadFileHandler(file.FileName, readContent()),
            _ => null,
        };

    /// <summary>Whether a fragment row belongs to a `depload.dat` rather than an `.fcb`, read off the
    /// container its staged path names.</summary>
    private static bool IsDepLoadFragment(VfsFile file)
        => ContainerFormats.ContainerPathOf(file.Path) is { } container
        && ContainerFormats.IsDepLoad(container);

    /// <summary>
    /// A plain read-only view for an unmodded (or origin-less) file, or - when <paramref name="file"/>
    /// is modded and has a base game version to compare against - the trimmed diff view
    /// (<see cref="TextFileHandler.CreateDiffView"/>) so the change is visible at a glance instead of
    /// buried in an otherwise-identical file. No size gate here (unlike <see cref="BuildLauncherPreview"/>
    /// and <see cref="FcbFileHandler"/>) - a plain xml/lua file's own view IS its content, so there's
    /// nothing to skip to.
    /// </summary>
    private static TextFileHandler BuildTextHandler(VfsFile file, Func<byte[]> readContent, Func<byte[]?> readOriginal)
    {
        byte[]? currentBytes = TryRead(readContent, out string? readError);
        if (currentBytes is null)
        {
            return new TextFileHandler { Text = readError!, Extension = file.Type.Extension };
        }

        string current = AppText.DecodeUtf8(currentBytes);
        byte[]? originalBytes = TryReadOriginalBytes(file, readOriginal);
        return originalBytes is null
            ? new TextFileHandler { Text = current, Extension = file.Type.Extension }
            : TextFileHandler.CreateDiffView(AppText.DecodeUtf8(originalBytes), current, file.Type.Extension);
    }

    /// <summary>
    /// A launcher-plus-preview view (<see cref="LauncherPreviewHandler"/>): the dedicated editor is
    /// where real navigation happens, but the trimmed diff-against-vanilla view underneath makes a
    /// change visible without opening it. <paramref name="skipUnmodifiedMessage"/>, when given, means
    /// an unmodified file's (possibly huge) content is never even read - the fragment case, where
    /// nothing would change to show anyway; content or base game version over
    /// <see cref="MaxPreviewBytes"/> shows a notice instead, for the same responsiveness reason.
    /// </summary>
    private static LauncherPreviewHandler BuildLauncherPreview(
        VfsFile file, Func<byte[]> readContent, Func<byte[]?> readOriginal, Action openEditor,
        string blurb, string buttonText, string extension, string? skipUnmodifiedMessage = null)
    {
        LauncherPreviewHandler Notice(string text)
            => new(blurb, buttonText, extension, openEditor, null, null, text);

        if (skipUnmodifiedMessage is not null && !file.IsModded)
        {
            return Notice(skipUnmodifiedMessage);
        }

        byte[]? currentBytes = TryRead(readContent, out string? readError);
        if (currentBytes is null)
        {
            return Notice(readError!);
        }

        byte[]? originalBytes = TryReadOriginalBytes(file, readOriginal);
        if (ExceedsPreviewLimit(currentBytes) || (originalBytes is not null && ExceedsPreviewLimit(originalBytes)))
        {
            return Notice(TooLargeMessage(Math.Max(currentBytes.Length, originalBytes?.Length ?? 0)));
        }

        string current = AppText.DecodeUtf8(currentBytes);
        string? originalText = originalBytes is null ? null : AppText.DecodeUtf8(originalBytes);
        return new LauncherPreviewHandler(
            blurb, buttonText, extension, openEditor, current, originalText, previewUnavailableText: null);
    }

    /// <summary>Null when <paramref name="file"/> isn't modded, has no base game version at all, or
    /// <paramref name="readOriginal"/> throws (e.g. the base game archive doesn't have it anymore) -
    /// every case where there's nothing to (usefully) diff against.</summary>
    private static byte[]? TryReadOriginalBytes(VfsFile file, Func<byte[]?> readOriginal)
    {
        if (!file.IsModded)
        {
            return null;
        }

        try
        {
            return readOriginal();
        }
        catch
        {
            return null; // no base game version to diff against - fall through to plain text
        }
    }

    /// <summary>Null (with an error message in <paramref name="errorText"/>) if <paramref name="readContent"/> throws.</summary>
    private static byte[]? TryRead(Func<byte[]> readContent, out string? errorText)
    {
        try
        {
            errorText = null;
            return readContent();
        }
        catch (Exception ex)
        {
            errorText = $"Couldn't read this file: {ex.Message}";
            return null;
        }
    }

    internal static bool ExceedsPreviewLimit(byte[] bytes) => bytes.Length > MaxPreviewBytes;

    /// <summary>The same limit against already-decoded text - what an .fcb's rendered XML is measured
    /// by, since its expanded size is what the editor actually lays out.</summary>
    internal static bool ExceedsPreviewLimit(string text) => text.Length > MaxPreviewBytes;

    internal static string TooLargeMessage(long byteLength)
        => $"This is {byteLength / 1024.0:N0} KB - larger than the {MaxPreviewBytes / 1024:N0} KB preview limit, "
         + "so it isn't shown here to keep the preview responsive.";
}
