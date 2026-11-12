using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D11.Debug;
using Vortice.DXGI;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// The one Direct3D 11 device the Map tab draws with: hardware where the driver offers feature level
/// 10.0 or better, else WARP, Windows' software renderer. Every layer reaches it through these
/// statics.
/// </summary>
public static class GpuDevice
{
    private static readonly FeatureLevel[] Levels =
        [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];

    public static ID3D11Device Device { get; private set; } = null!;

    public static ID3D11DeviceContext Context { get; private set; } = null!;

    private static FeatureLevel FeatureLevel { get; set; }

    private static bool IsWarp { get; set; }

    public static bool IsCreated => Device is not null;

    private static IDXGIAdapter3? _adapter;
    private static ID3D11InfoQueue? _infoQueue;

    /// <summary>
    /// Makes the device. <c>JACKALL_MAP_DEVICE=warp</c> skips the hardware, and <c>fail</c> throws, so
    /// both fallbacks can be seen on a PC that needs neither.
    /// </summary>
    public static void Create()
    {
        string? forced = Environment.GetEnvironmentVariable("JACKALL_MAP_DEVICE")?.Trim().ToLowerInvariant();
        if (forced == "fail")
        {
            throw new PlatformNotSupportedException("JACKALL_MAP_DEVICE=fail asked for the viewport not to start.");
        }

        DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport;
#if DEBUG
        flags |= DeviceCreationFlags.Debug;
#endif
        ID3D11Device? device = null;
        if (forced != "warp")
        {
            device = TryCreate(DriverType.Hardware, flags);
        }
        IsWarp = device is null;
        device ??= TryCreate(DriverType.Warp, flags)
            ?? throw new PlatformNotSupportedException("Direct3D 11 is not available, not even as WARP.");

        Device = device;
        Context = device.ImmediateContext;
        FeatureLevel = device.FeatureLevel;

        using IDXGIDevice1 dxgi = device.QueryInterface<IDXGIDevice1>();
        dxgi.SetMaximumFrameLatency(2);
        using IDXGIAdapter adapter = dxgi.GetAdapter();
        _adapter = adapter.QueryInterfaceOrNull<IDXGIAdapter3>();
        _infoQueue = device.QueryInterfaceOrNull<ID3D11InfoQueue>();

        GpuState.Create();

        adapter.CheckInterfaceSupport<IDXGIDevice>(out long driver);
        CrashLog.Gpu = $"{adapter.Description.Description} | Direct3D {LevelName} | driver " +
            $"{driver >> 48 & 0xFFFF}.{driver >> 32 & 0xFFFF}.{driver >> 16 & 0xFFFF}.{driver & 0xFFFF} | {VideoMemory()}";
    }

    /// <summary>The feature level below 10.0 is refused: the shaders are shader model 4.0.</summary>
    private static ID3D11Device? TryCreate(DriverType driver, DeviceCreationFlags flags)
    {
        Result result = D3D11.D3D11CreateDevice(null, driver, flags, Levels, out ID3D11Device? device);
        if (result.Failure && flags.HasFlag(DeviceCreationFlags.Debug))
        {
            // The debug layer is an optional Windows feature; without it the device is made plain.
            result = D3D11.D3D11CreateDevice(null, driver, flags & ~DeviceCreationFlags.Debug, Levels, out device);
        }
        return result.Success ? device : null;
    }

    private static string LevelName => FeatureLevel switch
    {
        FeatureLevel.Level_11_1 => "11.1",
        FeatureLevel.Level_11_0 => "11.0",
        FeatureLevel.Level_10_1 => "10.1",
        _ => "10.0",
    };

    public static IDXGIFactory2 Factory()
    {
        using IDXGIDevice dxgi = Device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgi.GetAdapter();
        return adapter.GetParent<IDXGIFactory2>();
    }

    /// <summary>This process's video memory against the budget the OS gives it.</summary>
    public static string VideoMemory()
    {
        if (IsWarp)
        {
            return "WARP";
        }
        if (_adapter is null)
        {
            return "VRAM unknown";
        }
        QueryVideoMemoryInfo info = _adapter.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
        return $"VRAM {info.CurrentUsage / 1073741824.0:0.0}/{info.Budget / 1073741824.0:0.0} GB";
    }

    /// <summary>Hands the debug layer's errors to the crash log; nothing without the debug layer.</summary>
    public static void DrainMessages()
    {
        if (_infoQueue is not { } queue)
        {
            return;
        }
        ulong count = queue.NumStoredMessages;
        for (ulong i = 0; i < count; i++)
        {
            Message message = queue.GetMessage(i);
            string text = $"D3D11 {message.Severity} {message.Category} [{(int)message.Id}]: {message.Description}";
            if (message.Severity is MessageSeverity.Error or MessageSeverity.Corruption)
            {
                CrashLog.NoteGpuError(text);
            }
            System.Diagnostics.Debug.WriteLine(text);
        }
        queue.ClearStoredMessages();
    }

    /// <summary>Why the device went away, for the crash log and the tab.</summary>
    public static string RemovedReason() => $"0x{Device.DeviceRemovedReason.Code:X8}";

    /// <summary>Whether the driver took the device away: a reset, an update, a hung GPU.</summary>
    public static bool IsRemoved => Device is not null && Device.DeviceRemovedReason.Failure;

    /// <summary>What Direct3D, DXGI or the shader compiler throw when the PC cannot run the viewport.</summary>
    public static bool IsFailure(Exception ex)
        => ex is SharpGenException or PlatformNotSupportedException or System.Runtime.InteropServices.ExternalException
            or DllNotFoundException or EntryPointNotFoundException or TypeLoadException;

    /// <summary>Lets go of the device and what is made once per device; the layers are the caller's.</summary>
    public static void Destroy()
    {
        WideLines.Release();
        FrameConstants.Release();
        GpuState.Release();
        _infoQueue?.Dispose();
        _infoQueue = null;
        _adapter?.Dispose();
        _adapter = null;
        Context?.ClearState();
        Context?.Flush();
        Context?.Dispose();
        Device?.Dispose();
        Context = null!;
        Device = null!;
    }
}
