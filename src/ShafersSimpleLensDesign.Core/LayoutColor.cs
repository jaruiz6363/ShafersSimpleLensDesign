using System.Collections.Concurrent;
using System.Diagnostics;
using AberrationCalculator.Core.Glass;

namespace ShafersSimpleLensDesign.Core;

/// <summary>A layout given real glasses and color corrected.</summary>
public sealed class ColorCandidate
{
    public required LayoutCandidate Base { get; init; }
    public required ElementGlass[] Glazing { get; init; }
    public required Design Design { get; init; }

    /// <summary>The optimized variables: log element thicknesses, log air spaces, logit buried-surface fractions.</summary>
    public required double[] X { get; init; }

    /// <summary>Real-ray RMS spot radius at best focus over the color wavelengths, mm: over the field, and at 0, 0.7 and 1.</summary>
    public required double Rms { get; init; }
    public required double[] FieldRms { get; init; }
    public required double Distortion { get; init; }
    public required double Length { get; init; }

    /// <summary>Back focus, mm, the air path, any cover glass not counted; the full-field chief ray's angle at the image, degrees.</summary>
    public double BackFocus { get; init; }
    public double ChiefAngle { get; init; }

    /// <summary>Paraxial color, shortest wavelength less longest, mm: the foci, and the image heights at full field.</summary>
    public required double AxialColor { get; init; }
    public required double LateralColor { get; init; }
    public int Rank { get; set; }

    public Layout Layout => Base.Layout;
    public string Glasses => string.Join(" | ", Glazing.Select(g => g.ToString()));
    public string Order => Layouts.Order(Design);
}

public sealed class ColorResult
{
    public required List<ColorCandidate> Ranked { get; init; }

    /// <summary>The layouts colored; the glasses usable; the glass choices screened by paraxial color, and how many of
    /// them were optimized by real rays.</summary>
    public required int Layouts { get; init; }
    public required int Usable { get; init; }
    public required long Screened { get; init; }
    public required int Optimized { get; init; }
    public required TimeSpan Elapsed { get; init; }
}

/// <summary>
/// Color for the layouts, by Shafer's buried surfaces. Each model glass is replaced by a real glass of nearly its
/// index, and one element is split by a buried surface concentric about the chief ray between two glasses of nearly one
/// index (C~): it has Ā = 0, so it changes axial color only, by −H (y/ȳ) Δ(δn/n), and y/ȳ runs along the element, so
/// where it sits sets how much. A second element may be split by a surface centered on the marginal ray's axial point
/// (M~): A = 0, so it changes lateral color only, by H Δ(δn/n) wherever it sits. With the lens's geometry at the design
/// wavelength fixed, the paraxial color is linear in the glasses' dispersions (Seidel's C_L = Σ A y Δ(δn/n) and
/// C_T = Σ Ā y Δ(δn/n)), so every choice of glasses is screened in microseconds: the C~ surface's place is solved for no
/// axial color, and the choice judged by what is left, the lateral color and the secondary spectrum, at the image. The
/// best few are then optimized by real rays over the color wavelengths, the radii still from the rules.
/// </summary>
public static class LayoutColor
{
    public static ColorResult Run(LayoutSettings settings, IReadOnlyList<LayoutCandidate> bases, GlassCatalog catalog,
        IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        var clock = Stopwatch.StartNew();
        var lambdas = settings.ColorWavelengths;
        var usable = GlassSet.Usable(catalog, settings.Catalogs, settings.Wavelength, lambdas, includeObsolete: false)
            .Where(g => settings.GlassFilter?.Invoke(g.Data) ?? true).ToList();
        long screened = 0;
        int optimized = 0;
        var results = new ConcurrentBag<ColorCandidate>();
        var seeds = bases.ToList();
        int rounds = Math.Max(1, settings.ColorRounds);
        for (int round = 0; round < rounds && seeds.Count > 0; round++)
        {
            // Screen each seed's geometry, then optimize its best choices by real rays from the seed's thicknesses.
            var screens = seeds.Select(b => (Base: b, Choices: new List<Choice>())).ToList();
            Parallel.ForEach(screens, new ParallelOptions { CancellationToken = cancel }, s =>
            {
                s.Choices.AddRange(Screen(settings, s.Base, usable, out long n));
                Interlocked.Add(ref screened, n);
            });
            var jobs = screens.SelectMany(s => s.Choices.Select(c => (s.Base, Choice: c))).ToList();
            var found = new ConcurrentBag<ColorCandidate>();
            int done = 0;
            Parallel.ForEach(jobs, new ParallelOptions { CancellationToken = cancel }, job =>
            {
                if (Polish(settings, job.Base, job.Choice) is { } c) { results.Add(c); found.Add(c); }
                Interlocked.Increment(ref optimized);
                progress?.Report((round + (double)Interlocked.Increment(ref done) / jobs.Count) / rounds);
            });

            // The thicknesses moved while the glasses stayed, so the color they were chosen for has drifted: the M~
            // surface's lateral color is fixed by its glasses. The best lenses of each layout, as they now are, are
            // the next round's seeds, their glasses picked again.
            seeds = found.GroupBy(c => c.Layout.ToString())
                .SelectMany(g => g.OrderBy(c => c.Rms).Take(settings.RepickSeeds))
                .Select(c => Equivalent(settings, c)).OfType<LayoutCandidate>().ToList();
        }
        // The best glasses for each layout first, then the rest; a choice found twice kept once.
        var ranked = results.GroupBy(c => c.Layout + " " + c.Glasses).Select(g => g.MinBy(c => c.Rms)!).OrderBy(c => c.Rms).ToList();
        for (int i = 0; i < ranked.Count; i++) ranked[i].Rank = i + 1;
        return new ColorResult
        {
            Ranked = ranked, Layouts = bases.Count, Usable = usable.Count, Screened = screened, Optimized = optimized, Elapsed = clock.Elapsed,
        };
    }

