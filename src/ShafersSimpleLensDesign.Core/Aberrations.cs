namespace ShafersSimpleLensDesign.Core;

/// <summary>Seidel sums of one surface, and the Petzval sum's term, from the paraxial marginal and chief rays.</summary>
public readonly record struct SurfaceSums(double S1, double S2, double S3, double S4, double S5, double Petzval);

/// <summary>
/// The third-order sums the targets are solved against, by Seidel's formulas (as Welford gives them) from the paraxial
/// marginal ray (y, u) and chief ray (ȳ, ū) at each surface:
/// A = n(yc + u), Ā = n(ȳc + ū), Δ(u/n) = u′/n′ − u/n, H = n(ūy − uȳ);
/// S1 = −A²yΔ(u/n), S2 = −AĀyΔ(u/n), S3 = −Ā²yΔ(u/n), S4 = −H²cΔ(1/n), S5 = (Ā/A)(S3 + S4)
/// = Ā[−Ā²yΔ(1/n²) + cȳΔ(1/n)(2Āy − Aȳ)].
/// A concentric surface has Ā = 0, so no S2, S3 or S5; an aplanatic surface has Δ(u/n) = 0, so no S1, S2 or S3.
/// </summary>
public static class Aberrations
{
    public static SurfaceSums[] Surfaces(double[] n, int stop, double[] t, double[] c)
    {
        var (yb, ub) = Builder.ChiefRay(n, stop, t, c);
        var sums = new SurfaceSums[c.Length];
        double y = 1, u = 0;
        double h = n[0] * (ub[0] * y - u * yb[0]);
        for (int k = 0; k < c.Length; k++)
        {
            double n1 = n[k], n2 = n[k + 1];
            double u2 = (n1 * u - y * (n2 - n1) * c[k]) / n2;
            double a = n1 * (y * c[k] + u), ab = n1 * (yb[k] * c[k] + ub[k]);
            double d = u2 / n2 - u / n1;
            double s1 = -a * a * y * d, s2 = -a * ab * y * d, s3 = -ab * ab * y * d;
            double s4 = -h * h * c[k] * (1 / n2 - 1 / n1);
            // (Ā/A)(S3 + S4) with A divided out, so a surface centered on the marginal ray's axial point (A = 0) has one too.
            double s5 = ab * (-ab * ab * y * (1 / (n2 * n2) - 1 / (n1 * n1)) + c[k] * yb[k] * (1 / n2 - 1 / n1) * (2 * ab * y - a * yb[k]));
            sums[k] = new SurfaceSums(s1, s2, s3, s4, s5, c[k] * (n2 - n1) / (n1 * n2));
            u = u2;
            if (k < c.Length - 1) y += t[k] * u;
        }
        return sums;
    }

    /// <summary>The value of each target's aberration for the lens (c, t): what the knobs are solved to make zero.</summary>
    public static double[] Of(LensSpec spec, AberrationCalculator.Core.Glass.GlassData?[] glass, double[] n, int stop, double[] t, double[] c,
        IReadOnlyList<Target> targets)
    {
        var s = Surfaces(n, stop, t, c);
        return targets.Select(g => g.What switch
        {
            Aberration.Petzval => s.Sum(v => v.Petzval),
            Aberration.Spherical => s.Sum(v => v.S1),
            Aberration.Coma => s.Sum(v => v.S2),
            Aberration.Astigmatism => s.Sum(v => v.S3),
            Aberration.Distortion => s.Sum(v => v.S5),
            Aberration.RealSpherical => RealSpherical(spec, n, t, c, g.Zone),
            Aberration.AxialColor => Color(spec, glass, stop, t, c).Axial,
            Aberration.LateralColor => Color(spec, glass, stop, t, c).Lateral,
            _ => throw new ArgumentOutOfRangeException(nameof(targets)),
        }).ToArray();
    }

    /// <summary>
    /// Paraxial color between the shortest and longest wavelengths, divided by the focal length (so independent of the
    /// lens's scale): axial, the difference of their foci; lateral, of their image heights on the design wavelength's
    /// image plane for a unit field angle.
    /// </summary>
    public static (double Axial, double Lateral) Color(LensSpec spec, AberrationCalculator.Core.Glass.GlassData?[] glass, int stop, double[] t, double[] c)
    {
        var nD = Builder.Indices(glass, spec.DesignWavelength);
        var (yd, ud) = Builder.Marginal(spec, nD, c, t);
        double efl = -1 / ud, image = -yd / ud;
        (double Focus, double Height) At(double lambda)
        {
            var n = Builder.Indices(glass, lambda);
            var (y, u) = Builder.Marginal(spec, n, c, t);
            var (yb, ub) = Builder.ChiefRay(n, stop, t, c);
            int k = c.Length - 1;
            double uOut = (n[k] * ub[k] - yb[k] * (n[k + 1] - n[k]) * c[k]) / n[k + 1];
            return (-y / u, yb[k] + image * uOut);
        }
        var lo = At(spec.Wavelengths.Min());
        var hi = At(spec.Wavelengths.Max());
        return ((lo.Focus - hi.Focus) / efl, (lo.Height - hi.Height) / efl);
    }

    /// <summary>
    /// Longitudinal spherical aberration of the real ray at <see cref="LensSpec.SphericalZone"/> of the entrance pupil,
    /// relative to the paraxial focus, divided by the focal length (so independent of the lens's scale). The ray enters
    /// parallel to the axis, from infinity.
    /// </summary>
    public static double RealSpherical(LensSpec spec, double[] n, double[] t, double[] c, double zone = 0)
    {
        var (yp, up) = Builder.Marginal(spec, n, c, t);
        double efl = -1 / up, focus = -yp / up;
        double h = (zone > 0 ? zone : spec.SphericalZone) * efl / (2 * spec.FNumber);
        var ray = Meridional.Trace(n, t, c, h);
        if (!ray.Ok) throw new InvalidOperationException("the real marginal ray does not pass");
        // The ray's axial crossing measured from the last vertex.
        double crossing = ray.Z - ray.Y * ray.N / ray.M;
        return (crossing - focus) / efl;
    }

    /// <summary>Paraxial color of a built design, between its shortest and longest wavelengths, in lens units: the
    /// foci, and the image heights at full field.</summary>
    public static ParaxialColor ColorOf(Design d)
    {
        var spec = d.Spec;
        double lo = spec.Wavelengths.Min(), hi = spec.Wavelengths.Max();
        double Focus(double l)
        {
            var (y, u) = Builder.Marginal(spec, d.Indices(l), d.Curvature, d.Thickness);
            return -y / u;
        }
        double Height(double l)
        {
            var n = d.Indices(l);
            var (yb, ub) = Builder.ChiefRay(n, d.StopSurface, d.Thickness, d.Curvature);
            int k = d.Count - 1;
            double u = (n[k] * ub[k] - yb[k] * (n[k + 1] - n[k]) * d.Curvature[k]) / n[k + 1];
            return (yb[k] + d.Thickness[k] * u) * Math.Tan(spec.HalfField * Math.PI / 180);
        }
        return new ParaxialColor(Focus(lo) - Focus(hi), Height(lo) - Height(hi));
    }
}

/// <summary>Paraxial axial and lateral color: the shortest wavelength's less the longest's.</summary>
public readonly record struct ParaxialColor(double Axial, double Lateral);
