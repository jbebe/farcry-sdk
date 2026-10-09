using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

// Window placement, capture, input and callout drawing for the screenshot scripts. C# 5: compiled by
// Windows PowerShell 5.1's Add-Type.
public static class ShotNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public INPUTUNION u; }

    [StructLayout(LayoutKind.Explicit)]
    struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    // Win32 dialogs (file pickers, message boxes) expose their controls only as panes to UI Automation.
    public static void SetText(IntPtr hwnd, string text)
    {
        SendMessage(hwnd, 0x000C, IntPtr.Zero, text);
    }

    // WM_COMMAND/BN_CLICKED to the button's dialog: unlike BM_CLICK it works when the dialog isn't
    // active, and posting it means a button that closes the dialog doesn't block on what comes next.
    public static void PressButton(IntPtr hwnd)
    {
        PostMessage(GetParent(hwnd), 0x0111, new IntPtr(GetDlgCtrlID(hwnd)), hwnd);
    }

    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr hwnd);

    public static void Init()
    {
        // Per-monitor v2, so UI Automation rectangles and captured pixels share one coordinate space.
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        // ES_CONTINUOUS | ES_DISPLAY_REQUIRED: a locked or blanked session paints nothing.
        SetThreadExecutionState(0x80000000 | 0x00000002);
    }

    public static void Place(IntPtr hwnd, int x, int y, int w, int h)
    {
        ShowWindow(hwnd, 9);
        SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, 0x0040);
    }

    public static void Front(IntPtr hwnd)
    {
        ShowWindow(hwnd, 9);
        SetForegroundWindow(hwnd);
    }

    // The visible frame, without the invisible resize borders GetWindowRect includes.
    public static Rectangle Bounds(IntPtr hwnd)
    {
        RECT r;
        if (DwmGetWindowAttribute(hwnd, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0)
        {
            GetWindowRect(hwnd, out r);
        }
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    // PrintWindow renders the window even when covered; the screen copy is for popups, which are
    // separate windows PrintWindow cannot see.
    public static Bitmap Capture(IntPtr hwnd, bool fromScreen)
    {
        Rectangle frame = Bounds(hwnd);
        if (fromScreen)
        {
            Bitmap shot = new Bitmap(frame.Width, frame.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(shot))
            {
                g.CopyFromScreen(frame.Location, Point.Empty, frame.Size);
            }
            return shot;
        }

        RECT outer;
        GetWindowRect(hwnd, out outer);
        Bitmap full = new Bitmap(outer.Right - outer.Left, outer.Bottom - outer.Top, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(full))
        {
            IntPtr hdc = g.GetHdc();
            // PW_RENDERFULLCONTENT: includes DirectComposition content such as the GL viewport.
            PrintWindow(hwnd, hdc, 2);
            g.ReleaseHdc(hdc);
        }
        Rectangle inner = new Rectangle(frame.Left - outer.Left, frame.Top - outer.Top, frame.Width, frame.Height);
        Bitmap cropped = full.Clone(inner, PixelFormat.Format24bppRgb);
        full.Dispose();
        return cropped;
    }

    // A capture of a locked session or a window that never painted comes back one flat color.
    public static bool IsBlank(Bitmap bmp)
    {
        Color first = bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
        for (int y = 0; y < bmp.Height; y += 17)
        {
            for (int x = 0; x < bmp.Width; x += 17)
            {
                if (bmp.GetPixel(x, y) != first) return false;
            }
        }
        return true;
    }

    // Outlines each rectangle and puts a numbered badge on its top-left corner, then crops to region.
    public static Bitmap Annotate(Bitmap shot, Rectangle[] callouts, Rectangle region)
    {
        Color accent = Color.FromArgb(231, 150, 70);
        Color ink = Color.FromArgb(30, 30, 30);
        using (Graphics g = Graphics.FromImage(shot))
        using (Pen outline = new Pen(accent, 3))
        using (Pen halo = new Pen(ink, 1))
        using (SolidBrush fill = new SolidBrush(accent))
        using (SolidBrush text = new SolidBrush(ink))
        using (Font font = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel))
        using (StringFormat center = new StringFormat())
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            center.Alignment = StringAlignment.Center;
            center.LineAlignment = StringAlignment.Center;

            List<Rectangle> badges = new List<Rectangle>();
            for (int i = 0; i < callouts.Length; i++)
            {
                Rectangle r = callouts[i];
                r.Inflate(3, 3);
                using (GraphicsPath path = Rounded(r, 5))
                {
                    g.DrawPath(outline, path);
                }
                Rectangle inside = r;
                inside.Inflate(-2, -2);
                using (GraphicsPath path = Rounded(inside, 4))
                {
                    g.DrawPath(halo, path);
                }

                // Top-left corner, or the opposite corner on whichever axis the image edge would clip.
                int bx = r.Left - 11 < 2 ? r.Right - 11 : r.Left - 11;
                int by = r.Top - 11 < 2 ? r.Bottom - 11 : r.Top - 11;
                Rectangle badge = new Rectangle(Math.Min(bx, shot.Width - 24), Math.Min(by, shot.Height - 24), 22, 22);
                while (badges.Exists(b => b.IntersectsWith(badge)))
                {
                    badge.Offset(24, 0);
                }
                badges.Add(badge);
                g.FillEllipse(fill, badge);
                g.DrawEllipse(halo, badge);
                g.DrawString((i + 1).ToString(), font, text, new RectangleF(badge.X, badge.Y + 1, badge.Width, badge.Height), center);
            }
        }

        region.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
        return shot.Clone(region, PixelFormat.Format24bppRgb);
    }

    static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = radius * 2;
        GraphicsPath path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Click(int x, int y, bool right)
    {
        SetCursorPos(x, y);
        Thread.Sleep(60);
        uint down = right ? 0x0008u : 0x0002u;
        uint up = right ? 0x0010u : 0x0004u;
        Send(new INPUT[] { Mouse(down), Mouse(up) });
    }

    public static void Park(int x, int y)
    {
        SetCursorPos(x, y);
    }

    public static void KeyState(ushort vk, bool up)
    {
        Send(new INPUT[] { Key(vk, up) });
    }

    // Presses the virtual keys in order and releases them in reverse, e.g. Ctrl+Shift+Z.
    public static void Keys(ushort[] vks)
    {
        List<INPUT> inputs = new List<INPUT>();
        foreach (ushort vk in vks) inputs.Add(Key(vk, false));
        for (int i = vks.Length - 1; i >= 0; i--) inputs.Add(Key(vks[i], true));
        Send(inputs.ToArray());
    }

    static INPUT Mouse(uint flags)
    {
        INPUT input = new INPUT();
        input.type = 0;
        input.u.mi.dwFlags = flags;
        return input;
    }

    static INPUT Key(ushort vk, bool up)
    {
        INPUT input = new INPUT();
        input.type = 1;
        input.u.ki.wVk = vk;
        input.u.ki.dwFlags = up ? 0x0002u : 0u;
        return input;
    }

    static void Send(INPUT[] inputs)
    {
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }
}
