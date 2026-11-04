namespace ShafersSimpleLensDesign.Core;

/// <summary>
/// An exact skew-ray trace through spheres and planes, for the search's merit: rays from a distant object, through
/// the paraxial entrance pupil, to the image plane. Small and allocation-free, so a design can be judged in
/// microseconds. AberrationCalculator's own trace checks it (see the tests).
/// </summary>
public static class SkewTrace
{
    /// <summary>A ray on the image plane (the last surface's thickness beyond it): where, and which way.</summary>
    public readonly record struct Landing(double X, double Y, double L, double M, double N, bool Ok);

    /// <summary>
    /// Traces the ray of field angle <paramref name="fieldDeg"/> (in the y-z plane) through the point
    /// (<paramref name="px"/>, <paramref name="py"/>) of the entrance pupil, as fractions of its radius
    /// <paramref name="pupilRadius"/>; the pupil lies <paramref name="pupilZ"/> from the first vertex.
    /// </summary>
    public static Landing Trace(double[] n, double[] t, double[] c, double fieldDeg, double px, double py,
        double pupilZ, double pupilRadius)
    {
        double th = fieldDeg * Math.PI / 180;
        double l = 0, m = Math.Sin(th), nn = Math.Cos(th);
        // Start on the entrance pupil, then express the point in surface 1's frame (z from its vertex).
        double x = px * pupilRadius, y = py * pupilRadius, z = pupilZ;
        for (int k = 0; k < c.Length; k++)
        {
            double s, gx, gy, gz;
            if (c[k] == 0)
            {
                if (nn <= 0) return default;
                s = -z / nn;
                gx = 0; gy = 0; gz = 1;
            }
            else
            {
                double r = 1 / c[k];
                double qz = z - r;
                double b = x * l + y * m + qz * nn, cc = x * x + y * y + qz * qz - r * r;
                double disc = b * b - cc;
                if (disc < 0) return default;
                double root = Math.Sqrt(disc);
                double s1 = -b - root, s2 = -b + root;
                s = Math.Abs(z + s1 * nn) < Math.Abs(z + s2 * nn) ? s1 : s2;
                double xh = x + s * l, yh = y + s * m, zh = z + s * nn;
                gx = -xh * c[k]; gy = -yh * c[k]; gz = 1 - zh * c[k];
            }
            x += s * l; y += s * m; z += s * nn;

            double mu = n[k] / n[k + 1];
            double cos1 = l * gx + m * gy + nn * gz;
            if (cos1 < 0) { gx = -gx; gy = -gy; gz = -gz; cos1 = -cos1; }
            double cos2sq = 1 - mu * mu * (1 - cos1 * cos1);
            if (cos2sq < 0) return default;
            double g = Math.Sqrt(cos2sq) - mu * cos1;
            l = mu * l + g * gx; m = mu * m + g * gy; nn = mu * nn + g * gz;
            z -= t[k];                                   // into the next surface's frame; after the last, the image plane's
        }
        if (nn <= 0) return default;
        double toImage = -z / nn;
        return new Landing(x + toImage * l, y + toImage * m, l, m, nn, true);
    }
}

/// <summary>
/// The search's merit: real-ray RMS spot radius at best focus, from rays at 0, 0.7 and 1 of the field, every
/// wavelength, through the pupil at six Gauss-Legendre rings in rho squared, eight points each, weighted: the polynomial
/// aberrations are integrated over the pupil's area almost exactly with 48 rays. Best focus is the one plane, for all
/// fields and wavelengths, with the least summed mean square spot.
/// </summary>
public static class SpotMerit
{
    public readonly record struct Value(double Rms, double[] FieldRms, double FocusShift);

    private static readonly (double X, double Y, double W)[] Pupil = MakePupil(8);

    private static (double, double, double)[] MakePupil(int arms)
    {
        double[] x = { -0.9324695142031521, -0.6612093864662645, -0.2386191860831969, 0.2386191860831969, 0.6612093864662645, 0.9324695142031521 };
        double[] w = { 0.1713244923791704, 0.3607615730481386, 0.4679139345726910, 0.4679139345726910, 0.3607615730481386, 0.1713244923791704 };
        var p = new List<(double, double, double)>();
        for (int r = 0; r < x.Length; r++)
        {
            double rho = Math.Sqrt((x[r] + 1) / 2);
            for (int a = 0; a < arms; a++)
            {
                double th = 2 * Math.PI * (a + 0.5) / arms;
                p.Add((rho * Math.Sin(th), rho * Math.Cos(th), w[r] / 2 / arms));
            }
        }
        return p.ToArray();
    }

    /// <summary>The merit of a built design, mm; null if any ray fails.</summary>
    public static Value? Of(Design d) => Rays(d) is { } r ? Merit(r, out _) : null;

