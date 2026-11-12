using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using JackAll.App.MapEditor.Gpu;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace JackAll.App.MapEditor;

/// <summary>
/// The Map tab's viewport: a native child window with a Direct3D swap chain. WPF cannot draw over it
/// or route its input, so the window's own messages become these events, in WPF's units and keys.
/// </summary>
public sealed class D3DViewport : HwndHost
{
    private const int WmSize = 0x0005, WmSetFocus = 0x0007, WmKillFocus = 0x0008, WmPaint = 0x000F;
    private const int WmEraseBkgnd = 0x0014, WmMouseActivate = 0x0021, WmGetDlgCode = 0x0087;
    private const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const int WmMouseMove = 0x0200, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204, WmRButtonUp = 0x0205, WmMButtonDown = 0x0207, WmMButtonUp = 0x0208;
    private const int WmMouseWheel = 0x020A, WmCaptureChanged = 0x0215;

    private static readonly string ClassName = "JackAllViewport";
    private static bool _classRegistered;

    private IntPtr _hwnd;
    private IDXGISwapChain1? _swapChain;
    private ID3D11RenderTargetView? _backBuffer;
    private bool _resizePending;
    private int _framePending;
    private volatile bool _continuous;
    private volatile bool _rendering;
    private Thread? _beat;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastFrame;
    private ViewportDropTarget? _dropTarget;

    /// <summary>Raised once a frame with the back buffer ready, and the time since the last frame.</summary>
    public event Action<TimeSpan>? Render;

    public event Action<MouseButton, Point>? MouseButtonDown;
    public event Action<MouseButton, Point>? MouseButtonUp;
    public event Action<Point>? PointerMoved;
    public event Action<int>? WheelTurned;

    /// <summary>Capture went elsewhere while a button was held - an Alt+Tab mid drag.</summary>
    public event Action? CaptureLost;

    /// <summary>Returns whether the key was used.</summary>
    public event Func<Key, bool>? KeyPressed;
    public event Action<Key>? KeyReleased;

    /// <summary>What a drag over the viewport would do, with its data and point.</summary>
    public event Func<IDataObject, Point, DragDropEffects>? DragHovered;
    public event Action<IDataObject, Point>? DragDropped;

    /// <summary>The device was lost on present - a driver reset or update.</summary>
    public event Action? DeviceLost;

    /// <summary>No swap chain could be made on this window, in either presentation model.</summary>
    public event Action<Exception>? Failed;

    /// <summary>The view the final pass draws into; null until the first frame. Held here alone, since
    /// a resize fails while anything else still refers to the old buffers.</summary>
    public ID3D11RenderTargetView? BackBuffer => _backBuffer;

    public int PixelWidth { get; private set; }

    public int PixelHeight { get; private set; }

    public bool HasFocus => _hwnd != IntPtr.Zero && GetFocus() == _hwnd;

    /// <summary>Where the cursor is now, in the viewport's own units.</summary>
    public Point CursorPosition => GetCursorPos(out NativePoint p) ? ScreenToDips(p.X, p.Y) : default;

    /// <summary>
    /// Redraws every display frame while set; otherwise only on <see cref="RequestFrame"/>. The beat
    /// is the desktop compositor's own, not WPF's render tick: with nothing of its own changing, WPF
    /// ticks at 60 whatever the display runs at.
    /// </summary>
    public bool RenderContinuously
    {
        get => _continuous;
        set
        {
            if (value == _continuous) return;
            _continuous = value;
            if (value && _beat is not { IsAlive: true })
            {
                _beat = new Thread(Beat) { IsBackground = true, Name = "Viewport frame beat" };
                _beat.Start();
            }
        }
    }

    /// <summary>One frame per composition while <see cref="RenderContinuously"/> holds.</summary>
    private void Beat()
    {
        while (_continuous)
        {
            if (DwmFlush() != 0)
            {
                // No compositor to wait on, as in some remote sessions: a 60 Hz beat instead.
                Thread.Sleep(16);
            }
            // A frame longer than a refresh skips a beat, so input, which the dispatcher ranks below
            // rendering, still gets the time up to the next one.
            if (!_rendering)
            {
                Post();
            }
        }
    }

    /// <summary>One frame at the next render pass; any number of requests before it make one frame.</summary>
    public void RequestFrame()
    {
        if (!_continuous && _hwnd != IntPtr.Zero)
        {
            Post();
        }
    }

