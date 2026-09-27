using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace JackAll.App;

/// <summary>One line of a <see cref="CurvePlot"/>, stroked with a theme brush by key.</summary>
public sealed record CurveSeries(IReadOnlyList<Point> Points, string Brush, double Thickness = 2, bool Dashed = false);

/// <summary>Curves drawn over their shared range, with the range's ends labelled; redraws on resize.</summary>
public sealed class CurvePlot : Canvas
{
    private const double Pad = 24;

    private IReadOnlyList<CurveSeries> _series = [];

    public CurvePlot()
    {
        ClipToBounds = true;
        SizeChanged += (_, _) => Redraw();
    }

    public void Show(IReadOnlyList<CurveSeries> series)
    {
        _series = series;
        Redraw();
    }

    private void Redraw()
    {
        Children.Clear();
        Point[] all = [.. _series.SelectMany(s => s.Points)];
        if (all.Length == 0 || ActualWidth < 20 || ActualHeight < 20)
        {
            return;
        }

        double minX = all.Min(p => p.X), maxX = Math.Max(all.Max(p => p.X), minX + 1e-3);
        double minY = Math.Min(0, all.Min(p => p.Y)), maxY = Math.Max(all.Max(p => p.Y), minY + 1e-3) * 1.1;
        Point At(Point p) => new(Pad + (p.X - minX) / (maxX - minX) * (ActualWidth - 2 * Pad),
                                 ActualHeight - Pad - (p.Y - minY) / (maxY - minY) * (ActualHeight - 2 * Pad));

        foreach (CurveSeries series in _series)
        {
            var line = new Polyline { StrokeThickness = series.Thickness, Points = [.. series.Points.Select(At)] };
            line.SetResourceReference(Shape.StrokeProperty, series.Brush);
            if (series.Dashed)
            {
                line.StrokeDashArray = [4, 3];
            }
            Children.Add(line);
        }

        AddLabel($"{minX:0.#}", 2, ActualHeight - Pad + 4);
        AddLabel($"{maxX:0.#}", ActualWidth - Pad - 20, ActualHeight - Pad + 4);
        AddLabel($"{maxY / 1.1:0.##}", 2, 2);
        if (minY < 0)
        {
            AddLabel($"{minY:0.#}", 2, ActualHeight - Pad - 14);
        }
    }

    private void AddLabel(string text, double left, double top)
    {
        var label = new TextBlock { Text = text, FontSize = 10 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        SetLeft(label, left);
        SetTop(label, top);
        Children.Add(label);
    }
}
