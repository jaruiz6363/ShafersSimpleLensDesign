using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using AberrationCalculator.Core.Glass;

namespace ShafersSimpleLensDesign.Core;

/// <summary>A search over every layout of air-spaced singlets at one wavelength.</summary>
public sealed class LayoutSettings
{
    public double Efl { get; set; } = 150;
    public double FNumber { get; set; } = 5.6;
    public double HalfField { get; set; } = 8;
    public double Wavelength { get; set; } = LensSpec.LambdaD;
    public int MinElements { get; set; } = 1;
    public int MaxElements { get; set; } = 5;

    /// <summary>The model glasses' index range.</summary>
    public double IndexMin { get; set; } = 1.45;
    public double IndexMax { get; set; } = 1.80;

    /// <summary>Random starting points per layout; the best result is kept.</summary>
    public int Starts { get; set; } = 4;
    public int Iterations { get; set; } = 40;

    /// <summary>Largest distortion at full field, a fraction.</summary>
    public double MaxDistortion { get; set; } = 0.01;

    /// <summary>Largest length, first vertex to image, in focal lengths.</summary>
    public double MaxLength { get; set; } = 2.0;

    /// <summary>Least center thickness and edge thickness of an element, and edge clearance between elements, in focal lengths.</summary>
    public double MinCenter { get; set; } = 0.01;
    public double MinEdge { get; set; } = 0.005;
    public double MinAirEdge { get; set; } = 0.005;

    /// <summary>Least air space between elements on the axis, in focal lengths: elements may not touch.</summary>
    public double MinAirCenter { get; set; } = 0.005;

    /// <summary>Least back focal distance, in focal lengths: the air path from the last lens vertex to the image, the cover
    /// glass's thickness not counted.</summary>
    public double MinBackFocus { get; set; } = 0.1;

    /// <summary>A cover glass (a sensor window or filter) between the lens and the image: a plane slab of
    /// <see cref="SlabGlass"/>, <see cref="SlabThickness"/> mm thick, its back face <see cref="SlabGap"/> mm in front of the
    /// image. None when the glass is null or the thickness 0.</summary>
    public GlassData? SlabGlass { get; set; }
    public double SlabThickness { get; set; }
    public double SlabGap { get; set; } = 0.5;

    /// <summary>Largest angle, degrees, between the real chief ray at full field and the axis, at the image (image-side
    /// telecentricity), whichever way it goes: −3 is the same limit as 3. 0 for no limit.</summary>
    public double MaxChiefAngle { get; set; }

    public int Top { get; set; } = 50;

    /// <summary>The color stage: how many of the best layouts are given real glasses (0: none), at which wavelengths,
    /// from which catalogs.</summary>
    public int ColorLayouts { get; set; }
    public double[] ColorWavelengths { get; set; } = { LensSpec.LambdaF, LensSpec.LambdaD, LensSpec.LambdaC };

    /// <summary>The weight of each color wavelength in the spot merit, in their order; null for equal.</summary>
    public double[]? ColorWeights { get; set; }
    public string[] Catalogs { get; set; } = GlassSet.DefaultCatalogs;

    /// <summary>The glasses the color stage may use, of those in <see cref="Catalogs"/>; null for all of them.</summary>
    public Func<GlassData, bool>? GlassFilter { get; set; }

    /// <summary>A real glass may replace a model glass within this of its index; the two glasses either side of a buried
    /// surface are within <see cref="PairIndexTolerance"/> of each other and <see cref="PairMinAbbeSplit"/> apart in Abbe number.</summary>
    public double GlassIndexTolerance { get; set; } = 0.03;
    public double PairIndexTolerance { get; set; } = 0.01;
    public double PairMinAbbeSplit { get; set; } = 10;

    /// <summary>Glasses tried per element (spread over the Abbe numbers), glass pairs tried per buried surface (the widest
    /// apart in Abbe number), and the glass choices per layout, best by paraxial color, that are optimized by real rays.</summary>
    public int Singles { get; set; } = 8;
    public int Pairs { get; set; } = 40;
    public int ColorKeep { get; set; } = 16;

