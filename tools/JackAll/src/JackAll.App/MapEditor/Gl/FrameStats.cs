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

    /// <summary>Timer queries in flight; a result is read this many frames after it was issued, by
    /// which time the GPU has finished it and the read does not stall.</summary>
    private const int QueryRing = 4;

    private const int GpuMemoryTotalNvx = 0x9048;
    private const int GpuMemoryAvailableNvx = 0x9049;
    private const int TextureFreeMemoryAti = 0x87FC;

    private static int _draws;
    private static long _triangles;

    private readonly int[] _queries = new int[QueryRing];
    private readonly bool[] _issued = new bool[QueryRing];
    private int _query;
    private readonly bool _nvidiaMemory;
    private readonly bool _amdMemory;
    private long _frameStart;

    private int _frames;
    private double _seconds;
    private double _worst;
    private double _cpu;
    private double _gpu;
    private int _gpuSamples;
    private long _windowDraws;
    private long _windowTriangles;
    private double _rebuild;
    private double _frameRebuild;

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

        if (_issued[_query])
        {
            GL.GetQueryObject(_queries[_query], GetQueryObjectParam.QueryResultAvailable, out int available);
            if (available != 0)
            {
                GL.GetQueryObject(_queries[_query], GetQueryObjectParam.QueryResult, out long nanoseconds);
                _gpu += nanoseconds / 1e6;
                _gpuSamples++;
            }
        }
        GL.BeginQuery(QueryTarget.TimeElapsed, _queries[_query]);
        _issued[_query] = true;
    }

    /// <summary>Time spent rebuilding instance streams or overlays this frame.</summary>
    public void Rebuilt(TimeSpan elapsed) => _frameRebuild = elapsed.TotalMilliseconds;

    /// <summary>
    /// Closes the frame and returns the stats line when it closes a window, else null. A frame the
    /// viewport did not ask for continuously is reported on its own: it has no rate, and its GPU time
    /// arrives frames later, but its CPU cost is exactly the hitch an edit causes.
    /// </summary>
    public string? EndFrame(TimeSpan interval, bool continuous)
    {
        GL.EndQuery(QueryTarget.TimeElapsed);
        _query = (_query + 1) % QueryRing;
        double cpu = Stopwatch.GetElapsedTime(_frameStart).TotalMilliseconds;

        if (!continuous)
        {
            ResetWindow();
            return Line("one frame", cpu, null, _draws, _triangles, _frameRebuild);
        }

        _frames++;
        _seconds += interval.TotalSeconds;
        _worst = Math.Max(_worst, interval.TotalMilliseconds);
        _cpu += cpu;
        _windowDraws += _draws;
        _windowTriangles += _triangles;
        _rebuild = Math.Max(_rebuild, _frameRebuild);
        if (_seconds < WindowSeconds)
        {
            return null;
        }

        string line = Line($"{_frames / _seconds:0} fps · worst {_worst:0} ms", _cpu / _frames,
            _gpuSamples > 0 ? _gpu / _gpuSamples : null, _windowDraws / _frames, _windowTriangles / _frames, _rebuild);
        ResetWindow();
        return line;
    }

    private string Line(string rate, double cpu, double? gpu, long draws, long triangles, double rebuild)
        => $"{rate} · CPU {cpu:0.0} ms" + (gpu is { } g ? $" · GPU {g:0.0} ms" : "") +
           $" · {draws:N0} draws · {triangles / 1e6:0.00}M tris" +
           (rebuild >= 0.05 ? $" · rebuild {rebuild:0.0} ms" : "") + $" · {VideoMemory()}";

    private void ResetWindow()
    {
        _frames = 0;
        _seconds = 0;
        _worst = 0;
        _cpu = 0;
        _gpu = 0;
        _gpuSamples = 0;
        _windowDraws = 0;
        _windowTriangles = 0;
        _rebuild = 0;
    }
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