    /// <summary>
    /// The merit as least-squares residuals: each ray's error from its field's centroid on the best-focus plane, x and y,
    /// times the square root of its weight, scaled so that their sum of squares is the merit's square, mm. Null if any ray fails.
    /// </summary>
    public static double[]? Residuals(Design d, out Value merit)
    {
        merit = default;
        if (Rays(d) is not { } rays) return null;
        merit = Merit(rays, out var center);
        double z = merit.FocusShift;
        int fields = rays.Length;
        var res = new double[rays.Sum(f => 2 * f.Length)];
        int i = 0;
        for (int f = 0; f < fields; f++)
        {
            double wsum = rays[f].Sum(r => r.W);
            foreach (var r in rays[f])
            {
                double s = Math.Sqrt(r.W / wsum / fields);
                res[i++] = s * (r.X + z * r.Dx - center[f].X - z * center[f].Dx);
                res[i++] = s * (r.Y + z * r.Dy - center[f].Y - z * center[f].Dy);
            }
        }
        return res;
    }

    private readonly record struct Ray(double X, double Y, double Dx, double Dy, double W);

    /// <summary>Every ray's landing on the paraxial image plane and its direction, by field.</summary>
    private static Ray[][]? Rays(Design d)
    {
        var spec = d.Spec;
        var fields = new[] { 0.0, 0.7, 1.0 };
        var idx = spec.Wavelengths.Select(d.Indices).ToArray();

        // Each wavelength through its own paraxial entrance pupil, filling the one stop: the pupil is where that
        // wavelength's chief ray (slope 1) crosses the axis, and its radius brings the marginal ray to the stop's edge.
        double StopHeight(double[] n) => Builder.MarginalHeights(n, d.Curvature, d.Thickness)[d.StopSurface];
        double stopEdge = spec.Efl / (2 * spec.FNumber) * StopHeight(d.Indices(spec.DesignWavelength));
        var pupilZ = idx.Select(n => -Builder.ChiefRay(n, d.StopSurface, d.Thickness, d.Curvature).Y[0]).ToArray();
        var pupilR = idx.Select(n => stopEdge / StopHeight(n)).ToArray();

        var rays = new Ray[fields.Length][];
        for (int f = 0; f < fields.Length; f++)
        {
            rays[f] = new Ray[idx.Length * Pupil.Length];
            int count = 0;
            for (int l = 0; l < idx.Length; l++)
                foreach (var p in Pupil)
                {
                    var r = SkewTrace.Trace(idx[l], d.Thickness, d.Curvature, fields[f] * spec.HalfField, p.X, p.Y, pupilZ[l], pupilR[l]);
                    if (!r.Ok) return null;
                    rays[f][count++] = new Ray(r.X, r.Y, r.L / r.N, r.M / r.N, p.W * (spec.WavelengthWeights?[l] ?? 1));
                }
        }
        return rays;
    }

    /// <summary>The merit from the rays: per field, the quadratic in the focus shift z of the mean square spot about the
    /// centroid; the best focus minimizes their sum. <paramref name="center"/> is each field's weighted mean ray.</summary>
    private static Value Merit(Ray[][] rays, out Ray[] center)
    {
        center = new Ray[rays.Length];
        var sums = new (double Ex2, double ExFx, double Fx2)[rays.Length];
        for (int f = 0; f < rays.Length; f++)
        {
            double mx = 0, my = 0, mdx = 0, mdy = 0, wsum = 0;
            foreach (var r in rays[f]) { mx += r.W * r.X; my += r.W * r.Y; mdx += r.W * r.Dx; mdy += r.W * r.Dy; wsum += r.W; }
            mx /= wsum; my /= wsum; mdx /= wsum; mdy /= wsum;
            center[f] = new Ray(mx, my, mdx, mdy, wsum);
            double e2 = 0, ef = 0, f2 = 0;
            foreach (var r in rays[f])
            {
                double ex = r.X - mx, ey = r.Y - my, fx = r.Dx - mdx, fy = r.Dy - mdy;
                e2 += r.W * (ex * ex + ey * ey); ef += r.W * (ex * fx + ey * fy); f2 += r.W * (fx * fx + fy * fy);
            }
            sums[f] = (e2 / wsum, ef / wsum, f2 / wsum);
        }
        double num = sums.Sum(s => s.ExFx), den = sums.Sum(s => s.Fx2);
        double z = den > 0 ? -num / den : 0;
        var per = sums.Select(s => Math.Sqrt(Math.Max(0, s.Ex2 + 2 * z * s.ExFx + z * z * s.Fx2))).ToArray();
        return new Value(Math.Sqrt(per.Average(v => v * v)), per, z);
    }
}