    /// <summary>Rounds of choosing glasses: after the first, the best <see cref="RepickSeeds"/> lenses of each layout have
    /// their glasses picked again for the thicknesses they were optimized to, and are optimized again.</summary>
    public int ColorRounds { get; set; } = 1;
    public int RepickSeeds { get; set; } = 3;
}

/// <summary>An element's glass: one glass, or two cemented at a buried surface that is concentric about the chief ray
/// ('C', for axial color) or centered on the marginal ray's axial point ('M', for lateral color).</summary>
public sealed record ElementGlass(GlassData Front, GlassData? Back = null, char Buried = ' ')
{
    public bool Split => Back is not null;

    public override string ToString() => Split ? $"{Layouts.Id(Front)} {Buried}~ {Layouts.Id(Back!)}" : Layouts.Id(Front);
}

/// <summary>A layout: the kind of each element (its two surfaces, C or A) and the air space holding the stop, 0 before
/// the first element to <c>Kinds.Length</c> after the last.</summary>
public sealed record Layout(string[] Kinds, int Stop)
{
    public int Elements => Kinds.Length;

    /// <summary>Written as the elements' kinds with S at the stop: "S CA CC AC".</summary>
    public override string ToString() =>
        string.Join(" ", Enumerable.Range(0, Elements + 1).SelectMany(i => (i == Stop ? new[] { "S" } : Array.Empty<string>())
            .Concat(i < Elements ? new[] { Kinds[i] } : Array.Empty<string>())));
}

/// <summary>One optimized layout.</summary>
public sealed class LayoutCandidate
{
    public required Layout Layout { get; init; }
    public required Design Design { get; init; }
    public required double[] Indices { get; init; }

    /// <summary>The optimized variables: log element thicknesses, log air spaces, then logit indices.</summary>
    public required double[] X { get; init; }

    /// <summary>Real-ray RMS spot radius at best focus, mm: over the field, and at 0, 0.7 and 1.</summary>
    public required double Rms { get; init; }
    public required double[] FieldRms { get; init; }

    /// <summary>Distortion at full field, a fraction; length, first vertex to image, mm.</summary>
    public required double Distortion { get; init; }
    public required double Length { get; init; }

    /// <summary>Back focus, mm: the air path from the last lens vertex to the image, any cover glass not counted; and the
    /// angle, degrees, of the full-field chief ray at the image.</summary>
    public double BackFocus { get; init; }
    public double ChiefAngle { get; init; }
    public int Rank { get; set; }

    /// <summary>The surface order, for the build command.</summary>
    public string Order => Layouts.Order(Design);
}

public sealed class LayoutResult
{
    public required LayoutSettings Settings { get; init; }
    public required List<LayoutCandidate> Ranked { get; init; }

    /// <summary>The best layout with each number of elements.</summary>
    public required SortedDictionary<int, LayoutCandidate> BestByElements { get; init; }
    public required int Layouts { get; init; }
    public required int Feasible { get; init; }
    public required TimeSpan Elapsed { get; init; }
}

/// <summary>
/// Shafer's method at one wavelength, over every layout. A layout of N air-spaced singlets is the kind of each
/// element, CC, CA, AC or AA (each surface concentric about the chief ray or aplanatic), and the air space holding the
/// stop, the first element not AA (a plane plate in the collimated beam): 3 · 4^(N−1) (N + 1) layouts. Every surface's
/// radius follows from its rule, so each lens is free of coma and astigmatism by construction; what is left, spherical
/// aberration, Petzval curvature and distortion, depends only on the thicknesses, air spaces and indices. These are optimized, the glasses as model glasses with no dispersion, by
/// Levenberg-Marquardt on the real-ray spot at best focus (<see cref="SpotMerit"/>), with distortion, length and
/// thicknesses held within limits; the radii never vary, so every lens stays Shafer's.
/// </summary>
public static class Layouts
{
    public static readonly string[] ElementKinds = { "CC", "CA", "AC", "AA" };

