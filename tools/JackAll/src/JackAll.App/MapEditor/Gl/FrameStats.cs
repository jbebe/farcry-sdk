using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// What the viewport's frames cost - interval, CPU and GPU time, scene draws and triangles, the
/// slowest stream rebuild - summed over a short window for the stats line.
/// </summary>
public sealed class FrameStats
{
    private const double WindowSeconds = 0.5;

    /// <summary>Timer queries in flight; a result is read this many frames after it was issued.</summary>
    private const int QueryRing = 4;

    private const int GpuMemoryTotalNvx = 0x9048;
    private const int GpuMemoryAvailableNvx = 0x9049;
    private const int TextureFreeMemoryAti = 0x87FC;

    private static int _draws;
    private static long _triangles;

    private readonly int[] _queries = new int[QueryRing];
    private int _issued;
    private readonly bool _nvidiaMemory;
    private readonly bool _amdMemory;
    private long _frameStart;
    private double _frameRebuild;
    private Window _window;

    /// <summary>The frames summed since the stats line last changed.</summary>
    private struct Window
    {
        public int Frames;
        public double Seconds;
        public double Worst;
        public double Cpu;
        public double Gpu;
        public int GpuSamples;
        public long Draws;
        public long Triangles;
        public double Rebuild;
    }

    public FrameStats()
    {
        GL.GenQueries(QueryRing, _queries);
        _nvidiaMemory = GlDebug.HasExtension("GL_NVX_gpu_memory_info");
        _amdMemory = GlDebug.HasExtension("GL_ATI_meminfo");
        CrashLog.Gpu = $"{GL.GetString(StringName.Renderer)} | {GL.GetString(StringName.Version)} | " +
                       $"{GL.GetString(StringName.Vendor)} | {VideoMemory()}";
    }

    /// <summary>One draw a scene layer issued, and the triangles it rasterises.</summary>
    public static void Drawn(long triangles)
    {
        _draws++;
        _triangles += triangles;
    }

    public void BeginFrame()
    {
        _frameStart = Stopwatch.GetTimestamp();
        _draws = 0;
        _triangles = 0;
        _frameRebuild = 0;

        int query = _queries[_issued % QueryRing];
        if (_issued >= QueryRing)
        {
            GL.GetQueryObject(query, GetQueryObjectParam.QueryResultAvailable, out int available);
            if (available != 0)
            {
                GL.GetQueryObject(query, GetQueryObjectParam.QueryResult, out long nanoseconds);
                _window.Gpu += nanoseconds / 1e6;
                _window.GpuSamples++;
            }
        }
        GL.BeginQuery(QueryTarget.TimeElapsed, query);
    }

    /// <summary>Time spent rebuilding instance streams or overlays this frame.</summary>
    public void Rebuilt(TimeSpan elapsed) => _frameRebuild = elapsed.TotalMilliseconds;

    /// <summary>Closes the frame and returns the stats line when it closes a window, else null. A
    /// frame the viewport did not ask for continuously is reported on its own, without a rate or GPU time.</summary>
    public string? EndFrame(TimeSpan interval, bool continuous)
    {
        GL.EndQuery(QueryTarget.TimeElapsed);
        _issued++;
        double cpu = Stopwatch.GetElapsedTime(_frameStart).TotalMilliseconds;

        if (!continuous)
        {
            _window = default;
            return Line("one frame", cpu, null, _draws, _triangles, _frameRebuild);
        }

        _window.Frames++;
        _window.Seconds += interval.TotalSeconds;
        _window.Worst = Math.Max(_window.Worst, interval.TotalMilliseconds);
        _window.Cpu += cpu;
        _window.Draws += _draws;
        _window.Triangles += _triangles;
        _window.Rebuild = Math.Max(_window.Rebuild, _frameRebuild);
        if (_window.Seconds < WindowSeconds)
        {
            return null;
        }

        Window w = _window;
        _window = default;
        return Line($"{w.Frames / w.Seconds:0} fps · worst {w.Worst:0} ms", w.Cpu / w.Frames,
            w.GpuSamples > 0 ? w.Gpu / w.GpuSamples : null, w.Draws / w.Frames, w.Triangles / w.Frames, w.Rebuild);
    }

    private string Line(string rate, double cpu, double? gpu, long draws, long triangles, double rebuild)
        => $"{rate} · CPU {cpu:0.0} ms" + (gpu is { } g ? $" · GPU {g:0.0} ms" : "") +
           $" · {draws:N0} draws · {triangles / 1e6:0.00}M tris" +
           (rebuild >= 0.05 ? $" · rebuild {rebuild:0.0} ms" : "") + $" · {VideoMemory()}";
    /// <summary>The card's memory in use by everything on it, where the driver says.</summary>
    private string VideoMemory()
    {
        if (_nvidiaMemory)
        {
            GL.GetInteger((GetPName)GpuMemoryTotalNvx, out int total);
            GL.GetInteger((GetPName)GpuMemoryAvailableNvx, out int available);
            return $"VRAM {(total - available) / 1048576.0:0.0}/{total / 1048576.0:0.0} GB";
        }
        if (_amdMemory)
        {
            var free = new int[4];
            GL.GetInteger((GetPName)TextureFreeMemoryAti, free);
            return $"VRAM {free[0] / 1048576.0:0.0} GB free";
        }
        return "VRAM unknown";
    }
}
