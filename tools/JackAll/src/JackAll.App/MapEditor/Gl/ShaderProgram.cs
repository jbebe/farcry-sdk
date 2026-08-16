using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor.Gl;

/// <summary>A compiled and linked GLSL vertex+fragment program.</summary>
public sealed class ShaderProgram : IDisposable
{
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
