using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// Routes driver diagnostics to the debug output, and driver errors to the crash log. Synchronous
/// only in Debug builds, where the call stack pointing at the offending call is the whole value;
/// elsewhere the driver keeps its own thread.
/// </summary>
public static class GlDebug
{
    /// <summary>Held for the lifetime of the process: the driver keeps the thunk, and a collected
    /// delegate is a crash inside the driver several frames later.</summary>
    private static DebugProc? _callback;

    /// <summary>Latched so a driver without the extension is asked once rather than once a frame -
    /// the extension walk marshals every name in the list.</summary>
    private static bool _tried;

    public static void Install()
    {
        if (_tried)
        {
            return;
        }

        _tried = true;
        if (!HasExtension("GL_KHR_debug"))
        {
            return;
        }

        _callback = OnMessage;
        GL.Enable(EnableCap.DebugOutput);
#if DEBUG
        GL.Enable(EnableCap.DebugOutputSynchronous);
#endif
        GL.DebugMessageControl(DebugSourceControl.DontCare, DebugTypeControl.DontCare,
            DebugSeverityControl.DebugSeverityNotification, 0, (int[]?)null, false);
        GL.DebugMessageCallback(_callback, IntPtr.Zero);
    }

    public static bool HasExtension(string name)
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

    /// <summary>Called on the driver's thread outside Debug builds.</summary>
    private static void OnMessage(
        DebugSource source, DebugType type, int id, DebugSeverity severity,
        int length, IntPtr message, IntPtr userParam)
    {
        bool error = type == DebugType.DebugTypeError || severity == DebugSeverity.DebugSeverityHigh;
#if !DEBUG
        if (!error)
        {
            return;
        }
#endif
        string text = $"GL {severity} {type} [{id}]: {System.Runtime.InteropServices.Marshal.PtrToStringAnsi(message, length)}";
        if (error)
        {
            CrashLog.NoteGlError(text);
        }
        System.Diagnostics.Debug.WriteLine(text);
    }
}
