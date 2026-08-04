namespace JackAll.Tools.Format;

/// <summary>
/// Byte-preserving edits to an <b>already-real</b> <c>.mgb</c> file: locate a top-level <c>Page</c>
/// via <see cref="MgbBody"/>'s own opt-in <see cref="MgbAreaLocation"/> trace, then splice a new
/// <c>Button</c> element into its children list in place, incrementing its <c>elementCount</c> field.
/// Everything else in the file - bytes before and after the splice point - is copied through
/// completely unmodified, so this never needs to (re-)understand any part of the format this project's
/// parser doesn't already decode (see docs/docs/file-formats/mgb.md's "Next direction" note on this
/// exact approach).
/// </summary>
public static class MgbPageEditor
{
    /// <summary>
    /// Appends a new <c>Button</c> as the last child of the <paramref name="topLevelPageIndex"/>-th
    /// top-level <c>Page</c> this parser can fully reach (0-based, counting only <c>Page</c>s the
    /// parser actually got all the way through before any error - see <see cref="MgbBody.ParsePackage(byte[], MgbHeader, List{MgbAreaLocation}?)"/>'s
    /// graceful-degradation behavior). Requires the file's own header type table to already declare
    /// <c>"Button"</c> - this method never grows the type table itself, since that would shift every
    /// byte offset after it and invalidate the very trace it just used to find the splice point.
    /// <see cref="MgbFileBuilder.BuildModsPage"/> already includes <c>"Button"</c> for exactly this
    /// reason; a real shipped file's own type table is normally the fixed, build-wide superset
    /// documented in mgb.md, which already includes it too.
    /// </summary>
    public static byte[] AddButtonToTopLevelPage(byte[] original, int topLevelPageIndex, string buttonLabel, MgbBox box)
    {
        MgbHeader header = MgbHeader.Decode(original);
        var trace = new List<MgbAreaLocation>();
        try
        {
            MgbBody.ParsePackage(original, header, trace);
        }
        catch
        {
            // A partial trace (everything located before the failure) is still useful - MgbBody's own
            // read path degrades the same way for the same reason (see its ParsePackage remarks).
        }

        List<MgbAreaLocation> topLevelPages = trace.Where(t => t.IsTopLevel && t.Kind == "Page").ToList();
        if (topLevelPageIndex < 0 || topLevelPageIndex >= topLevelPages.Count)
        {
            throw new InvalidOperationException(
                $"File only has {topLevelPages.Count} fully-parsed top-level Page(s) (index {topLevelPageIndex} requested) - " +
                "either the file genuinely doesn't have that many, or this parser's own coverage gap " +
                "(see docs/docs/file-formats/mgb.md's Unknowns) stopped before reaching it.");
        }
        MgbAreaLocation target = topLevelPages[topLevelPageIndex];

        int buttonTableIndex = header.Types.ToList().FindIndex(t => t.Name == "Button");
        if (buttonTableIndex < 0)
        {
            throw new InvalidOperationException(
                "This file's own type table doesn't declare \"Button\" - MgbPageEditor never grows the " +
                "type table itself (that would shift every offset after it), so the file must already " +
                "include it. MgbFileBuilder.BuildModsPage does this by default.");
        }
        byte buttonTypeId = (byte)(buttonTableIndex + 1); // confirmed off-by-one, see MgbBody.ResolveTypeTableEntry

        var elementWriter = new MgbWriter();
        MgbFileBuilder.WriteButton(elementWriter, buttonTypeId, MgbTypeTable.ComputeHash(buttonLabel), box);
        byte[] newButtonBytes = elementWriter.ToArray();

        var result = new byte[original.Length + newButtonBytes.Length];
        int existingChildrenBytes = target.ChildrenEndOffset - (target.ElementCountFieldOffset + 4);

        Array.Copy(original, 0, result, 0, target.ElementCountFieldOffset);
        WriteU32LE(result, target.ElementCountFieldOffset, target.ElementCount + 1);
        Array.Copy(original, target.ElementCountFieldOffset + 4, result, target.ElementCountFieldOffset + 4, existingChildrenBytes);
        Array.Copy(newButtonBytes, 0, result, target.ChildrenEndOffset, newButtonBytes.Length);
        Array.Copy(original, target.ChildrenEndOffset, result, target.ChildrenEndOffset + newButtonBytes.Length, original.Length - target.ChildrenEndOffset);

        return result;
    }

    private static void WriteU32LE(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }
}
