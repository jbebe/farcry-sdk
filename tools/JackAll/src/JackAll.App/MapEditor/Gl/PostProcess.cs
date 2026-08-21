using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// The step from scene radiance to a displayable image: exposure, a tonemap, and the sRGB encode.
/// Exposure lives here rather than in each layer, which is what stops the sky and the water drifting
/// out of step with the ground the way they did while every shader applied it for itself.
/// </summary>
public sealed class PostProcess : IDisposable
{
    private readonly ShaderProgram _program;
    private readonly int _vao;
    private readonly int _uExposure;
    private readonly int _uDemo;

    public PostProcess()
    {
        _vao = GL.GenVertexArray();
        _program = new ShaderProgram(
            ShaderProgram.FullScreenTriangleVertex,
            $$"""
            #version 330 core
            in vec2 uv;
            uniform sampler2D scene;
            uniform float exposure;
            uniform float demo;
            out vec4 fragment;

            {{SceneLighting.ColourGlsl}}

            // Khronos PBR Neutral. Highlights roll off to white while anything in gamut keeps its
            // hue and saturation - ACES tilts bright colour toward its primaries and turns a blown
            // sky cyan, which is a film-stock look laid over the data rather than a neutral one.
            vec3 tonemap(vec3 colour)
            {
                const float startCompression = 0.8 - 0.04;
                const float desaturation = 0.15;

                float darkest = min(colour.r, min(colour.g, colour.b));
                float offset = darkest < 0.08 ? darkest - 6.25 * darkest * darkest : 0.04;
                colour -= offset;

                float peak = max(colour.r, max(colour.g, colour.b));
                if (peak < startCompression)
                {
                    return colour;
                }

                float d = 1.0 - startCompression;
                float newPeak = 1.0 - d * d / (peak + d - startCompression);
                colour *= newPeak / peak;
                return mix(colour, vec3(newPeak),
                           1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0));
            }

            {{ShaderProgram.NoiseGlsl}}

            void main()
            {
                vec3 colour = texture(scene, uv).rgb * exposure;

                // Switched off, the tonemap goes with it: a flat view of the data wants the values
                // it was given, clipped, not a highlight rolloff shaping them.
                colour = mix(clamp(colour, 0.0, 1.0), tonemap(colour), demo);

                vec3 encoded = linearToSrgb(clamp(colour, 0.0, 1.0));
                // Triangular PDF at one 8-bit step, which is what keeps the sky gradient from
                // banding once it is quantised.
                float dither = (interleavedGradientNoise(gl_FragCoord.xy)
                              + interleavedGradientNoise(gl_FragCoord.xy + 17.0) - 1.0) / 255.0;
                fragment = vec4(encoded + dither, 1.0);
            }
            """);
        _uExposure = _program.UniformLocation("exposure");
        _uDemo = _program.UniformLocation("demo");
    }

    /// <summary>Resolves the scene colour into whatever framebuffer is bound. Depth is left alone:
    /// the target shares the scene's depth texture, and the overlays drawn afterwards test against
    /// it.</summary>
    public void Composite(int sceneColour, float demo)
    {
        _program.Use();
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, sceneColour);
        GL.Uniform1(_uExposure, SceneLighting.Exposure);
        GL.Uniform1(_uDemo, demo);

        using GlState pass = new();
        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        _program.Dispose();
        GL.DeleteVertexArray(_vao);
    }
}
