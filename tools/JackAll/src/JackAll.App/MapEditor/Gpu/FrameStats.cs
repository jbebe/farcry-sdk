using System.Diagnostics;
using Vortice.Direct3D11;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// What the viewport's frames cost - interval, CPU and GPU time, scene draws and triangles, the
/// slowest stream rebuild - summed over a short window for the stats line.
/// </summary>
public sealed class FrameStats : IDisposable
{
    private const double WindowSeconds = 0.5;

    /// <summary>Timer queries in flight; a result is read this many frames after it was issued.</summary>
    private const int QueryRing = 4;

    private static int _draws;
    private static long _triangles;

    /// <summary>Per frame: the disjoint query that gives the clock's rate, and a stamp either end.</summary>
    private readonly (ID3D11Query Disjoint, ID3D11Query Begin, ID3D11Query End)[] _queries =
        new (ID3D11Query, ID3D11Query, ID3D11Query)[QueryRing];

    private int _issued;
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
        for (int i = 0; i < QueryRing; i++)
        {
            _queries[i] = (Device.CreateQuery(QueryType.TimestampDisjoint), Device.CreateQuery(QueryType.Timestamp),
                Device.CreateQuery(QueryType.Timestamp));
        }
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

        var (disjoint, begin, end) = _queries[_issued % QueryRing];
        if (_issued >= QueryRing
            && Context.GetData(disjoint, AsyncGetDataFlags.DoNotFlush, out QueryDataTimestampDisjoint clock)
            && Context.GetData(begin, AsyncGetDataFlags.DoNotFlush, out ulong started)
            && Context.GetData(end, AsyncGetDataFlags.DoNotFlush, out ulong ended)
            && !clock.Disjoint)
        {
            _window.Gpu += (ended - started) * 1000.0 / clock.Frequency;
            _window.GpuSamples++;
        }
        Context.Begin(disjoint);
        Context.End(begin);
    }

    /// <summary>Time spent rebuilding instance streams or overlays this frame.</summary>
    public void Rebuilt(TimeSpan elapsed) => _frameRebuild = elapsed.TotalMilliseconds;

    /// <summary>Closes the frame and returns the stats line when it closes a window, else null. A
    /// frame the viewport did not ask for continuously is reported on its own, without a rate or GPU time.</summary>
    public string? EndFrame(TimeSpan interval, bool continuous)
    {
        var (disjoint, _, end) = _queries[_issued % QueryRing];
        Context.End(end);
        Context.End(disjoint);
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

    private static string Line(string rate, double cpu, double? gpu, long draws, long triangles, double rebuild)
        => $"{rate} · CPU {cpu:0.0} ms" + (gpu is { } g ? $" · GPU {g:0.0} ms" : "") +
           $" · {draws:N0} draws · {triangles / 1e6:0.00}M tris" +
           (rebuild >= 0.05 ? $" · rebuild {rebuild:0.0} ms" : "") + $" · {GpuDevice.VideoMemory()}";

    public void Dispose()
    {
        foreach (var (disjoint, begin, end) in _queries)
        {
            disjoint.Dispose();
            begin.Dispose();
            end.Dispose();
        }
    }
}
