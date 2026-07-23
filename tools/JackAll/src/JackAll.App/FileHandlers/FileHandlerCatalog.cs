using System.Windows.Controls;
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
using JackAll.Core.Vfs;

namespace JackAll.App.FileHandlers;

/// <summary>
/// Picks and builds the preview view for a file's type, if any. New handlers are added here: a
/// case below that constructs the handler's UserControl.
/// </summary>
public static class FileHandlerCatalog
{
    private const char Utf8Bom = (char)0xFEFF;

    /// <summary>
    /// Above this (either side of a diff, or a plain file), the text/diff views refuse to render the
    /// content at all - a multi-megabyte string is what actually hurts here: the AvalonEdit editor
    /// laying it out, and (for a diff) <c>DiffTextBuilder</c> running a line diff over it. Shared by
    /// <see cref="BuildTextHandler"/>, <see cref="BuildFragmentHandler"/>, and (via this constant)
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
    /// <paramref name="readOriginal"/> is used by the text and fragment cases, by the fcb case for a
    /// root that doesn't split into sub-files, and by the rml case, to diff a modded file against its
    /// base game version (see <see cref="BuildTextHandler"/>, <see cref="BuildFragmentHandler"/>,
    /// <see cref="FcbFileHandler"/> and <see cref="RmlFileHandler"/>).
    /// </summary>
    public static UserControl? CreateView(
        VfsFile file, Func<byte[]> readContent, Action<byte[]> replaceContent, Action openEditor,
        Func<byte[]?> readOriginal)
        => file switch
        {
            // Checked before the plain "xml" case below - a fragment's own VfsFile.Type.Extension is
            // also "xml" (see GameVfs.MergeFragments), but it needs the dedicated editor tab, not the
            // generic read-only text viewer.
            { IsFragment: true } => BuildFragmentHandler(file, readContent, readOriginal, openEditor),
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
            { Type.Extension: "spk" } => new SpkFileHandler(file.FileName, readContent()),
            { Type.Extension: "mgb" } => new MgbFileHandler(file.FileName, readContent()),
            _ => null,
        };

    /// <summary>
    /// A plain read-only view for an unmodded (or origin-less) file, or - when <paramref name="file"/>
    /// is modded and has a base game version to compare against - the trimmed diff view
    /// (<see cref="TextFileHandler.CreateDiffView"/>) so the change is visible at a glance instead of
    /// buried in an otherwise-identical file. No size gate here (unlike <see cref="BuildFragmentHandler"/>
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

        string current = DecodeText(currentBytes);
        byte[]? originalBytes = TryReadOriginalBytes(file, readOriginal);
        return originalBytes is null
            ? new TextFileHandler { Text = current, Extension = file.Type.Extension }
            : TextFileHandler.CreateDiffView(DecodeText(originalBytes), current, file.Type.Extension);
    }

    /// <summary>
    /// The Files tab's compact preview for one fragment of a splitting .fcb: the "Open in XML
    /// Editor…" launcher (see <see cref="FcbFragmentDetailsHandler"/>'s remarks - fragments can be huge
    /// and need real navigation, a job for the dedicated editor tab, not this column) with the same
    /// trimmed diff-against-vanilla view <see cref="BuildTextHandler"/> gives a plain xml/lua file
    /// underneath it, so a change is visible here too without having to open the editor first. An
    /// unmodified fragment never even has its (possibly huge) content read - nothing would change to
    /// show anyway - and a modified one whose content or base game version is too big (see
    /// <see cref="MaxPreviewBytes"/>) shows neither, same as the unmodified case, just with a different
    /// explanation.
    /// </summary>
    private static FcbFragmentDetailsHandler BuildFragmentHandler(
        VfsFile file, Func<byte[]> readContent, Func<byte[]?> readOriginal, Action openEditor)
    {
        if (!file.IsModded)
        {
            return new FcbFragmentDetailsHandler(openEditor, currentXml: null, originalXml: null,
                "No changes from the base game - not shown here since a fragment can be huge. Open in XML Editor to browse it.");
        }

        byte[]? currentBytes = TryRead(readContent, out string? readError);
        if (currentBytes is null)
        {
            return new FcbFragmentDetailsHandler(openEditor, currentXml: null, originalXml: null, readError!);
        }

        byte[]? originalBytes = TryReadOriginalBytes(file, readOriginal);
        if (ExceedsPreviewLimit(currentBytes) || (originalBytes is not null && ExceedsPreviewLimit(originalBytes)))
        {
            return new FcbFragmentDetailsHandler(openEditor, currentXml: null, originalXml: null,
                TooLargeMessage(Math.Max(currentBytes.Length, originalBytes?.Length ?? 0)));
        }

        string current = DecodeText(currentBytes);
        string? originalText = originalBytes is null ? null : DecodeText(originalBytes);
        return new FcbFragmentDetailsHandler(openEditor, current, originalText, previewUnavailableText: null);
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

    internal static string TooLargeMessage(long byteLength)
        => $"This is {byteLength / 1024.0:N0} KB - larger than the {MaxPreviewBytes / 1024:N0} KB preview limit, "
         + "so it isn't shown here to keep the preview responsive.";

    private static string DecodeText(byte[] bytes)
        => new System.Text.UTF8Encoding(false).GetString(bytes).TrimStart(Utf8Bom);
}
