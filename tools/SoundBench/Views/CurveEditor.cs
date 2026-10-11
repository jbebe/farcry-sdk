using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using JackAll.Tools.Spk;

namespace SoundBench.Views;

/// <summary>
/// A layer's curve over its sibling layers' curves on the same parameter, with the parameter's value now
/// marked. Drag a point to move it (with Shift, only its value), double-click to add one, right-click to
/// remove one; the wheel zooms into the parameter and dragging the background pans. The curve itself is
/// left alone: each change is asked for through <see cref="Edited"/>.
/// </summary>
public sealed class CurveEditor : FrameworkElement
{
    private const double Pad = 28, Grab = 8;

    private static readonly DashStyle Dashed = new([4, 3], 0);

    private readonly SpkCurve _curve;
    private readonly IReadOnlyList<SpkCurve> _background;
    private readonly (float Min, float Max)? _declared;
    private float? _now;

    /// <summary>The point under the mouse or being dragged, or -1.</summary>
    private int _active = -1;
    private bool _dragging;

    /// <summary>The parameter's value under the mouse when a pan began.</summary>
    private double? _panFrom;

    /// <summary>The values the curves span, the parameter across and the curve's output up.</summary>
    private Rect _full;

    /// <summary>The part of <see cref="_full"/> shown: a stretch of the parameter once zoomed. Fixed through a drag.</summary>
    private Rect _shown = Rect.Empty;

    /// <param name="declared">The parameter's range, which the plot always spans, or null for an unknown one.</param>
    public CurveEditor(SpkCurve curve, IReadOnlyList<SpkCurve> background, (float Min, float Max)? declared)
    {
        _curve = curve;
        _background = background;
        _declared = declared;
        ClipToBounds = true;
        MouseLeftButtonDown += OnPress;
        MouseMove += OnMove;
        MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
        LostMouseCapture += (_, _) => EndDrag();
        MouseRightButtonUp += OnRemove;
        MouseLeave += (_, _) => Activate(_dragging ? _active : -1);
        MouseWheel += OnWheel;
        Refit();
    }

    /// <summary>A gesture is about to change the curve.</summary>
    public event Action? Editing;

    /// <summary>The points the curve should have now.</summary>
    public event Action<SpkPoint[]>? Edited;

    private bool IsVolume => _curve.Target == 0;

    /// <summary>Marks the parameter's value now.</summary>
    public void Mark(float x)
    {
        if (_now != x)
        {
            _now = x;
            InvalidateVisual();
        }
    }

    /// <summary>Spans the points again, keeping any zoom, as after the curve changed elsewhere.</summary>
    public void Refit()
    {
        SpkPoint[] all = [.. _curve.Points, .. _background.SelectMany(c => c.Points)];
        if (all.Length == 0)
        {
            return;
        }
        double minX = Math.Min(_declared?.Min ?? float.MaxValue, all.Min(p => p.X));
        double maxX = Math.Max(_declared?.Max ?? float.MinValue, all.Max(p => p.X));
        double minY = Math.Min(IsVolume ? -48 : 0.5, all.Min(p => p.Y));
        double maxY = Math.Max(IsVolume ? 0 : 1.5, all.Max(p => p.Y));
        double margin = (maxY - minY) * 0.08;
        _full = new Rect(minX, minY - margin, Math.Max(maxX - minX, 1e-3), maxY - minY + 2 * margin);
        Rect from = _shown.IsEmpty ? _full : _shown;
        Span(from.Left, Math.Min(from.Width, _full.Width));
    }

    /// <summary>Shows the stretch of the parameter from <paramref name="left"/>, kept inside the curves' span.</summary>
    private void Span(double left, double width)
    {
        _shown = new Rect(Math.Clamp(left, _full.Left, _full.Right - width), _full.Y, width, _full.Height);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Transparent, so the whole area takes the mouse.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_shown.IsEmpty || ActualWidth < 4 * Pad || ActualHeight < 4 * Pad)
        {
            return;
        }