    /// <summary>
    /// A colored lens as the screen sees a layout: each element one model glass at its glasses' index (the mean, for a
    /// split element) and the thicknesses and air spaces it was optimized to, so its glasses can be picked again for the
    /// geometry it now has. Null if the rules cannot build it.
    /// </summary>
    internal static LayoutCandidate? Equivalent(LayoutSettings settings, ColorCandidate c)
    {
        int k = c.Layout.Elements;
        double lD = settings.Wavelength;
        var indices = c.Glazing.Select(g => g.Split ? (g.Front.IndexAt(lD) + g.Back!.IndexAt(lD)) / 2 : g.Front.IndexAt(lD)).ToArray();
        var x = c.X.Take(2 * k).ToArray();
        var (spec, glass) = Layouts.Lens(settings, c.Layout, x, indices.Select(n => new ElementGlass(GlassSet.Model(n))).ToArray());
        Design d;
        try { d = Builder.Build(spec, glass); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return null; }
        return new LayoutCandidate
        {
            Layout = c.Layout, Design = d, Indices = indices, X = x, Rms = c.Rms, FieldRms = c.FieldRms, Distortion = c.Distortion,
            Length = c.Length,
        };
    }

    /// <summary>A choice of glasses for one layout, with where its split elements' buried surfaces start (fractions of the
    /// element's thickness), and its paraxial score: lateral color and secondary spectrum at the image, mm.</summary>
    internal sealed record Choice(ElementGlass[] Glazing, double[] Fractions, double Score, double Lateral = 0, double Secondary = 0, double Lateral2 = 0);

    /// <summary>One way to glaze one element, and its share of the paraxial color (F−C and F−d, axial and lateral, as
    /// Seidel sums). For a C~ split, Gain and Gain2 multiply y/ȳ at the buried surface.</summary>
    private sealed record Option(ElementGlass Glass, double Cl, double Ct, double Cl2, double Ct2, double Gain = 0, double Gain2 = 0);

