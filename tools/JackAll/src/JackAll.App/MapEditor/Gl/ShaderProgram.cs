using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor.Gl;

/// <summary>A compiled and linked GLSL vertex+fragment program.</summary>
public sealed class ShaderProgram : IDisposable
{
    /// <summary>One oversized triangle covering the screen from <c>gl_VertexID</c> - prepend to a
    /// vertex shader, draw 3 vertices, and call <c>screenCorner()</c> for its [0,1] uv. Cheaper than
    /// a quad and, unlike two triangles, with no diagonal seam through the derivative quads.</summary>
    public const string FullScreenTriangleGlsl =
        """
        vec2 screenCorner()
        {
            return vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
        }
        """;

    /// <summary>The whole vertex stage for a pass that just wants the screen and its uv - which is
    /// every post pass. <see cref="FullScreenTriangleGlsl"/> is for the ones that want the corner
    /// but not this main, like the sky unprojecting it to a view ray.</summary>
    public const string FullScreenTriangleVertex =
        $$"""
        #version 330 core
        {{FullScreenTriangleGlsl}}
        out vec2 uv;
        void main()
        {
            uv = screenCorner();
            gl_Position = vec4(uv * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    /// <summary>Interleaved gradient noise - one value per pixel with no transcendentals, in a
    /// pattern a blur can actually average out where white noise cannot. Used both to rotate a
    /// sampling kernel and to dither a gradient on its way to 8 bits.</summary>
    public const string NoiseGlsl =
        """
        float interleavedGradientNoise(vec2 position)
        {
            return fract(52.9829189 * fract(dot(position, vec2(0.06711056, 0.00583715))));
        }
        """;

    /// <summary>Unit-square corner from gl_VertexID for buffer-less quad draws - prepend to a
    /// vertex shader and call <c>quadCorner()</c> (two triangles, 6 vertices, corners in [0,1]).</summary>
    public const string QuadCornerGlsl =
        """
        vec2 quadCorner()
        {
            return vec2((gl_VertexID == 1 || gl_VertexID == 4 || gl_VertexID == 5) ? 1.0 : 0.0,
                        (gl_VertexID == 2 || gl_VertexID == 3 || gl_VertexID == 5) ? 1.0 : 0.0);
        }
        """;

    private readonly int _handle;

    public ShaderProgram(string vertexSource, string fragmentSource)
    {
        int vertex = Compile(ShaderType.VertexShader, vertexSource);
        int fragment = Compile(ShaderType.FragmentShader, fragmentSource);

        _handle = GL.CreateProgram();
        GL.AttachShader(_handle, vertex);
        GL.AttachShader(_handle, fragment);
        GL.LinkProgram(_handle);
        GL.GetProgram(_handle, GetProgramParameterName.LinkStatus, out int linked);
        if (linked == 0)
        {
            string log = GL.GetProgramInfoLog(_handle);
            GL.DeleteProgram(_handle);
            throw new InvalidOperationException($"GLSL link failed: {log}");
        }

        // Linked programs keep their own copy; the shader objects are dead weight from here on.
        GL.DetachShader(_handle, vertex);
        GL.DetachShader(_handle, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
    }

    public void Use() => GL.UseProgram(_handle);

    public int UniformLocation(string name) => GL.GetUniformLocation(_handle, name);

    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string log = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            throw new InvalidOperationException($"{type} compile failed: {log}");
        }
        return shader;
    }

    public void Dispose() => GL.DeleteProgram(_handle);
}
