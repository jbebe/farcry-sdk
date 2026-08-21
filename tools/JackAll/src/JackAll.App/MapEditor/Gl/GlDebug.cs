using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// Routes driver diagnostics to the debug output, when the driver offers them. Synchronous, because
/// the whole value is that the call stack still points at the offending call when it fires.
/// </summary>
public static class GlDebug
{
    /// <summary>Held for the lifetime of the process: the driver keeps the thunk, and a collected
    /// delegate is a crash inside the driver several frames later.</summary>
    private static DebugProc? _callback;

    public static void Install()
    {
        if (_callback is not null || !HasExtension("GL_KHR_debug"))
        {
            return;
        }

        _callback = OnMessage;
        GL.Enable(EnableCap.DebugOutput);
        GL.Enable(EnableCap.DebugOutputSynchronous);
        GL.DebugMessageCallback(_callback, IntPtr.Zero);
    }

    private static bool HasExtension(string name)
    {
        GL.GetInteger(GetPName.NumExtensions, out int count);
        for (int i = 0; i < count; i++)
        {
            if (GL.GetString(StringNameIndexed.Extensions, i) == name)
            {
                return true;
            }
        }
        return false;
    }

    private static void OnMessage(
        DebugSource source, DebugType type, int id, DebugSeverity severity,
        int length, IntPtr message, IntPtr userParam)
    {
        if (severity == DebugSeverity.DebugSeverityNotification)
        {
            return;
        }

        string text = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(message, length);
        System.Diagnostics.Debug.WriteLine($"GL {severity} {type} [{id}]: {text}");
    }
}