        Brush brush = Resource(IsVolume ? "VolumeCurveBrush" : "PitchCurveBrush"), muted = Resource("TextMutedBrush"), cursor = Resource("CursorBrush");
        dc.PushClip(new RectangleGeometry(new Rect(Pad, 0, ActualWidth - 2 * Pad, ActualHeight)));
        double level = IsVolume ? 0 : 1;
        DrawLine(dc, [new(_shown.Left, level), new(_shown.Right, level)], new Pen(muted, 1) { DashStyle = Dashed });
        var grey = new Pen(Resource("BackgroundCurveBrush"), 1.5);
        foreach (SpkCurve other in _background)
        {
            DrawLine(dc, Held(other.Points), grey);
        }
        DrawLine(dc, Held(_curve.Points), new Pen(brush, 2));
        if (_now is { } x)
        {
            DrawLine(dc, [new(x, _shown.Top), new(x, _shown.Bottom)], new Pen(cursor, 1) { DashStyle = Dashed });
            double tick = _shown.Width * 0.015, y = _curve.Evaluate(x);
            DrawLine(dc, [new(x - tick, y), new(x + tick, y)], new Pen(cursor, 4));
        }
        dc.Pop();

        var ring = new Pen(cursor, 2);
        for (int i = 0; i < _curve.Points.Count; i++)
        {
            if (InView(_curve.Points[i]))
            {
                double radius = i == _active ? 5.5 : 3.5;
                dc.DrawEllipse(brush, i == _active ? ring : null, ToScreen(_curve.Points[i]), radius, radius);
            }
        }