    /// <summary>The paraxial screen of one layout: the best <see cref="LayoutSettings.ColorKeep"/> choices of glasses.</summary>
    internal static List<Choice> Screen(LayoutSettings settings, LayoutCandidate b, List<SearchGlass> usable, out long count,
        double fracLo = 0.15, double fracHi = 0.85, double steep = 0.85, double secondaryWeight = 1)
    {
        var d = b.Design;
        var layout = b.Layout;
        int k = layout.Elements;
        double lF = settings.ColorWavelengths.Min(), lC = settings.ColorWavelengths.Max(), lD = settings.Wavelength;
        double tanField = Math.Tan(settings.HalfField * Math.PI / 180);
        if (lC - lF < 1e-6) return Nearest(settings, b, usable, out count);

        // The rays at the design wavelength: marginal at the full aperture, chief at full field; slopes incident and after.
        var n = d.Indices(lD);
        var c = d.Curvature;
        int m = d.Count;
        var y = new double[m]; var u = new double[m]; var uo = new double[m];
        double yy = settings.Efl / (2 * settings.FNumber), uu = 0;
        for (int q = 0; q < m; q++)
        {
            y[q] = yy; u[q] = uu;
            uu = (n[q] * uu - yy * (n[q + 1] - n[q]) * c[q]) / n[q + 1];
            uo[q] = uu;
            if (q < m - 1) yy += d.Thickness[q] * uu;
        }
        var (ybRaw, ubRaw) = Builder.ChiefRay(n, d.StopSurface, d.Thickness, c);
        var yb = ybRaw.Select(v => v * tanField).ToArray();
        var ub = ubRaw.Select(v => v * tanField).ToArray();
        var ubo = new double[m];
        for (int q = 0; q < m; q++) ubo[q] = (n[q] * ub[q] - yb[q] * (n[q + 1] - n[q]) * c[q]) / n[q + 1];
        double image = uo[m - 1];                           // to the image: Seidel sums over n'u' are transverse aberrations

        // Each element's surfaces and coefficients.
        var options = new List<Option>[k];
        var cOptions = new List<Option>[k];
        var mOptions = new List<Option>[k];
        var geometry = new (double Y, double U, double Yb, double Ub, double T)[k];
        for (int i = 0; i < k; i++)
        {
            int f = 2 * i + (i >= layout.Stop ? 1 : 0), r = f + 1;
            double ni = n[f + 1];
            double aF = n[f] * (u[f] + y[f] * c[f]) * y[f] / ni, aR = ni * (u[r] + y[r] * c[r]) * y[r] / ni;
            double bF = n[f] * (ub[f] + yb[f] * c[f]) * y[f] / ni, bR = ni * (ub[r] + yb[r] * c[r]) * y[r] / ni;
            double h = ubo[f] * y[f] - uo[f] * yb[f];          // the Lagrange invariant over the element's index
            geometry[i] = (y[f], uo[f], yb[f], ubo[f], d.Thickness[f]);

            var near = usable.Where(g => Math.Abs(g.NDesign - b.Indices[i]) <= settings.GlassIndexTolerance).ToList();
            (double FC, double Fd) Disp(SearchGlass g) => (g.Data.IndexAt(lF) - g.Data.IndexAt(lC), g.Data.IndexAt(lF) - g.Data.IndexAt(lD));

            // Single glasses spread over the Abbe numbers.
            var byAbbe = near.OrderBy(g => g.Data.Vd).ToList();
            int take = Math.Min(settings.Singles, byAbbe.Count);
            options[i] = Enumerable.Range(0, take).Select(j => byAbbe[take == 1 ? byAbbe.Count / 2 : j * (byAbbe.Count - 1) / (take - 1)]).Distinct()
                .Select(g =>
                {
                    var (p, p2) = Disp(g);
                    return new Option(new ElementGlass(g.Data), p * (aF - aR), p * (bF - bR), p2 * (aF - aR), p2 * (bF - bR));
                }).ToList();

            // Pairs for a buried surface, the widest apart in Abbe number, either way round.
            var pairs = near.SelectMany(g1 => near.Where(g2 => Math.Abs(g2.NDesign - g1.NDesign) <= settings.PairIndexTolerance
                                                             && Math.Abs(g2.Data.Vd - g1.Data.Vd) >= settings.PairMinAbbeSplit).Select(g2 => (g1, g2)))
                .OrderByDescending(p => Math.Abs(p.g1.Data.Vd - p.g2.Data.Vd)).Take(settings.Pairs).ToList();
            cOptions[i] = new List<Option>();
            mOptions[i] = new List<Option>();
            foreach (var (g1, g2) in pairs)
            {
                var (p, p2) = Disp(g1);
                var (q, q2) = Disp(g2);
                double cl = p * aF - q * aR, ct = p * bF - q * bR, cl2 = p2 * aF - q2 * aR, ct2 = p2 * bF - q2 * bR;
                cOptions[i].Add(new Option(new ElementGlass(g1.Data, g2.Data, 'C'), cl, ct, cl2, ct2, -(q - p) * h, -(q2 - p2) * h));
                mOptions[i].Add(new Option(new ElementGlass(g1.Data, g2.Data, 'M'), cl, ct + (q - p) * h, cl2, ct2 + (q2 - p2) * h));
            }
        }

        // Every choice: one element split C~, at most one other split M~, the rest single glasses.
        var heap = new PriorityQueue<Choice, double>();
        long tried = 0;
        for (int ce = 0; ce < k; ce++)
        {
            if (cOptions[ce].Count == 0) continue;
            var g = geometry[ce];
            for (int me = -1; me < k; me++)
            {
                if (me == ce || (me >= 0 && mOptions[me].Count == 0)) continue;
                var lists = Enumerable.Range(0, k).Where(i => i != ce).Select(i => i == me ? mOptions[i] : options[i]).ToArray();
                if (lists.Any(l => l.Count == 0)) continue;
                var others = Enumerable.Range(0, k).Where(i => i != ce).ToArray();
                var pick = new int[lists.Length];
                while (true)
                {
                    double cl = 0, ct = 0, cl2 = 0, ct2 = 0;
                    for (int j = 0; j < lists.Length; j++)
                    {
                        var o = lists[j][pick[j]];
                        cl += o.Cl; ct += o.Ct; cl2 += o.Cl2; ct2 += o.Ct2;
                    }
                    foreach (var o in cOptions[ce])
                    {
                        tried++;
                        // y/ȳ at the C~ surface for no axial color, and where along the element that is.
                        double ratio = -(cl + o.Cl) / o.Gain;
                        double den = g.U - ratio * g.Ub;
                        if (!double.IsFinite(ratio) || Math.Abs(den) < 1e-300) continue;
                        double z = (ratio * g.Yb - g.Y) / den, frac = z / g.T;
                        if (!(frac >= fracLo && frac <= fracHi)) continue;
                        double yz = g.Y + g.U * z, ybz = g.Yb + g.Ub * z;
                        if (Math.Abs(g.Ub / ybz) * (Math.Abs(yz) + Math.Abs(ybz)) > steep) continue;   // a hemisphere at most
                        double lat = (ct + o.Ct) / image, sec = (cl2 + o.Cl2 + o.Gain2 * ratio) / image, lat2 = (ct2 + o.Ct2) / image;
                        double score = Math.Sqrt(lat * lat + secondaryWeight * (sec * sec + lat2 * lat2));
                        if (heap.Count < settings.ColorKeep || score < -PeekPriority(heap))
                        {
                            var glazing = new ElementGlass[k];
                            var fractions = new double[k];
                            for (int j = 0; j < lists.Length; j++) glazing[others[j]] = lists[j][pick[j]].Glass;
                            glazing[ce] = o.Glass;
                            fractions[ce] = frac;
                            if (me >= 0) fractions[me] = 0.5;
                            heap.Enqueue(new Choice(glazing, fractions, score, lat, sec, lat2), -score);
                            if (heap.Count > settings.ColorKeep) heap.Dequeue();
                        }
                    }
                    int a = 0;
                    while (a < pick.Length && ++pick[a] == lists[a].Count) pick[a++] = 0;
                    if (a == pick.Length) break;
                }
            }
        }
        count = tried;
        return heap.UnorderedItems.Select(e => e.Element).OrderBy(e => e.Score).ToList();
    }

