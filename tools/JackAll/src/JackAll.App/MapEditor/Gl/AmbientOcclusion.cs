using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// Screen-space ambient occlusion over the depth prepass, and the blur that makes it usable. What
/// it produces multiplies the ambient term and nothing else - darkening a sunlit face with it would
/// be the same mistake as painting shadow into an albedo.
/// </summary>
public sealed class AmbientOcclusion : IDisposable
{
    /// <summary>How far a sample reaches, in metres. Roughly a doorway: wide enough to darken under
    /// eaves and into corners, tight enough that a hillside does not occlude the valley.</summary>
    private const float Radius = 2.2f;

    private readonly ShaderProgram _occlusion;
    private readonly ShaderProgram _blur;
    private readonly int _vao;
    private readonly int _uInverseProjection;
    private readonly int _bDirection;

    public AmbientOcclusion()
    {
        _vao = GL.GenVertexArray();

        _occlusion = new ShaderProgram(
            ShaderProgram.FullScreenTriangleVertex,
            $$"""
            #version 330 core
            in vec2 uv;
            uniform sampler2D sceneDepth;
            uniform mat4 inverseProjection;
            out float fragment;

            const float radius = {{Radius:0.0##}};
            // Constant for the process, so it is folded in rather than uploaded every frame.
            const float focalLength = {{1f / MathF.Tan(Camera3D.VerticalFovRadians * 0.5f):0.0####}};
            const int directions = 4;
            const int steps = 6;

            {{ShaderProgram.NoiseGlsl}}

            // Takes the depth rather than sampling it, so the caller that already has it - which is
            // every caller - does not fetch the same texel twice.
            vec3 viewPosition(vec2 texcoord, float depth)
            {
                vec4 clip = vec4(texcoord * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
                vec4 view = inverseProjection * clip;
                return view.xyz / view.w;
            }

            void main()
            {
                float depth = texture(sceneDepth, uv).r;
                if (depth >= 1.0)
                {
                    fragment = 1.0;
                    return;
                }

                vec2 screen = vec2(textureSize(sceneDepth, 0));
                vec3 origin = viewPosition(uv, depth);

                // Taken from the depth buffer rather than a normal target: one less full-screen
                // surface to write, and the derivative is exact everywhere but a silhouette.
                vec3 normal = normalize(cross(dFdx(origin), dFdy(origin)));
                if (normal.z < 0.0)
                {
                    normal = -normal;
                }

                // The view-space radius, in pixels, at this depth.
                float pixelRadius = radius * focalLength * 0.5 * screen.y / max(-origin.z, 0.01);
                pixelRadius = min(pixelRadius, 96.0);

                float occlusion = 0.0;
                float rotation = interleavedGradientNoise(gl_FragCoord.xy) * 6.2831853;
                for (int d = 0; d < directions; d++)
                {
                    float angle = rotation + float(d) * (6.2831853 / float(directions));
                    vec2 march = vec2(cos(angle), sin(angle));

                    // The highest thing this direction can see, as the sine of its elevation over
                    // the surface. Occlusion is how far up the hemisphere that reaches.
                    float horizon = 0.0;
                    for (int s = 1; s <= steps; s++)
                    {
                        vec2 at = uv + march * (pixelRadius * float(s) / float(steps)) / screen;
                        if (any(lessThan(at, vec2(0.0))) || any(greaterThan(at, vec2(1.0))))
                        {
                            break;
                        }

                        vec3 delta = viewPosition(at, texture(sceneDepth, at).r) - origin;
                        float travelled = length(delta);
                        if (travelled < 1.0e-4)
                        {
                            continue;
                        }

                        // Squared falloff, so something well outside the radius stops counting
                        // rather than casting occlusion across the whole scene.
                        float falloff = max(1.0 - dot(delta, delta) / (radius * radius), 0.0);
                        horizon = max(horizon, (dot(delta / travelled, normal) - 0.02) * falloff);
                    }
                    occlusion += horizon;
                }

                fragment = 1.0 - occlusion / float(directions);
            }
            """);
        _uInverseProjection = _occlusion.UniformLocation("inverseProjection");

        _blur = new ShaderProgram(
            ShaderProgram.FullScreenTriangleVertex,
            """
            #version 330 core
            in vec2 uv;
            uniform sampler2D occlusion;
            uniform sampler2D sceneDepth;
            uniform vec2 direction;
            out float fragment;

            void main()
            {
                float centre = texture(sceneDepth, uv).r;
                float sum = 0.0;
                float weight = 0.0;
                for (int i = -3; i <= 3; i++)
                {
                    vec2 at = uv + direction * float(i) / vec2(textureSize(sceneDepth, 0));
                    // Bilateral: samples across a depth edge drop out, so the occlusion under a
                    // rock does not bleed onto the ground behind it.
                    float w = exp(-abs(texture(sceneDepth, at).r - centre) * 8000.0);
                    sum += texture(occlusion, at).r * w;
                    weight += w;
                }
                fragment = sum / weight;
            }
            """);
        _bDirection = _blur.UniformLocation("direction");
        _blur.Use();
        GL.Uniform1(_blur.UniformLocation("occlusion"), 0);
        GL.Uniform1(_blur.UniformLocation("sceneDepth"), 1);
    }