    public static IEnumerable<Layout> All(int minElements, int maxElements)
    {
        for (int k = minElements; k <= maxElements; k++)
            for (int code = 0; code < 1 << (2 * k); code++)
            {
                var kinds = Enumerable.Range(0, k).Select(i => ElementKinds[(code >> (2 * i)) & 3]).ToArray();
                // An aplanatic surface in the collimated beam is flat, and so is the next if it is aplanatic too: a first element
                // AA is a plane plate, which only adds length.
                if (kinds[0] == "AA") continue;
                for (int stop = 0; stop <= k; stop++) yield return new Layout(kinds, stop);
            }
    }

    public static LayoutResult Run(LayoutSettings settings, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        var clock = Stopwatch.StartNew();
        var layouts = All(settings.MinElements, settings.MaxElements).ToList();
        var results = new ConcurrentBag<LayoutCandidate>();
        int done = 0;
        Parallel.ForEach(layouts, new ParallelOptions { CancellationToken = cancel }, layout =>
        {
            if (Optimize(settings, layout) is { } c) results.Add(c);
            progress?.Report((double)Interlocked.Increment(ref done) / layouts.Count);
        });
        var ranked = results.OrderBy(c => c.Rms).ToList();
        var best = new SortedDictionary<int, LayoutCandidate>();
        foreach (var c in ranked) best.TryAdd(c.Layout.Elements, c);
        ranked = ranked.Take(settings.Top).ToList();
        for (int i = 0; i < ranked.Count; i++) ranked[i].Rank = i + 1;
        return new LayoutResult
        {
            Settings = settings, Ranked = ranked, BestByElements = best, Layouts = layouts.Count, Feasible = results.Count,
            Elapsed = clock.Elapsed,
        };
    }

    /// <summary>The best of <see cref="LayoutSettings.Starts"/> optimizations of one layout, or null if none ends within
    /// the limits.</summary>
    public static LayoutCandidate? Optimize(LayoutSettings settings, Layout layout)
    {
        int k = layout.Elements;
        var rng = new Random(layout.ToString().Aggregate(17, (h, ch) => h * 31 + ch));
        LayoutCandidate? best = null;
        for (int start = 0; start < settings.Starts; start++)
        {
            // A start the rules can build: element thicknesses, air spaces (in units of the entrance pupil's radius)
            // and indices at random.
            double[]? x = null;
            for (int attempt = 0; attempt < 20 && x is null; attempt++)
            {
                var trial = new double[3 * k];
                for (int i = 0; i < k; i++)
                {
                    trial[i] = Math.Log(0.1 + 0.9 * rng.NextDouble());
                    trial[k + i] = Math.Log(0.05 * Math.Pow(60, rng.NextDouble()));
                    trial[2 * k + i] = Logit((0.1 + 0.8 * rng.NextDouble()));
                }
                if (Evaluate(settings, layout, trial) is not null) x = trial;
            }
            if (x is null) continue;
            var e = Minimize(settings, layout, x);
            if (e is null || e.Violation > 1e-4) continue;
            if (best is null || e.Merit.Rms < best.Rms)
                best = new LayoutCandidate
                {
                    Layout = layout, Design = e.Design, Indices = e.Indices, X = e.X, Rms = e.Merit.Rms, FieldRms = e.Merit.FieldRms,
                    Distortion = e.Distortion, Length = e.Length, BackFocus = e.BackFocus, ChiefAngle = e.ChiefAngle,
                };
        }
        return best;
    }

    internal sealed record Evaluation(double[] X, Design Design, double[] Indices, double[] Residuals, SpotMerit.Value Merit, double Distortion,
        double Length, double Violation, double BackFocus = 0, double ChiefAngle = 0)
    {
        public double Norm => Residuals.Sum(v => v * v);
    }