    /// <summary>
    /// The screen at one wavelength, where there is no color to correct: each element a single glass, no buried surfaces.
    /// The <see cref="LayoutSettings.Singles"/> glasses nearest each element's model index are tried in every combination,
    /// and the best <see cref="LayoutSettings.ColorKeep"/> kept, judged by how far their indices are from the model's.
    /// </summary>
    private static List<Choice> Nearest(LayoutSettings settings, LayoutCandidate b, List<SearchGlass> usable, out long count)
    {
        int k = b.Layout.Elements;
        var lists = Enumerable.Range(0, k).Select(i => usable
            .Where(g => Math.Abs(g.NDesign - b.Indices[i]) <= settings.GlassIndexTolerance)
            .OrderBy(g => Math.Abs(g.NDesign - b.Indices[i])).Take(settings.Singles).ToList()).ToArray();
        count = 0;
        if (lists.Any(l => l.Count == 0)) return new List<Choice>();
        var heap = new PriorityQueue<Choice, double>();
        var pick = new int[k];
        while (true)
        {
            count++;
            double score = Math.Sqrt(Enumerable.Range(0, k).Sum(i => Math.Pow(lists[i][pick[i]].NDesign - b.Indices[i], 2)));
            if (heap.Count < settings.ColorKeep || score < -PeekPriority(heap))
            {
                heap.Enqueue(new Choice(Enumerable.Range(0, k).Select(i => new ElementGlass(lists[i][pick[i]].Data)).ToArray(), new double[k], score), -score);
                if (heap.Count > settings.ColorKeep) heap.Dequeue();
            }
            int a = 0;
            while (a < k && ++pick[a] == lists[a].Count) pick[a++] = 0;
            if (a == k) break;
        }
        return heap.UnorderedItems.Select(e => e.Element).OrderBy(e => e.Score).ToList();
    }