        DrawLabel(dc, FormatX(_shown.Left), 2, ActualHeight - Pad + 4, muted);
        DrawLabel(dc, FormatX(_shown.Right), ActualWidth - Pad - 24, ActualHeight - Pad + 4, muted);
        DrawLabel(dc, Format(_shown.Bottom), 2, 2, muted);
        DrawLabel(dc, Format(_shown.Top), 2, ActualHeight - Pad - 14, muted);
        if (_active >= 0 && _active < _curve.Points.Count)
        {
            SpkPoint point = _curve.Points[_active];
            DrawLabel(dc, $"{FormatX(point.X)}: {Format(point.Y)}", ActualWidth / 2 - 30, 2, muted);
        }
    }

    private Brush Resource(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    private string Format(double y) => IsVolume ? $"{y:0.0} dB" : $"×{y:0.000}";

    /// <summary>A parameter value to the step a drag moves it by, at most to a hundredth.</summary>
    private string FormatX(double x) => Math.Round(x, Math.Clamp(-(int)Math.Round(Math.Log10(Step(_shown.Width))), 0, 2)).ToString();

    private bool InView(SpkPoint point) => point.X >= _shown.Left && point.X <= _shown.Right;

    /// <summary>The points as DARE reads them, held level out to the curves' full span.</summary>
    private Point[] Held(List<SpkPoint> points)
        => points.Count == 0 ? [] : [new(_full.Left, points[0].Y), .. points.Select(p => new Point(p.X, p.Y)), new(_full.Right, points[^1].Y)];

    private Point ToScreen(Point value)
        => new(Pad + (value.X - _shown.X) / _shown.Width * (ActualWidth - 2 * Pad),
               ActualHeight - Pad - (value.Y - _shown.Y) / _shown.Height * (ActualHeight - 2 * Pad));

    private Point ToScreen(SpkPoint point) => ToScreen(new Point(point.X, point.Y));

    private Point ToValue(Point screen)
        => new(_shown.X + (screen.X - Pad) / (ActualWidth - 2 * Pad) * _shown.Width,
               _shown.Y + (ActualHeight - Pad - screen.Y) / (ActualHeight - 2 * Pad) * _shown.Height);

    private int PointAt(Point screen)
        => _curve.Points.FindIndex(p => InView(p) && (ToScreen(p) - screen).Length <= Grab);

    /// <summary>A round step for values across <paramref name="span"/>: a thousandth of it, to a power of ten.</summary>
    private static double Step(double span) => Math.Pow(10, Math.Floor(Math.Log10(span)) - 3);

    private static double Round(double value, double step) => Math.Round(value / step) * step;

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        Point mouse = e.GetPosition(this);
        int at = PointAt(mouse);
        if (at >= 0)
        {
            Editing?.Invoke();
            _active = at;
            _dragging = true;
            CaptureMouse();
        }
        else if (e.ClickCount == 2)
        {
            Point value = ToValue(mouse);
            var added = new SpkPoint(
                (float)Math.Clamp(Round(value.X, Step(_shown.Width)), _shown.Left, _shown.Right),
                (float)Math.Clamp(Round(value.Y, Step(_shown.Height)), _shown.Top, _shown.Bottom));
            List<SpkPoint> points = _curve.Points;
            if (points.Any(p => p.X == added.X))
            {
                return;
            }
            int index = points.FindIndex(p => p.X > added.X);
            if (index < 0)
            {
                index = points.Count;
            }
            Editing?.Invoke();
            _active = index;
            Edit([.. points[..index], added, .. points[index..]]);
        }
        else
        {
            _panFrom = ToValue(mouse).X;
            CaptureMouse();
        }
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        Point mouse = e.GetPosition(this);
        if (_panFrom is { } from)
        {
            Span(_shown.Left + from - ToValue(mouse).X, _shown.Width);
            return;
        }
        if (!_dragging)
        {
            Activate(PointAt(mouse));
            return;
        }

        List<SpkPoint> points = _curve.Points;
        SpkPoint old = points[_active];
        Point value = ToValue(mouse);
        double stepX = Step(_shown.Width);
        double low = _active > 0 ? points[_active - 1].X + stepX : _shown.Left;
        double high = _active < points.Count - 1 ? points[_active + 1].X - stepX : _shown.Right;
        double x = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || low > high ? old.X : Math.Clamp(Round(value.X, stepX), low, high);
        double y = Math.Clamp(Round(value.Y, Step(_shown.Height)), _shown.Top, _shown.Bottom);
        var moved = new SpkPoint((float)x, (float)y);
        if (moved != old)
        {
            SpkPoint[] edited = [.. points];
            edited[_active] = moved;
            Edit(edited);
        }
    }

    private void OnRemove(object sender, MouseButtonEventArgs e)
    {
        int at = PointAt(e.GetPosition(this));
        if (_curve.Points.Count > 1 && at >= 0)
        {
            Editing?.Invoke();
            _active = -1;
            Edit([.. _curve.Points.Where((_, i) => i != at)]);
        }
        e.Handled = true;
    }

    /// <summary>Zooms the parameter in or out around the mouse.</summary>
    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_shown.IsEmpty)
        {
            double at = ToValue(e.GetPosition(this)).X;
            double width = Math.Clamp(_shown.Width * Math.Pow(0.8, e.Delta / 120.0), _full.Width / 100, _full.Width);
            Span(at - (at - _shown.Left) * width / _shown.Width, width);
        }
        e.Handled = true;
    }

    private void EndDrag()
    {
        _panFrom = null;
        if (_dragging)
        {
            _dragging = false;
            Refit();
        }
    }

    private void Activate(int at)
    {
        if (_active != at)
        {
            _active = at;
            InvalidateVisual();
        }
    }

    private void Edit(SpkPoint[] points)
    {
        Edited?.Invoke(points);
        InvalidateVisual();
    }

    private void DrawLine(DrawingContext dc, Point[] values, Pen pen)
    {
        if (values.Length < 2)
        {
            return;
        }
        var line = new StreamGeometry();
        using (StreamGeometryContext context = line.Open())
        {
            context.BeginFigure(ToScreen(values[0]), isFilled: false, isClosed: false);
            context.PolyLineTo([.. values.Skip(1).Select(v => ToScreen(v))], isStroked: true, isSmoothJoin: true);
        }
        dc.DrawGeometry(null, pen, line);
    }

    private void DrawLabel(DrawingContext dc, string text, double left, double top, Brush brush)
        => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(SystemFonts.MessageFontFamily.Source),
            10, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(left, top));
}
