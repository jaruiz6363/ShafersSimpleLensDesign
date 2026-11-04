using ShafersSimpleLensDesign.Core;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AberrationCalculator.Core.Glass;

namespace ShafersSimpleLensDesign.Views;

/// <summary>Draws a <see cref="LensDrawing"/>: each glass filled, the stop, the image plane and rays colored by field.</summary>
public sealed class LensView : Control
{
    public static readonly StyledProperty<LensDrawing?> DrawingProperty =
        AvaloniaProperty.Register<LensView, LensDrawing?>(nameof(Drawing));

    public LensDrawing? Drawing
    {
        get => GetValue(DrawingProperty);
        set => SetValue(DrawingProperty, value);
    }

    static LensView() => AffectsRender<LensView>(DrawingProperty);

    private static readonly IBrush Background = Brushes.White;
    // One fill per distinct glass, in the order the glasses first appear (front to back).
    private static readonly IBrush[] Fills =
    {
        new SolidColorBrush(Color.Parse("#D6E8F7")),
        new SolidColorBrush(Color.Parse("#A8CBEA")),
        new SolidColorBrush(Color.Parse("#CFE6C8")),
        new SolidColorBrush(Color.Parse("#F3DFB8")),
        new SolidColorBrush(Color.Parse("#E5D0EC")),
        new SolidColorBrush(Color.Parse("#F7D6D6")),
        new SolidColorBrush(Color.Parse("#D9D9D9")),
    };
    private static readonly Pen GlassPen = new(new SolidColorBrush(Color.Parse("#2F4F7A")), 1.2);
    private static readonly Pen StopPen = new(Brushes.Black, 3);
    private static readonly Pen ImagePen = new(new SolidColorBrush(Color.Parse("#333333")), 2.5);
    private static readonly Pen AxisPen = new(new SolidColorBrush(Color.Parse("#999999")), 0.8, new DashStyle(new double[] { 6, 4 }, 0));
    private static readonly Pen ScalePen = new(Brushes.Black, 1.5);
    // Field colors as OpticStudio's layout: blue, green, red.
    private static readonly Pen[] RayPens =
    {
        new(new SolidColorBrush(Color.Parse("#1F4FD8")), 0.9),
        new(new SolidColorBrush(Color.Parse("#1A9A3A")), 0.9),
        new(new SolidColorBrush(Color.Parse("#D62728")), 0.9),
    };

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        ctx.FillRectangle(Background, new Rect(b.Size));
        var L = Drawing;
        if (L is null)
        {
            DrawText(ctx, "Select a lens in the table to draw it.", new Point(12, 12), 13, Brushes.Gray);
            return;
        }

        // Fit everything drawn into the control, the same scale on both axes.
        var all = L.Rays.SelectMany(r => r.Points).Concat(L.Regions.SelectMany(r => r.Outline)).ToList();
        if (all.Count == 0) return;
        double zMin = all.Min(p => p.Z), zMax = all.Max(p => p.Z), yMin = all.Min(p => p.Y), yMax = all.Max(p => p.Y);
        const double pad = 18;
        double scale = Math.Min((b.Width - 2 * pad) / (zMax - zMin), (b.Height - 2 * pad - 20) / (yMax - yMin));
        if (!(scale > 0)) return;
        double ox = pad + ((b.Width - 2 * pad) - scale * (zMax - zMin)) / 2 - scale * zMin;
        double oy = pad + ((b.Height - 2 * pad - 20) - scale * (yMax - yMin)) / 2 + scale * yMax;
        Point P(Pt p) => new(ox + scale * p.Z, oy - scale * p.Y);

        ctx.DrawLine(AxisPen, P(new Pt(zMin, 0)), P(new Pt(zMax, 0)));

        var glasses = new List<GlassData>();
        foreach (var (g, outline) in L.Regions)
        {
            if (!glasses.Contains(g)) glasses.Add(g);
            ctx.DrawGeometry(Fills[glasses.IndexOf(g) % Fills.Length], GlassPen, Poly(outline.Select(P), true));
        }

        // The stop: a plane, open within its semi-diameter.
        double rim = L.StopSemi * 1.35;
        ctx.DrawLine(StopPen, P(new Pt(L.StopZ, L.StopSemi)), P(new Pt(L.StopZ, rim)));
        ctx.DrawLine(StopPen, P(new Pt(L.StopZ, -L.StopSemi)), P(new Pt(L.StopZ, -rim)));

        foreach (var (field, points) in L.Rays)
            ctx.DrawGeometry(null, RayPens[Math.Min(field, RayPens.Length - 1)], Poly(points.Select(P), false));

        double imageHalf = L.Rays.Select(r => Math.Abs(r.Points[^1].Y)).DefaultIfEmpty(1).Max() * 1.1;
        ctx.DrawLine(ImagePen, P(new Pt(L.ImageZ, -imageHalf)), P(new Pt(L.ImageZ, imageHalf)));

        // Scale bar and legend.
        double bar = NiceLength(0.2 * b.Width / scale);
        var s0 = new Point(pad, b.Height - pad);
        ctx.DrawLine(ScalePen, s0, new Point(pad + bar * scale, s0.Y));
        ctx.DrawLine(ScalePen, s0 - new Point(0, 4), s0 + new Point(0, 4));
        ctx.DrawLine(ScalePen, new Point(pad + bar * scale, s0.Y - 4), new Point(pad + bar * scale, s0.Y + 4));
        DrawText(ctx, $"{bar.ToString("G", CultureInfo.InvariantCulture)} mm", new Point(pad + bar * scale + 6, s0.Y - 9), 12, Brushes.Black);

        double x = b.Width - pad;
        for (int f = L.FieldAngles.Length - 1; f >= 0; f--)
        {
            var ft = Text($"{L.FieldAngles[f].ToString("0.##", CultureInfo.InvariantCulture)}°", 12, RayPens[Math.Min(f, RayPens.Length - 1)].Brush!);
            x -= ft.Width;
            ctx.DrawText(ft, new Point(x, b.Height - pad - 9));
            x -= 12;
        }
    }

    private static StreamGeometry Poly(IEnumerable<Point> pts, bool closed)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        bool first = true;
        foreach (var p in pts)
        {
            if (first) { c.BeginFigure(p, closed); first = false; }
            else c.LineTo(p);
        }
        if (!first) c.EndFigure(closed);
        return g;
    }

    private static double NiceLength(double v)
    {
        double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (double m in new[] { 5.0, 2.0, 1.0 }) if (m * p <= v) return m * p;
        return p;
    }

    private static FormattedText Text(string s, double size, IBrush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);

    private static void DrawText(DrawingContext ctx, string s, Point at, double size, IBrush brush) =>
        ctx.DrawText(Text(s, size, brush), at);
}
