using AberrationCalculator.Core.Glass;

namespace ShafersSimpleLensDesign.Core;

/// <summary>A point of a drawing: z along the axis from the first vertex, y up, mm.</summary>
public readonly record struct Pt(double Z, double Y);

/// <summary>
/// A lens as it is drawn in its meridional section: each glass as an outline, the stop, the image plane, and rays at
/// three fields. Every surface is as wide as the rays through it need, and an element's faces are cut to the wider of
/// the two, joined by straight edges.
/// </summary>
public sealed class LensDrawing
{
    /// <summary>Each run of glass between two surfaces: its glass and its outline, closed.</summary>
    public required List<(GlassData Glass, List<Pt> Outline)> Regions { get; init; }

    /// <summary>Rays, by field index (0, 1, 2 for 0, 0.7 and 1 of the field), each a polyline from the start plane to the image.</summary>
    public required List<(int Field, List<Pt> Points)> Rays { get; init; }
    public required double[] FieldAngles { get; init; }
    public required double StopZ { get; init; }
    public required double StopSemi { get; init; }
    public required double ImageZ { get; init; }

    /// <summary>Draws <paramref name="d"/> at <paramref name="halfField"/> degrees: rays at 0, 0.7 and 1 of it, through the
    /// edges and center of the paraxial entrance pupil at the design wavelength.</summary>
    public static LensDrawing Of(Design d, double halfField)
    {
        var (semi, rays, fields) = Rim(d, halfField);
        int m = d.Count;
        var n = d.Indices(d.Spec.DesignWavelength);
        double pupilR = d.Spec.Efl / (2 * d.Spec.FNumber);
        var vertex = Vertices(d);
        var rim = Rims(d, semi);

        // Each run of glass, its faces cut to its element's rim.
        var regions = new List<(GlassData, List<Pt>)>();
        for (int q = 0; q < m - 1; q++)
        {
            if (d.Glass[q] is not { } g) continue;
            double h = rim[q];
            var front = Face(d.Curvature[q], vertex[q], h).ToList();
            var back = Face(d.Curvature[q + 1], vertex[q + 1], h).Reverse();
            regions.Add((g, front.Concat(back).ToList()));
        }

        int stop = d.StopSurface;
        return new LensDrawing
        {
            Regions = regions, Rays = rays, FieldAngles = fields.Select(v => v * halfField).ToArray(), StopZ = vertex[stop],
            StopSemi = Math.Abs(Builder.MarginalHeights(n, d.Curvature, d.Thickness)[stop]) * pupilR, ImageZ = vertex[m],
        };
    }

    /// <summary>
    /// Each surface's semi-diameter, as the lens is drawn and its edges judged: the highest of the real meridional rays
    /// at 0, 0.7 and 1 of <paramref name="halfField"/>, through the edges and center of the paraxial entrance pupil at the
    /// design wavelength, with 4% to spare. Null if any of those rays fails.
    /// </summary>
    public static double[]? SemiDiameters(Design d, double halfField)
    {
        var (semi, rays, _) = Rim(d, halfField);
        return rays.Count == 9 ? semi : null;
    }

    /// <summary>
    /// How far each surface is drawn from the axis: every face of an element, cemented pieces and all, is cut to the widest
    /// of its faces' <paramref name="semi"/>-diameters. A surface with air either side (a stop) keeps its own.
    /// </summary>
    public static double[] Rims(Design d, double[] semi)
    {
        var rim = (double[])semi.Clone();
        for (int q = 0; q < d.Count - 1; q++)
        {
            if (d.Glass[q] is null) continue;
            int end = q;
            while (end + 1 < d.Count - 1 && d.Glass[end + 1] is not null) end++;
            double h = Enumerable.Range(q, end - q + 2).Max(s => semi[s]);
            for (int s = q; s <= end + 1; s++) rim[s] = h;
            q = end;
        }
        return rim;
    }

    private static double[] Vertices(Design d)
    {
        var vertex = new double[d.Count + 1];
        for (int q = 0; q < d.Count; q++) vertex[q + 1] = vertex[q] + d.Thickness[q];
        return vertex;
    }

