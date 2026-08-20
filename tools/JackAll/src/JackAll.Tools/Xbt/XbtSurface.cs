namespace JackAll.Tools.Xbt;

/// <summary>
/// Reads a Dunia <c>.xbt</c> as the one mip chain the engine sees, pulling in the separate
/// <c>_mip0.xbt</c> file when the header names one.
/// </summary>
/// <remarks>
/// Reading the named file alone is not reading the texture. Dunia streams the top mip level out of a
/// sibling file so it can drop the largest level without touching the rest, and 960 of the 1,947
/// textures in the shipped graphics tree are split that way - every terrain layer among them. A
/// reader that stops at the named file gets a texture that is correct in every respect except that
/// it is half the size in each axis, which looks like nothing at all until something is drawn close
/// enough for it to be the only thing you can see.
/// </remarks>
public static class XbtSurface
{
    /// <summary>Null when the bytes are not a readable .xbt or the payload is not block-compressed;
    /// callers wanting the latter decoded fall back to a full BCn decode.</summary>
    /// <param name="readByPath">Resolves the companion's archive-relative path. A companion that
    /// cannot be read is simply not applied, leaving the smaller chain.</param>
    public static DdsSurface? TryRead(byte[] xbt, Func<string, byte[]?> readByPath)
    {
        byte[] header, dds;
        try
        {
            (header, dds) = XbtTexture.Split(xbt);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        if (DdsSurface.TryParse(dds) is not { } surface)
        {
            return null;
        }
        if (XbtTexture.CompanionPath(header) is not { } companion ||
            readByPath(companion) is not { } companionBytes)
        {
            return surface;
        }

        try
        {
            (_, byte[] companionDds) = XbtTexture.Split(companionBytes);
            return DdsSurface.TryParse(companionDds) is { } top ? surface.WithTopLevel(top) : surface;
        }
        catch (InvalidDataException)
        {
            return surface;
        }
    }
}
