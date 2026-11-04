using AberrationCalculator.Core.Aberrations;
using AberrationCalculator.Core.Glass;
using AberrationCalculator.Core.Models;
using AberrationCalculator.Core.RayTrace;

namespace ShafersSimpleLensDesign.Core;

/// <summary>RMS spot radius at one field, at each wavelength about its own centroid and for all together about theirs, mm.</summary>
public sealed record FieldSpot(double FieldDeg, double[] PerWavelength, double Polychromatic);

public sealed class AnalysisResult
{
    public required Design Design { get; init; }
    public required OpticalSystem System { get; init; }
    public required SeidelResult Seidel { get; init; }

    /// <summary>Spots on the design wavelength's paraxial image plane.</summary>
    public required FieldSpot[] Spots { get; init; }

    /// <summary>Spots on the best-focus plane: the one plane, for every field and wavelength, with the least summed
    /// mean square spot.</summary>
    public required FieldSpot[] BestSpots { get; init; }

    /// <summary>The best-focus plane, from the paraxial one (positive: further from the lens), mm.</summary>
    public required double BestFocusShift { get; init; }

    public required double PetzvalSum { get; init; }
    public required double AiryRadius { get; init; }
    public required int Vignetted { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// A built design analyzed by AberrationCalculator, independently of the builder: Seidel sums surface by surface (which
/// confirm each surface's role), and real-ray spots at every field and wavelength, on the paraxial image plane and on
/// the best-focus plane.
/// </summary>
public static class Analysis
{
    public static AnalysisResult Run(Design d, GlassCatalog catalog, int rings = 24)
    {
        var warnings = new List<string>();
        var sys = LensModel.Build(d, catalog, warnings);
        var spec = d.Spec;
        var nD = IndexResolver.Build(sys, catalog, spec.DesignWavelength, warnings);
        var nF = IndexResolver.Build(sys, catalog, spec.Wavelengths.Min(), warnings);
        var nC = IndexResolver.Build(sys, catalog, spec.Wavelengths.Max(), warnings);
        var par = ParaxialTrace.Trace(sys, nD, spec.HalfField);
        var seidel = SeidelCoefficients.Compute(sys, nD, nF, nC, par);

        // A polar grid of pupil points, uniform over the pupil's area: evenly spaced rings, each with points in proportion
        // to its circumference.
        var pupil = new List<(double X, double Y)>();
        for (int r = 1; r <= rings; r++)
        {
            double rho = (r - 0.5) / rings;
            int arms = 6 * r;
            for (int a = 0; a < arms; a++)
            {
                double th = 2 * Math.PI * a / arms;
                pupil.Add((rho * Math.Sin(th), rho * Math.Cos(th)));
            }
        }

        // Every ray on the paraxial image plane and on a plane delta beyond it; past the last surface rays are straight,
        // so their landing on any plane is linear in its distance.
        const double delta = 1.0;
        int last = sys.Surfaces.Count - 2;
        int vignetted = 0;
        var rays = new List<(int Field, int Lambda, double X0, double Y0, double Dx, double Dy)>();
        var indices = spec.Wavelengths.Select(l => IndexResolver.Build(sys, catalog, l, warnings)).ToArray();
        for (int f = 0; f < sys.Fields.Count; f++)
            for (int l = 0; l < spec.Wavelengths.Length; l++)
            {
                var parL = ParaxialTrace.Trace(sys, indices[l], sys.Fields[f].Y);
                foreach (var (px, py) in pupil)
                {
                    var a = RealRayTrace.Trace(sys, indices[l], parL, sys.Fields[f].Y, py, px, false);
                    sys.Surfaces[last].Thickness += delta;
                    var b = RealRayTrace.Trace(sys, indices[l], parL, sys.Fields[f].Y, py, px, false);
                    sys.Surfaces[last].Thickness -= delta;
                    if (!a.Ok || !b.Ok) { vignetted++; continue; }
                    rays.Add((f, l, a.Z, a.Y, (b.Z - a.Z) / delta, (b.Y - a.Y) / delta));
                }
            }

        // The plane z minimizing the summed polychromatic mean square spot over the fields: a quadratic in z.
        double num = 0, den = 0;
        foreach (var grp in rays.GroupBy(r => r.Field))
        {
            var g = grp.ToList();
            double mx = g.Average(r => r.X0), my = g.Average(r => r.Y0), mdx = g.Average(r => r.Dx), mdy = g.Average(r => r.Dy);
            foreach (var r in g)
            {
                double ex = r.X0 - mx, ey = r.Y0 - my, fx = r.Dx - mdx, fy = r.Dy - mdy;
                num += (ex * fx + ey * fy) / g.Count;
                den += (fx * fx + fy * fy) / g.Count;
            }
        }
        double best = den > 0 ? -num / den : 0;

        FieldSpot[] SpotsAt(double z) => Enumerable.Range(0, sys.Fields.Count).Select(f =>
        {
            var per = new double[spec.Wavelengths.Length];
            for (int l = 0; l < per.Length; l++)
                per[l] = Rms(rays.Where(r => r.Field == f && r.Lambda == l).Select(r => (r.X0 + z * r.Dx, r.Y0 + z * r.Dy)).ToList());
            return new FieldSpot(sys.Fields[f].Y, per, Rms(rays.Where(r => r.Field == f).Select(r => (r.X0 + z * r.Dx, r.Y0 + z * r.Dy)).ToList()));
        }).ToArray();

        double petzval = 0;
        for (int k = 0; k < d.Count; k++)
            petzval += d.Curvature[k] * (nD[k + 1] - nD[k]) / (nD[k] * nD[k + 1]);
        return new AnalysisResult
        {
            Design = d, System = sys, Seidel = seidel, Spots = SpotsAt(0), BestSpots = SpotsAt(best), BestFocusShift = best,
            PetzvalSum = petzval, AiryRadius = 1.22 * spec.DesignWavelength * 1e-3 * spec.FNumber, Vignetted = vignetted,
            Warnings = warnings.Distinct().ToList(),
        };
    }

    private static double Rms(List<(double X, double Y)> pts)
    {
        if (pts.Count == 0) return double.NaN;
        double cx = pts.Average(p => p.X), cy = pts.Average(p => p.Y);
        return Math.Sqrt(pts.Average(p => (p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)));
    }
}