    /// <summary>
    /// Levenberg-Marquardt from <paramref name="x"/>: differences for the Jacobian, Marquardt's scaling. With
    /// <paramref name="glazing"/>, the lens has those glasses (see <see cref="Lens"/>). The limits are penalties the size
    /// of the spot's errors, which least squares trades against the spot, ending a few hundredths of a millimeter outside
    /// them; while it does, the penalties are made ten times heavier and the minimization continued, up to four times.
    /// </summary>
    internal static Evaluation? Minimize(LayoutSettings settings, Layout layout, double[] x, ElementGlass[]? glazing = null)
    {
        Evaluation? e = null;
        for (double weight = 1; weight <= 1e4; weight *= 10)
        {
            e = Minimize(settings, layout, e?.X ?? x, glazing, weight);
            if (e is null || e.Violation <= 1e-4) break;
        }
        return e;
    }

    private static Evaluation? Minimize(LayoutSettings settings, Layout layout, double[] x, ElementGlass[]? glazing, double weight)
    {
        var e = Evaluate(settings, layout, x, glazing, weight: weight);
        if (e is null) return null;
        int p = x.Length;
        double mu = 1e-3;
        int slow = 0;
        for (int it = 0; it < settings.Iterations && slow < 3; it++)
        {
            int m = e.Residuals.Length;
            var jac = new double[m, p];
            for (int j = 0; j < p; j++)
            {
                const double h = 1e-5;
                var xj = (double[])x.Clone();
                xj[j] += h;
                var ej = Evaluate(settings, layout, xj, glazing, weight: weight);
                if (ej is null || ej.Residuals.Length != m) continue;
                for (int i = 0; i < m; i++) jac[i, j] = (ej.Residuals[i] - e.Residuals[i]) / h;
            }
            var a = new double[p, p];
            var g = new double[p];
            for (int r = 0; r < p; r++)
            {
                for (int c = r; c < p; c++)
                {
                    double s = 0;
                    for (int i = 0; i < m; i++) s += jac[i, r] * jac[i, c];
                    a[r, c] = a[c, r] = s;
                }
                double t = 0;
                for (int i = 0; i < m; i++) t += jac[i, r] * e.Residuals[i];
                g[r] = -t;
            }
            // A variable that has stopped mattering (an index logit saturated at its bound) still needs damping, or its step,
            // a ratio of two tiny numbers, flings it across its range and no step is ever accepted.
            double floor = 1e-6 * Enumerable.Range(0, p).Max(r => a[r, r]) + 1e-300;
            bool moved = false;
            while (mu < 1e10)
            {
                var b = (double[,])a.Clone();
                for (int r = 0; r < p; r++) b[r, r] += mu * (a[r, r] + floor);
                var step = Linear.Solve(b, g);
                if (step is null) { mu *= 4; continue; }
                var xt = x.Select((v, j) => Math.Clamp(v + step[j], -12, 12)).ToArray();
                var et = Evaluate(settings, layout, xt, glazing, weight: weight);
                if (et is not null && et.Norm < e.Norm)
                {
                    slow = et.Norm > (1 - 1e-4) * e.Norm ? slow + 1 : 0;
                    x = xt;
                    e = et;
                    mu = Math.Max(mu / 3, 1e-9);
                    moved = true;
                    break;
                }
                mu *= 4;
            }
            if (!moved) break;
        }
        return e;
    }

    private static double Logit(double f) => Math.Log(f / (1 - f));
    private static double Index(LayoutSettings s, double v) => s.IndexMin + (s.IndexMax - s.IndexMin) / (1 + Math.Exp(-v));