    /// <summary>
    /// Fills the occlusion buffer from the scene depth and returns it. Two blur passes, so the
    /// dither pattern the sampling leaves behind averages out along both axes.
    /// </summary>
    public int Render(RenderTargets targets, Matrix4 projection)
    {
        // This pass brings its own framebuffer and viewport, which is the case the guard exists for
        // - restoring to a remembered state rather than to an assumed one.
        using GlState pass = new();
        Matrix4 inverse = Matrix4.Invert(projection);

        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);
        GL.BindVertexArray(_vao);
        GL.Viewport(0, 0, targets.Width, targets.Height);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, targets.OcclusionFramebuffer);
        _occlusion.Use();
        GL.UniformMatrix4(_uInverseProjection, false, ref inverse);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, targets.Depth);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        _blur.Use();
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, targets.Depth);
        Blur(targets.OcclusionBlurFramebuffer, targets.Occlusion, Vector2.UnitX);
        Blur(targets.OcclusionFramebuffer, targets.OcclusionBlur, Vector2.UnitY);

        GL.BindVertexArray(0);
        GL.ActiveTexture(TextureUnit.Texture0);
        return targets.Occlusion;
    }

    private void Blur(int framebuffer, int source, Vector2 direction)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.Uniform2(_bDirection, direction);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, source);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    public void Dispose()
    {
        _occlusion.Dispose();
        _blur.Dispose();
        GL.DeleteVertexArray(_vao);
    }
}

/// <summary>
/// One program's occlusion uniforms, resolved once at link. Same shape as
/// <see cref="ShadowBinding"/> and for the same reason: the lookup lives in shared GLSL, so the
/// uniforms behind it have to be filled the same way by every program that pastes it.
/// </summary>
public sealed class OcclusionBinding
{
    private readonly int _map;
    private readonly int _strength;

    public OcclusionBinding(ShaderProgram program)
    {
        _map = program.UniformLocation("occlusionMap");
        _strength = program.UniformLocation("occlusionStrength");
    }

    public void Apply()
    {
        if (SceneLighting.OcclusionMap == 0)
        {
            GL.Uniform1(_strength, 0f);
            return;
        }

        GL.ActiveTexture(TextureUnit.Texture0 + TextureUnits.Occlusion);
        GL.BindTexture(TextureTarget.Texture2D, SceneLighting.OcclusionMap);
        GL.Uniform1(_map, TextureUnits.Occlusion);
        GL.Uniform1(_strength, 1f);
        GL.ActiveTexture(TextureUnit.Texture0);
    }
}