    private static (double[] Semi, List<(int, List<Pt>)> Rays, double[] Fields) Rim(Design d, double halfField)
    {
        var spec = d.Spec;
        var n = d.Indices(spec.DesignWavelength);
        int m = d.Count;
        var vertex = Vertices(d);

        // The paraxial entrance pupil: where the chief ray (unit slope) aims, and its radius.
        double pupilZ = -Builder.ChiefRay(n, d.StopSurface, d.Thickness, d.Curvature).Y[0];
        double pupilR = spec.Efl / (2 * spec.FNumber);
        double start = Math.Min(0, pupilZ) - 0.12 * vertex[m];

        var fields = new[] { 0.0, 0.7, 1.0 };
        var rays = new List<(int, List<Pt>)>();
        var semi = new double[m];
        for (int f = 0; f < fields.Length; f++)
            foreach (double rho in new[] { -1.0, 0.0, 1.0 })
            {
                double tan = Math.Tan(fields[f] * halfField * Math.PI / 180);
                double y0 = rho * pupilR + (start - pupilZ) * tan;
                if (Trace(n, d.Curvature, vertex, start, y0, tan) is not { } pts) continue;
                for (int q = 0; q < m; q++) semi[q] = Math.Max(semi[q], Math.Abs(pts[q + 1].Y));
                rays.Add((f, pts));
            }
        for (int q = 0; q < m; q++) semi[q] *= 1.04;
        return (semi, rays, fields);
    }

    /// <summary>A face from −h to h: its sag at each height, from the vertex at <paramref name="z"/>.</summary>
    private static IEnumerable<Pt> Face(double c, double z, double h)
    {
        const int steps = 40;
        for (int i = 0; i <= steps; i++)
        {
            double y = -h + 2 * h * i / steps;
            double s = 1 - c * c * y * y;
            double sag = c == 0 ? 0 : s <= 0 ? 1 / c : c * y * y / (1 + Math.Sqrt(s));
            yield return new Pt(z + sag, y);
        }
    }

    /// <summary>A meridional ray from (start, y0) with slope tan, through every surface to the image plane: its point at
    /// the start, at each surface and at the image; null if it misses a surface or is totally reflected.</summary>
    private static List<Pt>? Trace(double[] n, double[] c, double[] vertex, double start, double y0, double tan)
    {
        double norm = Math.Sqrt(1 + tan * tan);
        double m = tan / norm, nn = 1 / norm;          // direction cosines (y, z)
        double y = y0, z = start;
        var pts = new List<Pt> { new(z, y) };
        for (int k = 0; k < c.Length; k++)
        {
            double zl = z - vertex[k];                  // in the surface's frame
            double s, ny, nz;
            if (c[k] == 0)
            {
                s = -zl / nn;
                ny = 0; nz = 1;
            }
            else
            {
                double r = 1 / c[k], qz = zl - r;
                double b = y * m + qz * nn, cc = y * y + qz * qz - r * r, disc = b * b - cc;
                if (disc < 0) return null;
                double root = Math.Sqrt(disc), s1 = -b - root, s2 = -b + root;
                s = Math.Abs(zl + s1 * nn) < Math.Abs(zl + s2 * nn) ? s1 : s2;
                ny = -(y + s * m) * c[k]; nz = 1 - (zl + s * nn) * c[k];
            }
            y += s * m; z += s * nn;
            pts.Add(new Pt(z, y));
            double mu = n[k] / n[k + 1], cos1 = m * ny + nn * nz;
            if (cos1 < 0) { ny = -ny; nz = -nz; cos1 = -cos1; }
            double cos2 = 1 - mu * mu * (1 - cos1 * cos1);
            if (cos2 < 0) return null;
            double g = Math.Sqrt(cos2) - mu * cos1;
            m = mu * m + g * ny; nn = mu * nn + g * nz;
        }
        double toImage = (vertex[^1] - z) / nn;
        pts.Add(new Pt(vertex[^1], y + toImage * m));
        return pts;
    }
}