    /// <summary>
    /// The lens of the variables <paramref name="x"/>, built by the rules and scaled to the focal length: log element
    /// thicknesses, log air spaces, then either logit model indices (at the design wavelength only) or, with
    /// <paramref name="glazing"/>, the logit of where each split element's buried surface sits, as a fraction of the
    /// element's thickness (at the color wavelengths).
    /// </summary>
    public static (LensSpec Spec, GlassData?[] Glass) Lens(LayoutSettings settings, Layout layout, double[] x, ElementGlass[]? glazing = null)
    {
        int k = layout.Elements, air = 0, split = 0;
        var surfaces = new List<SurfaceSpec>();
        var glass = new List<GlassData?>();
        SurfaceSpec Surface(char kind, GlassData? medium, double t, bool partner = false) => new()
        {
            Role = kind switch { 'C' => Role.Concentric, 'M' => Role.Marginal, _ => Role.Aplanatic }, Partner = partner,
            Glass = medium is null ? "" : Id(medium), Thickness = t,
        };
        for (int i = 0; i <= k; i++)
        {
            if (i == layout.Stop)
            {
                surfaces.Add(new SurfaceSpec { Role = Role.Stop, Glass = "", Thickness = air < k ? Math.Exp(x[k + air++]) : 0 });
                glass.Add(null);
            }
            if (i == k) break;
            var e = glazing?[i] ?? new ElementGlass(GlassSet.Model(Index(settings, x[2 * k + i])));
            double t = Math.Exp(x[i]);
            if (e.Split)
            {
                double f = 1 / (1 + Math.Exp(-x[2 * k + split++]));
                surfaces.Add(Surface(layout.Kinds[i][0], e.Front, t * f));
                glass.Add(e.Front);
                surfaces.Add(Surface(e.Buried, e.Back, t * (1 - f), partner: true));
                glass.Add(e.Back);
            }
            else
            {
                surfaces.Add(Surface(layout.Kinds[i][0], e.Front, t));
                glass.Add(e.Front);
            }
            surfaces.Add(Surface(layout.Kinds[i][1], null, air < k ? Math.Exp(x[k + air++]) : 0));
            glass.Add(null);
        }
        var spec = new LensSpec
        {
            Efl = settings.Efl, FNumber = settings.FNumber, HalfField = settings.HalfField,
            Wavelengths = glazing is null ? new[] { settings.Wavelength } : settings.ColorWavelengths,
            WavelengthWeights = glazing is null ? null : settings.ColorWeights, DesignWavelength = settings.Wavelength,
            Surfaces = surfaces,
        };
        return (spec, glass.ToArray());
    }

    /// <summary>A glass as the surface order names it: CATALOG:NAME, or MODEL:index.</summary>
    public static string Id(GlassData g) =>
        g.Catalog == GlassSet.ModelCatalog ? $"{GlassSet.ModelCatalog}:{g.Nd.ToString("F4", CultureInfo.InvariantCulture)}" : $"{g.Catalog}:{g.Name}";

