using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor.Gl;

/// <summary>Sampler state shared by every layer that draws a mipmapped surface.</summary>
public static class GlSampling
{
    private static float? _maxAnisotropy;

    /// <summary>
    /// Turns on anisotropic filtering for the texture currently bound to <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// Ground and walls are nearly always seen edge-on, and there trilinear filtering has to pick one
    /// mip level for a footprint that is many times longer in one axis than the other - so it picks
    /// the blurry one, and everything more than a few metres out turns to mush. This is the single
    /// cheapest thing that makes a close-up view look like the game's.
    /// </remarks>
    public static void Anisotropic(TextureTarget target)
    {
        // Core since GL 4.6 and an EXT everywhere before that, but the limit is still worth asking
        // for rather than assuming: drivers cap it, and asking past the cap is an error. The floor of
        // 1 - plain trilinear - covers a driver without the extension at all, where the query leaves
        // the value at zero and anything below 1 would itself be rejected.
        _maxAnisotropy ??= GL.GetFloat((GetPName)All.MaxTextureMaxAnisotropy);
        GL.TexParameter(target, (TextureParameterName)All.TextureMaxAnisotropy,
            Math.Clamp(_maxAnisotropy.Value, 1f, 16f));
    }
}
