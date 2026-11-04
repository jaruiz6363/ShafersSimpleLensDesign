using AberrationCalculator.Core.Glass;

namespace ShafersSimpleLensDesign.Core;

/// <summary>A lens built from a surface order: a curvature, a thickness and a glass per surface, at the target focal
/// length. The last thickness is the paraxial back focal distance at the design wavelength.</summary>
public sealed class Design
{
    public required LensSpec Spec { get; init; }
    public required double[] Curvature { get; init; }
    public required double[] Thickness { get; init; }
    public required GlassData?[] Glass { get; init; }
    public required int StopSurface { get; init; }

    /// <summary>The thicknesses of the surface order (before scaling) that the targets were solved with.</summary>
    public required double[] SolvedThickness { get; init; }

    public int Count => Curvature.Length;
    public double Radius(int k) => Curvature[k] == 0 ? double.PositiveInfinity : 1 / Curvature[k];

    /// <summary>Index after each surface at <paramref name="lambda"/>; entry 0 is the object space (air).</summary>
    public double[] Indices(double lambda) => Builder.Indices(Glass, lambda);
}

/// <summary>
/// Builds a lens from a surface order by Shafer's rules, from the paraxial marginal and chief rays at the design
/// wavelength:
/// <list type="bullet">
/// <item><b>Concentric</b>: curvature −ū/ȳ, centring the surface where the incident chief ray (height ȳ, slope ū) crosses
/// the axis.</item>
/// <item><b>Aplanatic</b>: curvature −(n + n′)u/(n y), putting the incident marginal ray's axial point (height y, slope u)
/// at the distance l = R(n + n′)/n from the vertex that Abbe's aplanatic conjugates require.</item>
/// <item><b>Buried</b> and <b>fixed</b>: the curvature given, or solved for a target.</item>
/// </list>
/// A concentric surface ahead of the stop depends on the chief ray, so on every surface between it and the stop, which
/// may in turn depend on it; the curvatures are therefore solved together, as the fixed point of the rules, by Newton's
/// method. Then the targets' knobs (thicknesses, or the radii of buried surfaces) are solved, and the lens is scaled to
/// the focal length.
/// </summary>
public static class Builder
{
    /// <summary>The glass a search chooses, and the glass that repeats the one before.</summary>
    public const string Any = "*", Same = "=";

    /// <summary>
    /// The surface whose glass surface <paramref name="k"/>'s repeats (0-based), or null if it names its own: "=" repeats the
    /// glass just before it (a stop or buried plane inside a lens), "=j" the glass after surface j (two elements of one
    /// glass, as when Shafer splits an element in two). -1 for "=" on the first surface (air).
    /// </summary>
    public static int? Tied(LensSpec spec, int k)
    {
        string id = spec.Surfaces[k].Glass;
        if (id == Same) return k - 1;
        if (id.Length > 1 && id[0] == '=')
        {
            if (!int.TryParse(id.AsSpan(1), out int j) || j < 1 || j > k)
                throw new ArgumentException($"surface {k + 1}: '{id}' must name an earlier surface, =1 to ={k}");
            return j - 1;
        }
        return null;
    }

    /// <summary>The surface whose glass choice surface <paramref name="k"/>'s glass is, following repeats back.</summary>
    public static int Source(LensSpec spec, int k)
    {
        while (k >= 0 && Tied(spec, k) is int j) k = j;
        return k;
    }

    /// <summary>The glass after each surface. "=" repeats the glass before the surface (a stop inside a lens); "*" is a
    /// glass the search chooses, and is left null here.</summary>
    public static GlassData?[] ResolveGlasses(LensSpec spec, GlassCatalog catalog)
    {
        var g = new GlassData?[spec.Surfaces.Count];
        for (int k = 0; k < g.Length; k++)
        {
            string id = spec.Surfaces[k].Glass;
            g[k] = id.Length == 0 || id == Any ? null
                : Tied(spec, k) is int j ? (j >= 0 ? g[j] : null)
                : ModelIndex(id) is double nd ? GlassSet.Model(nd)
                : (id.Contains(':') ? catalog.Find(id) : null)
                  ?? throw new ArgumentException($"glass '{id}' not found: name it CATALOG:NAME, e.g. SCHOTT:N-BK7");
        }
        return g;
    }

