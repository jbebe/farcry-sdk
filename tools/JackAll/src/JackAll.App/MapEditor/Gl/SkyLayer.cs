using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// A procedural sky: one full-screen triangle whose fragments unproject to a view ray, shaded as a
/// horizon-to-zenith gradient with a sun disc and glow. No geometry, no textures, no cubemap - it
/// replaces the flat clear colour for the cost of three vertices.
/// </summary>
public sealed class SkyLayer : IDisposable
{
    private readonly ShaderProgram _program;
    private readonly int _vao;
    private readonly int _uInverseViewProjection;
    private readonly int _uCameraPosition;
    private readonly int _uSunDirection;

    public SkyLayer()
    {
        _vao = GL.GenVertexArray();
        _program = new ShaderProgram(
            """
            #version 330 core
            out vec2 ndc;
            void main()
            {
                // One oversized triangle covering the screen; cheaper than a quad and seamless.
                vec2 corner = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
                ndc = corner * 2.0 - 1.0;
                gl_Position = vec4(ndc, 1.0, 1.0);
            }
            """,
            $$"""
            #version 330 core
            in vec2 ndc;
            uniform mat4 inverseViewProjection;
            uniform vec3 cameraPosition;
            uniform vec3 sunDirection;
            out vec4 fragment;

            const vec3 zenith  = vec3({{F(SceneLighting.Zenith)}});
            const vec3 horizon = vec3({{F(SceneLighting.Horizon)}});
            const vec3 nadir   = vec3({{F(SceneLighting.Nadir)}});
            const vec3 sunTint = vec3({{F(SceneLighting.SunColour)}});

            void main()
            {
                vec4 far = inverseViewProjection * vec4(ndc, 1.0, 1.0);
                vec3 ray = normalize(far.xyz / far.w - cameraPosition);

                // The gradient is deliberately compressed near the horizon, where a linear ramp
                // reads as a flat wash rather than atmosphere.
                vec3 sky = ray.z >= 0.0
                    ? mix(horizon, zenith, pow(clamp(ray.z, 0.0, 1.0), 0.42))
                    : mix(horizon, nadir, clamp(-ray.z * 2.5, 0.0, 1.0));

                float toSun = max(dot(ray, sunDirection), 0.0);
                sky += sunTint * pow(toSun, 1800.0) * 6.0;
                sky += sunTint * pow(toSun, 8.0) * 0.30;

                fragment = vec4(sky, 1.0);
            }
            """);
        _uInverseViewProjection = _program.UniformLocation("inverseViewProjection");
        _uCameraPosition = _program.UniformLocation("cameraPosition");
        _uSunDirection = _program.UniformLocation("sunDirection");
    }

    private static string F(Vector3 v) =>
        System.FormattableString.Invariant($"{v.X:0.####}, {v.Y:0.####}, {v.Z:0.####}");

    /// <summary>Drawn before the scene with depth writes off, so it fills whatever the terrain does
    /// not cover without ever occluding it.</summary>
    public void Draw(Matrix4 viewProjection, Vector3 cameraPosition)
    {
        Matrix4 inverse = Matrix4.Invert(viewProjection);

        _program.Use();
        GL.UniformMatrix4(_uInverseViewProjection, false, ref inverse);
        GL.Uniform3(_uCameraPosition, cameraPosition);
        GL.Uniform3(_uSunDirection, SceneLighting.SunDirection);

        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.DepthMask(true);
        GL.Enable(EnableCap.DepthTest);
    }

    public void Dispose()
    {
        _program.Dispose();
        GL.DeleteVertexArray(_vao);
    }
}
