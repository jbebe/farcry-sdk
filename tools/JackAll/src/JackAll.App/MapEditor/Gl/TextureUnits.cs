namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// The texture units the shared GLSL blocks bind on. Which sampler sits on which unit is something
/// every program has to agree about, and it was being tracked by comments in one file reasoning
/// about the bindings in another - the terrain claims 0-8 and the model shader 0-2, so anything
/// shared has to start above both.
/// </summary>
public static class TextureUnits
{
    /// <summary>First unit no layer binds for itself.</summary>
    private const int Shared = 9;

    /// <summary>The cascade array, for <see cref="SceneLighting.ShadowGlsl"/>.</summary>
    public const int ShadowMap = Shared;

    /// <summary>The occlusion buffer, for the lookup in <see cref="SceneLighting.SurfaceGlsl"/>.</summary>
    public const int Occlusion = Shared + 1;
}