    private void Post()
    {
        if (Interlocked.Exchange(ref _framePending, 1) == 0)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, RenderFrame);
        }
    }

    public new void Focus()
    {
        if (_hwnd != IntPtr.Zero)
        {
            SetFocus(_hwnd);
        }
    }

    public new void CaptureMouse() => SetCapture(_hwnd);

    public new void ReleaseMouseCapture()
    {
        if (GetCapture() == _hwnd)
        {
            ReleaseCapture();
        }
    }

    /// <summary>Drops the swap chain, so the next frame builds one on the current device.</summary>
    public void ReleaseSwapChain()
    {
        _backBuffer?.Dispose();
        _backBuffer = null;
        _swapChain?.Dispose();
        _swapChain = null;
    }

    private void RenderFrame()
    {
        Volatile.Write(ref _framePending, 0);
        _rendering = true;
        try
        {
            DrawAndPresent();
        }
        finally
        {
            _rendering = false;
        }
    }

    private void DrawAndPresent()
    {
        if (!GpuDevice.IsCreated || _hwnd == IntPtr.Zero || !IsVisible)
        {
            return;
        }

        GetClientRect(_hwnd, out NativeRect client);
        PixelWidth = client.Right - client.Left;
        PixelHeight = client.Bottom - client.Top;
        if (PixelWidth <= 0 || PixelHeight <= 0)
        {
            return;
        }
        // A device removed under the frame can throw from any call in it, not only from Present, and
        // the shader compiler first loads in the frame too.
        try
        {
            PrepareSwapChain();

            TimeSpan now = _clock.Elapsed;
            TimeSpan delta = now - _lastFrame;
            _lastFrame = now;
            Render?.Invoke(delta);

            // Interval 0: the compositor shows the newest frame at its next composition, which is
            // also what paces a continuous run.
            _swapChain!.Present(0, PresentFlags.None).CheckError();
        }
        catch (Exception ex) when (GpuDevice.IsFailure(ex))
        {
            if (GpuDevice.IsRemoved)
            {
                CrashLog.NoteGpuError($"device lost, reason {GpuDevice.RemovedReason()}");
                DeviceLost?.Invoke();
            }
            else
            {
                Failed?.Invoke(ex);
            }
            return;
        }
        GpuDevice.DrainMessages();
    }

    private void PrepareSwapChain()
    {
        if (_swapChain is null)
        {
            using IDXGIFactory2 factory = GpuDevice.Factory();
            var description = new SwapChainDescription1
            {
                Width = (uint)PixelWidth,
                Height = (uint)PixelHeight,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                // Three, so a frame never waits inside its own GPU time for the compositor to hand a
                // buffer back; two stalled each frame to the next refresh.
                BufferCount = 3,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Ignore,
            };
            try
            {
                _swapChain = factory.CreateSwapChainForHwnd(GpuDevice.Device, _hwnd, description);
            }
            catch (SharpGenException)
            {
                // Some remote sessions and old drivers have no flip model; the blit model still draws.
                description.SwapEffect = SwapEffect.Discard;
                description.BufferCount = 1;
                _swapChain = factory.CreateSwapChainForHwnd(GpuDevice.Device, _hwnd, description);
            }
            factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAll);
            _resizePending = false;
        }
        else if (_resizePending)
        {
            _resizePending = false;
            // The context's own binding is a reference too.
            GpuDevice.Context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
            _backBuffer?.Dispose();
            _backBuffer = null;
            _swapChain.ResizeBuffers(0, (uint)PixelWidth, (uint)PixelHeight, Format.Unknown, SwapChainFlags.None)
                .CheckError();
        }
        if (_backBuffer is null)
        {
            using ID3D11Texture2D buffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
            _backBuffer = GpuDevice.Device.CreateRenderTargetView(buffer);
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        IntPtr instance = GetModuleHandle(null);
        if (!_classRegistered)
        {
            var windowClass = new WindowClass
            {
                Size = Marshal.SizeOf<WindowClass>(),
                WindowProcedure = GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW"),
                Instance = instance,
                Cursor = LoadCursor(IntPtr.Zero, 32512),
                ClassName = ClassName,
            };
            RegisterClassEx(ref windowClass);
            _classRegistered = true;
        }

        const int child = 0x40000000, visible = 0x10000000, clipSiblings = 0x04000000, clipChildren = 0x02000000;
        _hwnd = CreateWindowEx(0, ClassName, "", child | visible | clipSiblings | clipChildren,
            0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        // A window takes drops only once OLE is running on its thread, which WPF starts lazily.
        OleInitialize(IntPtr.Zero);
        _dropTarget = new ViewportDropTarget(this);
        int registered = RegisterDragDrop(_hwnd, _dropTarget);
        if (registered < 0)
        {
            Debug.WriteLine($"The viewport takes no drops: RegisterDragDrop 0x{registered:X8}");
        }
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        RenderContinuously = false;
        ReleaseSwapChain();
        RevokeDragDrop(hwnd.Handle);
        DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmEraseBkgnd:
                handled = true;
                return 1;
            case WmPaint:
                ValidateRect(hwnd, IntPtr.Zero);
                RequestFrame();
                handled = true;
                return IntPtr.Zero;
            case WmSize:
                _resizePending = true;
                RequestFrame();
                break;
            case WmMouseActivate:
                SetFocus(hwnd);
                handled = true;
                return 1;
            case WmGetDlgCode:
                handled = true;
                return 0x0004 | 0x0001;
            case WmLButtonDown or WmRButtonDown or WmMButtonDown:
                SetFocus(hwnd);
                MouseButtonDown?.Invoke(ButtonOf(msg), PointOf(lParam));
                handled = true;
                return IntPtr.Zero;
            case WmLButtonUp or WmRButtonUp or WmMButtonUp:
                MouseButtonUp?.Invoke(ButtonOf(msg), PointOf(lParam));
                handled = true;
                return IntPtr.Zero;
            case WmMouseMove:
                PointerMoved?.Invoke(PointOf(lParam));
                handled = true;
                return IntPtr.Zero;
            case WmMouseWheel:
                WheelTurned?.Invoke((short)((long)wParam >> 16 & 0xFFFF));
                handled = true;
                return IntPtr.Zero;
            case WmCaptureChanged when lParam != hwnd:
                CaptureLost?.Invoke();
                break;
            // With Alt held a key arrives as WPF's System, as it does to WPF's own handlers. Its
            // release is passed as itself, so a key let go under Alt does not stay held.
            case WmKeyDown or WmSysKeyDown:
                if (KeyPressed?.Invoke(msg == WmSysKeyDown ? Key.System : KeyOf(wParam)) == true)
                {
                    handled = true;
                    return IntPtr.Zero;
                }
                break;
            case WmKeyUp or WmSysKeyUp:
                KeyReleased?.Invoke(KeyOf(wParam));
                break;
            case WmSetFocus or WmKillFocus:
                RequestFrame();
                break;
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private static MouseButton ButtonOf(int msg) => msg switch
    {
        WmLButtonDown or WmLButtonUp => MouseButton.Left,
        WmRButtonDown or WmRButtonUp => MouseButton.Right,
        _ => MouseButton.Middle,
    };

    private static Key KeyOf(IntPtr wParam) => KeyInterop.KeyFromVirtualKey((int)wParam);

    private Point PointOf(IntPtr lParam)
    {
        long value = (long)lParam;
        return ToDips((short)(value & 0xFFFF), (short)(value >> 16 & 0xFFFF));
    }

    private Point ToDips(int x, int y)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        return new Point(x / dpi.DpiScaleX, y / dpi.DpiScaleY);
    }

    internal Point ScreenToDips(int x, int y)
    {
        var p = new NativePoint { X = x, Y = y };
        ScreenToClient(_hwnd, ref p);
        return ToDips(p.X, p.Y);
    }

    internal DragDropEffects OnDragHover(IDataObject data, Point at) => DragHovered?.Invoke(data, at) ?? DragDropEffects.None;

    internal void OnDragDrop(IDataObject data, Point at) => DragDropped?.Invoke(data, at);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public int Size;
        public int Style;
        public IntPtr WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, int cursor);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetCapture();

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool ValidateRect(IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);

    [DllImport("ole32.dll")]
    private static extern int RegisterDragDrop(IntPtr hwnd, [MarshalAs(UnmanagedType.Interface)] ViewportDropTarget.IOleDropTarget target);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr hwnd);

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr reserved);

    /// <summary>Blocks until the desktop compositor's next composition.</summary>
    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