    /// <summary>The index of a model glass named MODEL:index (no dispersion), or null for any other name.</summary>
    private static double? ModelIndex(string id) =>
        id.StartsWith(GlassSet.ModelCatalog + ":", StringComparison.OrdinalIgnoreCase)
        && double.TryParse(id.AsSpan(GlassSet.ModelCatalog.Length + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n) && n >= 1
            ? n : null;

    public static double[] Indices(GlassData?[] glass, double lambda)
    {
        var n = new double[glass.Length + 1];
        n[0] = 1.0;
        for (int k = 0; k < glass.Length; k++) n[k + 1] = glass[k]?.IndexAt(lambda) ?? 1.0;
        return n;
    }

    /// <summary>Builds the lens: the rules' curvatures, the targets solved, scaled to the focal length.</summary>
    public static Design Build(LensSpec spec, GlassCatalog catalog)
    {
        var glass = ResolveGlasses(spec, catalog);
        if (spec.Surfaces.Any(s => s.Glass == Any)) throw new ArgumentException("the surface order has glasses to search (*): use search, not build");
        return Build(spec, glass);
    }

    /// <summary>Builds the lens with the glasses given, one per surface (null for air).</summary>
    public static Design Build(LensSpec spec, GlassData?[] glass)
    {
        var n = Indices(glass, spec.DesignWavelength);
        int stop = StopIndex(spec);
        int count = spec.Surfaces.Count;
        var t = spec.Surfaces.Select(s => s.Thickness).ToArray();
        var fc = spec.Surfaces.Select(s => s.Role is Role.Fixed or Role.Buried && s.Radius != 0 ? 1 / s.Radius : 0.0).ToArray();

        if (spec.Targets.Count > 0) SolveTargets(spec, glass, n, stop, t, fc);
        var c = Curvatures(spec, n, stop, t, fc);

        // Scale to the focal length: with y = 1 and u = 0 in, the focal length is −1/u′ out.
        var (y, u) = Marginal(spec, n, c, t);
        double efl = -1 / u, s = spec.Efl / efl;
        if (!(efl > 0)) throw new InvalidOperationException($"the lens has a focal length of {efl:G4}: it does not form a real image");
        var thick = t.Select(v => v * s).ToArray();
        thick[^1] = -y / u * s;
        return new Design
        {
            Spec = spec, Curvature = c.Select(v => v / s).ToArray(), Thickness = thick, Glass = glass, StopSurface = stop,
            SolvedThickness = t,
        };
    }

    private static int StopIndex(LensSpec spec)
    {
        int stop = spec.Surfaces.FindIndex(s => s.IsStop || s.Role == Role.Stop);
        if (stop < 0) throw new ArgumentException("the surface order has no stop: mark one surface with @, or add S");
        return stop;
    }

    /// <summary>The curvature of every surface under its rule, for thicknesses <paramref name="t"/>, and with
    /// <paramref name="fc"/> the curvatures of the buried and fixed surfaces.</summary>
    public static double[] Curvatures(LensSpec spec, double[] n, int stop, double[] t, double[] fc)
    {
        int count = spec.Surfaces.Count;
        var c = new double[count];
        for (int k = 0; k < count; k++)
            if (spec.Surfaces[k].Role is Role.Fixed or Role.Buried) c[k] = fc[k];

        // A few passes of the rules themselves give a starting point; Newton finishes it.
        for (int pass = 0; pass < 3; pass++) c = Rules(spec, n, stop, t, c, fc);
        double previous = double.PositiveInfinity;
        int stalled = 0;
        for (int it = 0; it < 30; it++)
        {
            var g = Residual(spec, n, stop, t, c, fc);
            double size = g.Max(Math.Abs);
            // Converged to rounding: Newton has stopped gaining (each step at least squares the error until then).
            if (size < 1e-13 * Scale(c) && size >= 0.5 * previous) return c;
            if (size < 1e-15 * Scale(c)) return c;
            // Far from converged and not gaining either: no consistent set of radii from here.
            if (size >= 0.5 * previous && ++stalled >= 4) break;
            previous = size;
            var jac = new double[count, count];
            for (int j = 0; j < count; j++)
            {
                double h = 1e-7 * Math.Max(Math.Abs(c[j]), Scale(c));
                var cj = (double[])c.Clone();
                cj[j] += h;
                var gj = Residual(spec, n, stop, t, cj, fc);
                for (int i = 0; i < count; i++) jac[i, j] = (gj[i] - g[i]) / h;
            }
            var step = Linear.Solve(jac, g) ?? throw new InvalidOperationException("the surface order has no consistent set of radii");
            for (int k = 0; k < count; k++) c[k] -= step[k];
        }
        if (Residual(spec, n, stop, t, c, fc).Max(Math.Abs) < 1e-10 * Scale(c)) return c;
        throw new InvalidOperationException("the radii the rules call for did not converge");
    }

    private static double Scale(double[] c) => Math.Max(1e-300, c.Max(Math.Abs));

    private static double[] Residual(LensSpec spec, double[] n, int stop, double[] t, double[] c, double[] fc)
    {
        var r = Rules(spec, n, stop, t, c, fc);
        for (int k = 0; k < c.Length; k++) r[k] = c[k] - r[k];
        return r;
    }

    /// <summary>The curvature each rule asks for, given the rays the curvatures <paramref name="c"/> produce. Buried and
    /// fixed surfaces ask for their given curvature, <paramref name="fc"/>.</summary>
    private static double[] Rules(LensSpec spec, double[] n, int stop, double[] t, double[] c, double[] fc)
    {
        int count = c.Length;
        var next = (double[])c.Clone();
        var (yb, ub) = ChiefRay(n, stop, t, c);
        double y = 1, u = 0;
        for (int k = 0; k < count; k++)
        {
            var role = spec.Surfaces[k].Role;
            if (role == Role.Aplanatic)
                next[k] = Math.Abs(u) < 1e-300 ? 0 : -(n[k] + n[k + 1]) * u / (n[k] * y);
            else if (role == Role.Concentric)
            {
                if (Math.Abs(yb[k]) < 1e-12 * Math.Max(1, Math.Abs(ub[k])))
                    throw new ArgumentException($"surface {k + 1} is concentric about the chief ray but sits where the chief ray crosses the axis (the stop)");
                next[k] = -ub[k] / yb[k];
            }
            else if (role == Role.Marginal)
            {
                if (Math.Abs(y) < 1e-12) throw new ArgumentException($"surface {k + 1} is centered on the marginal ray's axial point but sits on it");
                next[k] = -u / y;
            }
            else if (role == Role.Stop) next[k] = 0;
            else next[k] = fc[k];
            u = (n[k] * u - y * (n[k + 1] - n[k]) * c[k]) / n[k + 1];
            if (k < count - 1) y += t[k] * u;
        }
        return next;
    }

    /// <summary>The paraxial marginal ray, y = 1 and u = 0 at the first surface: its height at, and slope after, the last.</summary>
    public static (double Y, double U) Marginal(LensSpec spec, double[] n, double[] c, double[] t)
    {
        double y = 1, u = 0;
        for (int k = 0; k < c.Length; k++)
        {
            u = (n[k] * u - y * (n[k + 1] - n[k]) * c[k]) / n[k + 1];
            if (k < c.Length - 1) y += t[k] * u;
        }
        return (y, u);
    }

    /// <summary>The paraxial marginal ray from infinity, y = 1 at the first surface: its height at each surface.</summary>
    public static double[] MarginalHeights(double[] n, double[] c, double[] t)
    {
        var ys = new double[c.Length];
        double y = 1, u = 0;
        for (int k = 0; k < c.Length; k++)
        {
            ys[k] = y;
            u = (n[k] * u - y * (n[k + 1] - n[k]) * c[k]) / n[k + 1];
            if (k < c.Length - 1) y += t[k] * u;
        }
        return ys;
    }

    /// <summary>
    /// The paraxial chief ray, at unit incident slope, through the center of the stop: its height at, and slope incident
    /// on, each surface. Height is linear in the launch height, so two traces find the launch that crosses the stop.
    /// </summary>
    public static (double[] Y, double[] U) ChiefRay(double[] n, int stop, double[] t, double[] c)
    {
        (double[] Y, double[] U) Trace(double y0)
        {
            var ys = new double[c.Length];
            var us = new double[c.Length];
            double y = y0, u = 1;
            for (int k = 0; k < c.Length; k++)
            {
                ys[k] = y; us[k] = u;
                u = (n[k] * u - y * (n[k + 1] - n[k]) * c[k]) / n[k + 1];
                if (k < c.Length - 1) y += t[k] * u;
            }
            return (ys, us);
        }
        var a = Trace(0);
        var b = Trace(1);
        double slope = b.Y[stop] - a.Y[stop];
        if (Math.Abs(slope) < 1e-300) throw new InvalidOperationException("the stop is not imaged into object space");
        return Trace(-a.Y[stop] / slope);
    }

    private static void SolveTargets(LensSpec spec, GlassData?[] glass, double[] n, int stop, double[] t, double[] fc)
    {
        foreach (var g in spec.Targets)
        {
            if (g.Radius && (g.Knob < 1 || g.Knob > spec.Surfaces.Count || spec.Surfaces[g.Knob - 1].Role is not (Role.Buried or Role.Fixed)))
                throw new ArgumentException($"knob r{g.Knob}: only a buried (B) or fixed (F) surface's radius may vary");
            if (g.Position && (g.Knob < 2 || g.Knob >= spec.Surfaces.Count))
                throw new ArgumentException($"knob p{g.Knob}: give a surface with thicknesses before and after it, inside an element");
            if (!g.Radius && !g.Position && (g.Knob < 1 || g.Knob >= spec.Surfaces.Count))
                throw new ArgumentException($"knob {g.Knob}: give the number of a surface whose thickness, to the next surface, may vary");
        }

        // Real spherical aberration is solved from the third-order solution: from an arbitrary start the real marginal
        // ray may not pass, while third order always can be evaluated, and lands close.
        if (spec.Targets.Any(g => g.What == Aberration.RealSpherical))
            Solve(spec, glass, n, stop, t, fc, ThirdOrderStandIn(spec.Targets));
        Solve(spec, glass, n, stop, t, fc, spec.Targets);
    }

    /// <summary>
    /// Newton from the lens as it is; failing that, with <see cref="LensSpec.Continuation"/>, a homotopy. The targets'
    /// values at the start, f0, are a solution of f = (1 - s) f0 at s = 0; the solution curve is followed to s = 1 by
    /// pseudo-arclength continuation (a step along its tangent, then Newton back onto it at right angles), which turns
    /// round a fold, where s would have to go back for a while, instead of stopping there. A target new to a solved lens
    /// (a buried surface's color, a second zone) is so bent in gradually instead of from a start Newton cannot reach it from.
    /// </summary>
    private static void Solve(LensSpec spec, GlassData?[] glass, double[] n, int stop, double[] t, double[] fc, IReadOnlyList<Target> targets)
    {
        var t0 = (double[])t.Clone();
        var fc0 = (double[])fc.Clone();
        try { Newton(new Knobs(spec, glass, n, stop, targets), t, fc); }
        catch (InvalidOperationException) when (spec.Continuation)
        {
            Array.Copy(t0, t, t.Length);
            Array.Copy(fc0, fc, fc.Length);
            Arclength(new Knobs(spec, glass, n, stop, targets), t, fc);
        }
    }

    /// <summary>The homotopy f(x) = (1 - s) f0 followed from s = 0 to 1 by pseudo-arclength continuation, then Newton
    /// to finish. Variables are scaled by their starting size, so the arc length weighs a thickness and a curvature alike.</summary>
    private static void Arclength(Knobs k, double[] t, double[] fc)
    {
        int m = k.Count;
        var t0Of = (double[])t.Clone();
        var fc0Of = (double[])fc.Clone();
        var f0 = k.Values(t, fc);
        var x0 = k.Get(t, fc);
        var scale = x0.Select((v, j) => k.Targets[j].Radius ? Math.Max(Math.Abs(v), 0.1 * Scale(k.Curvatures(t, fc))) : Math.Max(Math.Abs(v), 1e-3)).ToArray();
        // z = (x / scale, s).
        double[] Z(double[] tt, double[] ff, double s) => k.Get(tt, ff).Select((v, j) => v / scale[j]).Append(s).ToArray();
        (double[] T, double[] F)? Lens(double[] z)
        {
            var tt = (double[])t0Of.Clone();
            var ff = (double[])fc0Of.Clone();
            return k.Set(tt, ff, z.Take(m).Select((v, j) => v * scale[j]).ToArray()) ? (tt, ff) : null;
        }
        double[] F(double[] z, (double[] T, double[] F) lens) => k.Values(lens.T, lens.F).Select((v, i) => v - (1 - z[m]) * f0[i]).ToArray();
        // d F / d z: the knobs' columns by differences, the s column is f0.
        double[,]? Jacobian(double[] z, double[] fz)
        {
            var a = new double[m, m + 1];
            for (int j = 0; j < m; j++)
            {
                var zj = (double[])z.Clone();
                double h = 1e-6 * Math.Max(1, Math.Abs(z[j]));
                zj[j] += h;
                if (Lens(zj) is not { } lj) return null;
                double[] fj;
                try { fj = F(zj, lj); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return null; }
                for (int i = 0; i < m; i++) a[i, j] = (fj[i] - fz[i]) / h;
            }
            for (int i = 0; i < m; i++) a[i, m] = f0[i];
            return a;
        }
        // The tangent: A tau = 0, continuing the way the last one pointed.
        double[]? Tangent(double[,] a, double[] previous)
        {
            var b = new double[m + 1, m + 1];
            for (int i = 0; i < m; i++) for (int j = 0; j <= m; j++) b[i, j] = a[i, j];
            for (int j = 0; j <= m; j++) b[m, j] = previous[j];
            var rhs = new double[m + 1];
            rhs[m] = 1;
            var tau = Linear.Solve(b, rhs);
            if (tau is null) return null;
            double norm = Math.Sqrt(tau.Sum(v => v * v));
            return tau.Select(v => v / norm).ToArray();
        }

        var z = Z(t, fc, 0);
        var tangent = new double[m + 1];
        tangent[m] = 1;
        double step = 0.1;
        string why = "the path could not be followed";
        for (int it = 0; it < 300 && step > 1e-5; it++)
        {
            var here = Lens(z)!.Value;
            var a = Jacobian(z, F(z, here));
            var tau = a is null ? null : Tangent(a, tangent);
            if (tau is null) { why = "the knobs stop acting independently"; break; }
            // Predict along the tangent, then correct at right angles to it.
            var zp = z.Select((v, j) => v + step * tau[j]).ToArray();
            var zc = (double[])zp.Clone();
            bool ok = false;
            why = "a step could not be corrected back onto the path";
            for (int c = 0; c < 8; c++)
            {
                if (Lens(zc) is not { } lc) break;
                double[] fz;
                try { fz = F(zc, lc); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { why = ex.Message; break; }
                var j = Jacobian(zc, fz);
                if (j is null) { why = "just beyond, the rays or the surface rules fail"; break; }
                var b = new double[m + 1, m + 1];
                var rhs = new double[m + 1];
                for (int i = 0; i < m; i++) { for (int q = 0; q <= m; q++) b[i, q] = j[i, q]; rhs[i] = fz[i]; }
                for (int q = 0; q <= m; q++) { b[m, q] = tau[q]; rhs[m] += tau[q] * (zc[q] - zp[q]); }
                var dz = Linear.Solve(b, rhs);
                if (dz is null) break;
                for (int q = 0; q <= m; q++) zc[q] -= dz[q];
                if (dz.Max(Math.Abs) < 1e-7 * Math.Max(1, zc.Max(Math.Abs))) { ok = Lens(zc) is not null; break; }
            }
            if (!ok) { step /= 2; continue; }
            if (zc[m] >= 1)
            {
                // Past the goal: from the point on the path at s = 1, by interpolation, Newton finishes.
                double w = (1 - z[m]) / (zc[m] - z[m]);
                var lens = Lens(z.Select((v, q) => v + w * (zc[q] - v)).ToArray());
                if (lens is { } l)
                    try { Newton(k, l.T, l.F); Array.Copy(l.T, t, t.Length); Array.Copy(l.F, fc, fc.Length); return; }
                    catch (InvalidOperationException ex) { why = ex.Message; }
                step /= 2;
                continue;
            }
            z = zc;
            tangent = tau;
            step = Math.Min(1.0, 1.5 * step);
        }
        var last = Lens(z)!.Value;
        // Usually the path runs into a thickness that would have to go negative: say which, it is the design's limit.
        var thin = Enumerable.Range(0, last.T.Length - 1).Where(q => last.T[q] < 1e-3 * last.T.Max()).Select(q => $"{q + 1}-{q + 2}").ToList();
        throw new InvalidOperationException($"the targets did not converge, even by continuation: reached {z[m]:P0} of the way"
            + (thin.Count > 0 ? $", where the thickness between surfaces {string.Join(", ", thin)} reaches zero" : $" ({why})"));
    }

    /// <summary>
    /// The targets with third-order spherical aberration standing in for the first real one; any further real ones (other
    /// zones, for the fifth order) are dropped, their knobs left where they start.
    /// </summary>
    public static List<Target> ThirdOrderStandIn(IReadOnlyList<Target> targets)
    {
        var list = new List<Target>();
        bool first = true;
        foreach (var g in targets)
        {
            if (g.What != Aberration.RealSpherical) list.Add(g);
            else if (first) { list.Add(g with { What = Aberration.Spherical }); first = false; }
        }
        return list;
    }

    /// <summary>
    /// The targets' knobs. A knob's value lives in t (a thickness) or fc (a radius's curvature). A thickness that runs into
    /// a buried surface is part of one element: the knob is then the element's whole thickness, the buried surface keeping
    /// its place in proportion, so the element can be any thickness.
    /// </summary>
    private sealed class Knobs(LensSpec spec, GlassData?[] glass, double[] n, int stop, IReadOnlyList<Target> targets)
    {
        public IReadOnlyList<Target> Targets => targets;
        public int Count => targets.Count;
        public double[] Curvatures(double[] t, double[] fc) => Builder.Curvatures(spec, n, stop, t, fc);
        public double[] Values(double[] t, double[] fc) => Aberrations.Of(spec, glass, n, stop, t, Curvatures(t, fc), targets);

        private int Span(int k)
        {
            int last = k;
            while (last + 1 < spec.Surfaces.Count - 1 && spec.Surfaces[last + 1].IsBuried) last++;
            return last;
        }

        public double Get(Target g, double[] t, double[] fc)
        {
            if (g.Radius) return fc[g.Knob - 1];
            if (g.Position) return t[g.Knob - 2];
            double sum = 0;
            for (int k = g.Knob - 1; k <= Span(g.Knob - 1); k++) sum += t[k];
            return sum;
        }

        public void Set(Target g, double[] t, double[] fc, double v)
        {
            if (g.Radius) { fc[g.Knob - 1] = v; return; }
            if (g.Position)
            {
                // Moving the surface within its element: what the thickness before it gains, the one after it loses.
                t[g.Knob - 1] += t[g.Knob - 2] - v;
                t[g.Knob - 2] = v;
                return;
            }
            double old = Get(g, t, fc);
            for (int k = g.Knob - 1; k <= Span(g.Knob - 1); k++) t[k] *= v / old;
        }

        public double[] Get(double[] t, double[] fc) => targets.Select(g => Get(g, t, fc)).ToArray();

        /// <summary>Sets every knob; false if a thickness would not be positive.</summary>
        public bool Set(double[] t, double[] fc, double[] x)
        {
            for (int j = 0; j < targets.Count; j++) Set(targets[j], t, fc, x[j]);
            return Valid(t, fc);
        }

        public bool Valid(double[] t, double[] fc) =>
            targets.All(g => (g.Radius || Get(g, t, fc) > 0) && (!g.Position || t[g.Knob - 1] > 0));
    }

    /// <summary>
    /// Solves each target by varying its knob: Newton's method on all of them at once, with steps cut back to keep every
    /// thickness knob positive.
    /// </summary>
    private static void Newton(Knobs k, double[] t, double[] fc)
    {
        int m = k.Count;
        var targets = k.Targets;

        // Newton converges in a handful of steps when there is a solution; a step that cannot reduce the residual means
        // there is none from here, and the search moves on rather than grinding.
        var history = new List<double>();
        for (int it = 0; it < 25; it++)
        {
            var f = k.Values(t, fc);
            if (f.All(v => Math.Abs(v) < 1e-12)) return;
            // Creeping towards a minimum of the residual that is not a root: no solution with these glasses.
            double norm = f.Sum(v => v * v);
            history.Add(norm);
            if (history.Count > 6 && norm > 0.25 * history[^4]) break;
            bool moved = false;
            var jac = new double[m, m];
            for (int j = 0; j < m; j++)
            {
                var g = targets[j];
                double v0 = k.Get(g, t, fc);
                double h = g.Radius ? 1e-6 * Math.Max(1e-3 * Scale(k.Curvatures(t, fc)), Math.Abs(v0)) : 1e-6 * Math.Max(1e-3, Math.Abs(v0));
                var tj = (double[])t.Clone();
                var fj = (double[])fc.Clone();
                k.Set(g, tj, fj, v0 + h);
                var vj = k.Values(tj, fj);
                for (int i = 0; i < m; i++) jac[i, j] = (vj[i] - f[i]) / h;
            }
            var step = Linear.Solve(jac, f) ?? throw new InvalidOperationException("the targets cannot be solved with these knobs (they do not act independently)");
            double lambda = 1;
            for (int cut = 0; cut < 16; cut++, lambda /= 2)
            {
                var tt = (double[])t.Clone();
                var ff = (double[])fc.Clone();
                if (!k.Set(tt, ff, k.Get(tt, ff).Select((v, j) => v - lambda * step[j]).ToArray())) continue;
                double[] ft;
                try { ft = k.Values(tt, ff); }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { continue; }
                if (ft.Sum(v => v * v) < norm)
                {
                    Array.Copy(tt, t, t.Length);
                    Array.Copy(ff, fc, fc.Length);
                    moved = true;
                    break;
                }
            }
            if (!moved) break;
        }
        var final = k.Values(t, fc);
        if (final.Any(v => Math.Abs(v) > 1e-9))
            throw new InvalidOperationException("the targets did not converge: " + string.Join(", ", targets.Zip(final, (g, v) => $"{g.What} {v:E2}")));
    }
}

/// <summary>Small dense linear algebra.</summary>
internal static class Linear
{
    /// <summary>Solves A x = b by Gaussian elimination with partial pivoting; null if A is singular.</summary>
    public static double[]? Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        double norm = 0;
        foreach (double v in a) norm = Math.Max(norm, Math.Abs(v));
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++) if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
            if (Math.Abs(m[piv, col]) <= 1e-14 * norm) return null;
            if (piv != col)
            {
                for (int k = 0; k < n; k++) (m[col, k], m[piv, k]) = (m[piv, k], m[col, k]);
                (x[col], x[piv]) = (x[piv], x[col]);
            }
            for (int r = col + 1; r < n; r++)
            {
                double f = m[r, col] / m[col, col];
                for (int k = col; k < n; k++) m[r, k] -= f * m[col, k];
                x[r] -= f * x[col];
            }
        }
        for (int r = n - 1; r >= 0; r--)
        {
            double s = x[r];
            for (int k = r + 1; k < n; k++) s -= m[r, k] * x[k];
            x[r] = s / m[r, r];
        }
        return x;
    }
}