    /// <summary>Builds and judges the lens of <paramref name="x"/>: the spot's residuals, and the limits' as further
    /// residuals (each the shortfall, mm, times <paramref name="weight"/>, so a lens within every limit is judged by its
    /// spot alone). Null if the rules cannot build it, or a ray or an edge fails; <paramref name="why"/>, if given, gets
    /// the reason, or each limit not met.</summary>
    internal static Evaluation? Evaluate(LayoutSettings settings, Layout layout, double[] x, ElementGlass[]? glazing = null, List<string>? why = null, double weight = 1)
    {
        var (spec, glass) = Lens(settings, layout, x, glazing);
        Design d;
        try { d = Builder.Build(spec, glass); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { why?.Add("not built: " + ex.Message); return null; }
        int lens = d.Count;
        double backFocus = d.Thickness[^1];
        d = WithSlab(settings, d);
        if (SpotMerit.Residuals(d, out var merit) is not { } spot) { why?.Add("a ray fails"); return null; }

        double f = settings.Efl, tanField = Math.Tan(settings.HalfField * Math.PI / 180);
        var n = d.Indices(settings.Wavelength);
        var (yb, _) = Builder.ChiefRay(n, d.StopSurface, d.Thickness, d.Curvature);
        var ym = Builder.MarginalHeights(n, d.Curvature, d.Thickness);
        // Each surface's semi-diameter as the lens is drawn: by real rays, with 4% to spare. If one of those rays fails, the
        // marginal ray's height plus the chief ray's, paraxially.
        // Every face of an element is cut to the widest of them, as it is drawn.
        var h = LensDrawing.Rims(d, LensDrawing.SemiDiameters(d, settings.HalfField)
                ?? Enumerable.Range(0, d.Count).Select(q => Math.Abs(ym[q]) * f / (2 * settings.FNumber) + Math.Abs(yb[q]) * tanField).ToArray());
        double? Sag(int q, double r)
        {
            double c = d.Curvature[q], s = 1 - c * c * r * r;
            return s <= 0 ? null : c * r * r / (1 + Math.Sqrt(s));
        }

        var limits = new List<double>();
        void Limit(double shortfall, string name)
        {
            limits.Add(shortfall);
            if (shortfall > 1e-4) why?.Add($"{name} short by {shortfall:G3} mm");
        }
        for (int q = 0; q < lens - 1; q++)
        {
            if (Sag(q, h[q]) is null || Sag(q + 1, h[q + 1]) is null) { why?.Add($"surface {q + 1} or {q + 2} steeper than a hemisphere over its aperture"); return null; }
            double Gap(double y) => d.Thickness[q] - Sag(q, y)!.Value + Sag(q + 1, y)!.Value;
            if (glass[q] is not null)
            {
                // Both faces are cut to the element's rim.
                Limit(Math.Max(0, settings.MinCenter * f - d.Thickness[q]), $"glass center {q + 1}");
                Limit(Math.Max(0, settings.MinEdge * f - Gap(h[q])), $"glass edge {q + 1}");
            }
            else
            {
                // Two elements of different widths face each other over the narrower's height, and the gap need not be least
                // at its rim: a steep meniscus can reach the next element part way out.
                double reach = Math.Min(h[q], h[q + 1]);
                double edge = Enumerable.Range(1, 8).Min(i => Gap(reach * i / 8));
                Limit(Math.Max(0, settings.MinAirEdge * f - edge), $"air edge {q + 1}");
                Limit(Math.Max(0, settings.MinAirCenter * f - d.Thickness[q]), $"air center {q + 1}");
            }
        }
        // Distortion: the chief ray at full field, from the center of the paraxial entrance pupil, against f tan(field).
        var chief = SkewTrace.Trace(n, d.Thickness, d.Curvature, settings.HalfField, 0, 0, -yb[0], 0);
        if (!chief.Ok) { why?.Add("the full-field chief ray fails"); return null; }
        double distortion = chief.Y / (f * tanField) - 1;
        Limit(Math.Max(0, Math.Abs(distortion) - settings.MaxDistortion) * f * tanField, "distortion");
        // Telecentricity: the angle the chief ray makes with the axis at the image (a plane slab leaves it as it is), positive
        // if it is going away from the axis (the exit pupil ahead of the image), negative if toward it.
        double angle = Math.Atan2(Math.Sqrt(chief.L * chief.L + chief.M * chief.M), Math.Abs(chief.N)) * 180 / Math.PI
                       * (chief.M * chief.Y < 0 ? -1 : 1);
        if (settings.MaxChiefAngle != 0)
        {
            // Either way: the limit is on its size. In mm, as every limit: the angle in excess, as the height it gains over
            // the least back focus.
            Limit(Math.Max(0, Math.Tan(Math.Abs(angle) * Math.PI / 180) - Math.Tan(Math.Abs(settings.MaxChiefAngle) * Math.PI / 180)) * settings.MinBackFocus * f,
                "chief ray angle at the image");
        }
        // Back focus, the air path: a slab moves the image back by t(1 − 1/n), and its own thickness t does not count.
        if (lens < d.Count)
        {
            double t = d.Thickness[lens], nSlab = d.Glass[lens]!.IndexAt(settings.Wavelength);
            backFocus -= t / nSlab;
            Limit(Math.Max(0, settings.MinAirCenter * f - d.Thickness[lens - 1]), "cover glass clearance");
        }
        Limit(Math.Max(0, settings.MinBackFocus * f - backFocus), "back focus");
        double length = d.Thickness.Sum();
        Limit(Math.Max(0, length - settings.MaxLength * f), "length");

        var indices = glazing?.Select(e => e.Front.Nd).ToArray()
                      ?? Enumerable.Range(0, layout.Elements).Select(i => Index(settings, x[2 * layout.Elements + i])).ToArray();
        return new Evaluation(x, d, indices, spot.Concat(limits.Select(v => weight * v)).ToArray(), merit, distortion, length, limits.Sum(), backFocus, angle);
    }

    /// <summary>
    /// The design with the cover glass of <paramref name="settings"/> behind it, if there is one: two plane surfaces, the
    /// slab's back face <see cref="LayoutSettings.SlabGap"/> in front of the image. A plane slab moves the paraxial image
    /// back by t(1 − 1/n) and changes nothing else at first order, so the space before it is the back focus without it
    /// less t/n and the gap.
    /// </summary>
    public static Design WithSlab(LayoutSettings settings, Design d)
    {
        if (settings.SlabGlass is not { } g || settings.SlabThickness <= 0) return d;
        double t = settings.SlabThickness, n = g.IndexAt(d.Spec.DesignWavelength);
        double before = d.Thickness[^1] - t / n - settings.SlabGap;
        var spec = d.Spec.Copy();
        spec.Surfaces = d.Spec.Surfaces.Select((s, q) => q < d.Count - 1 ? s : new SurfaceSpec
            {
                Role = s.Role, IsStop = s.IsStop, Partner = s.Partner, Glass = s.Glass, Thickness = before, Radius = s.Radius,
            })
            .Append(new SurfaceSpec { Role = Role.Fixed, Glass = Id(g), Thickness = t })
            .Append(new SurfaceSpec { Role = Role.Fixed, Glass = "", Thickness = settings.SlabGap }).ToList();
        return new Design
        {
            Spec = spec, Curvature = d.Curvature.Concat(new[] { 0.0, 0.0 }).ToArray(),
            Thickness = d.Thickness[..^1].Concat(new[] { before, t, settings.SlabGap }).ToArray(),
            Glass = d.Glass.Concat(new[] { g, null }).ToArray(), StopSurface = d.StopSurface,
            SolvedThickness = d.SolvedThickness.Concat(new[] { t, settings.SlabGap }).ToArray(),
        };
    }

    /// <summary>A design's surface order, for the build command: roles, glasses and the thicknesses as built.</summary>
    public static string Order(Design d) =>
        string.Join(", ", Enumerable.Range(0, d.Count).Select(q =>
        {
            string role = LensModel.Code(d.Spec.Surfaces[q]).Replace("@", "");
            if (d.Spec.Surfaces[q].Role == Role.Fixed)
                role = "F=" + (d.Curvature[q] == 0 ? "0" : d.Radius(q).ToString("G9", CultureInfo.InvariantCulture));
            string medium = d.Glass[q] is { } g ? Id(g) : "air";
            return q == d.Count - 1 ? $"{role} {medium}" : $"{role} {medium} {d.Thickness[q].ToString("G6", CultureInfo.InvariantCulture)}";
        }));

    /// <summary>Writes the ranked list as CSV; returns the path.</summary>
    public static string WriteCsv(LayoutResult r, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var w = new StreamWriter(path);
        w.WriteLine("rank,elements,layout,rms_um,rms_0field_um,rms_0.7field_um,rms_1field_um,distortion_percent,length_mm,indices,order");
        foreach (var c in r.Ranked)
            w.WriteLine(string.Join(",", c.Rank, c.Layout.Elements, c.Layout, (c.Rms * 1000).ToString("G6", CultureInfo.InvariantCulture),
                string.Join(",", c.FieldRms.Select(v => (v * 1000).ToString("G6", CultureInfo.InvariantCulture))),
                (c.Distortion * 100).ToString("G4", CultureInfo.InvariantCulture), c.Length.ToString("G6", CultureInfo.InvariantCulture),
                string.Join(" ", c.Indices.Select(v => v.ToString("F4", CultureInfo.InvariantCulture))), $"\"{c.Order}\""));
        return path;
    }
}