    private static double PeekPriority(PriorityQueue<Choice, double> heap) => heap.TryPeek(out _, out double p) ? p : double.NegativeInfinity;

    /// <summary>Writes colored lenses as CSV, in the order given; returns the full path.</summary>
    public static string WriteCsv(IEnumerable<ColorCandidate> ranked, string path)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var w = new StreamWriter(path);
        w.WriteLine("rank,layout,rms_um,rms_0field_um,rms_0.7field_um,rms_1field_um,axial_color_um,lateral_color_um,distortion_percent,"
                    + "length_mm,back_focus_mm,chief_angle_deg,glasses,order");
        foreach (var c in ranked)
            w.WriteLine(string.Join(",", c.Rank, c.Layout, (c.Rms * 1000).ToString("G5", inv),
                string.Join(",", c.FieldRms.Select(v => (v * 1000).ToString("G5", inv))), (c.AxialColor * 1000).ToString("G5", inv),
                (c.LateralColor * 1000).ToString("G5", inv), (c.Distortion * 100).ToString("G4", inv), c.Length.ToString("G6", inv),
                c.BackFocus.ToString("G6", inv), c.ChiefAngle.ToString("G4", inv), $"\"{c.Glasses}\"", $"\"{c.Order}\""));
        return Path.GetFullPath(path);
    }

    /// <summary>A choice of glasses optimized by real rays over the color wavelengths, from the layout's monochromatic
    /// thicknesses; null if it ends outside the limits.</summary>
    internal static ColorCandidate? Polish(LayoutSettings settings, LayoutCandidate b, Choice choice)
    {
        int k = b.Layout.Elements;
        var x = b.X.Take(2 * k).Concat(Enumerable.Range(0, k).Where(i => choice.Glazing[i].Split)
            .Select(i => Math.Log(choice.Fractions[i] / (1 - choice.Fractions[i])))).ToArray();
        var e = Layouts.Minimize(settings, b.Layout, x, choice.Glazing);
        if (e is null || e.Violation > 1e-4) return null;
        var d = e.Design;
        var (axial, lateral) = Aberrations.Color(d.Spec, d.Glass, d.StopSurface, d.Thickness, d.Curvature);
        return new ColorCandidate
        {
            Base = b, Glazing = choice.Glazing, Design = d, X = e.X, Rms = e.Merit.Rms, FieldRms = e.Merit.FieldRms, Distortion = e.Distortion,
            Length = e.Length, BackFocus = e.BackFocus, ChiefAngle = e.ChiefAngle, AxialColor = axial * settings.Efl,
            LateralColor = lateral * settings.Efl * Math.Tan(settings.HalfField * Math.PI / 180),
        };
    }
}
