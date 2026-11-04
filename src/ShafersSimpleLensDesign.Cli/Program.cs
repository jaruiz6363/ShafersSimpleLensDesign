using System.Globalization;
using AberrationCalculator.Core.Glass;
using ShafersSimpleLensDesign.Core;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
Console.OutputEncoding = System.Text.Encoding.UTF8;
// Without this, Windows 11 throttles a search running in a background terminal.
WindowsPerformance.DisablePowerThrottling();

const string Usage = """
shafer-lens: the command line of Shafer's Simple Lens Design (after D. Shafer, Proc. SPIE 237, 1980).

  shafer-lens layouts [options]                        every layout of air-spaced singlets, then real glasses and color
  shafer-lens build  --order "<surfaces>" [options]    build one lens from its surface order (as the app prints it)

The surface order: surfaces separated by commas, each "ROLE GLASS THICKNESS"
  ROLE       C  concentric about the chief ray      A  aplanatic for the marginal ray
             M  centered on the marginal ray's axial point
             S  a stop plane                         F=radius  a fixed radius
             B  a buried surface (two glasses of one index, different dispersions), radius solved
             a trailing @ puts the stop on that surface
  GLASS      the medium after the surface: CATALOG:NAME, MODEL:index (no dispersion), air, = (the glass before again)
             or =j (the glass after surface j again)
  THICKNESS  to the next surface, in any unit (the lens is scaled to --efl); omit on the last
Example, Shafer's Figure 1:   --order "C SCHOTT:N-SK16 1, A@ air"

Options:
  --efl 100          focal length
  --fno 8            f-number
  --hfov 3           half field of view, degrees
  --solve "petzval:2, lsa:1"   solve each aberration by varying the thickness after the surface named:
                     petzval, spherical (third order), lsa (real marginal ray at the paraxial focus), coma, astigmatism, distortion,
                     axial, lateral (paraxial color); rN varies the radius of surface N (a buried surface, B)
  --lambda 0.4861,0.5876,0.6563      wavelengths, um (the middle one is the design wavelength)
  --sa-zone 1        the pupil zone whose real ray lsa brings to the paraxial focus (0.85 balances better)
  --continuation     if the targets cannot be solved directly, bring them to zero in steps (slower to fail)
  --export <folder>  write the lens as .zmx, .seq, .len, .otx, .json and .lhlt
  --catalogs <folder>

Layouts options (model glasses, at the middle --lambda):
  --elements 5       most air-spaced singlets (--min-elements 1)   --starts 4   random starts per layout
  --index 1.45,1.80  the model glasses' index range   --max-distortion 1 (%)   --max-length 2 (focal lengths)
  --min-center 0.01  --min-edge 0.005  --min-bfl 0.1  --min-air 0.005  --min-air-edge 0.005 (focal lengths)
  --cover SCHOTT:N-BK7,1.1,0.5   a cover glass: glass, thickness mm, and gap mm from its back face to the image;
                     the back focus (--min-bfl) is the air path, the cover glass not counted
  --max-cra 5        largest angle, degrees, of the full-field chief ray at the image, either way (image-side telecentricity)
  --color 50         then give the best N layouts (and the best of each size) real glasses and correct their color:
                     one element split by a C~ buried surface (axial color), at most one by an M~ (lateral color)
  --color-lambda 0.4861,0.5876,0.6563   --vendors SCHOTT,OHARA,HOYA,SUMITA,CDGM   --glass-tol 0.03 (index of the model glass)
  --buried-tol 0.01  --abbe-split 10  (the glasses either side of a buried surface)   --color-keep 16 (choices optimized per layout)
  --color-rounds 1   rounds of picking glasses: after the first, the best lenses have theirs picked again and are re-optimized
  --common           only common glasses: those their maker marks preferred, or melts most often (melt frequency 1 or 2)
  --top 50           layouts listed and written to the CSV
  --out results      folder for the ranked CSVs and exports   --export 5   write the best N (and the best of each size)

""";

try
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help") { Console.WriteLine(Usage); return 0; }
    var opt = Options.Parse(args.Skip(1));
    return args[0] switch
    {
        "build" => Build(opt),
        "layouts" => LayoutsCommand(opt),
        _ => throw new ArgumentException($"unknown command '{args[0]}'; see --help"),
    };
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine("error: " + ex.Message);
    return 2;
}

static int Build(Options opt)
{
    var catalog = LoadCatalog(opt.Get("catalogs", ""));
    var lambdas = opt.GetList("lambda", new[] { LensSpec.LambdaF, LensSpec.LambdaD, LensSpec.LambdaC });
    var spec = new LensSpec
    {
        Efl = opt.GetD("efl", 100), FNumber = opt.GetD("fno", 8), HalfField = opt.GetD("hfov", 3),
        Wavelengths = lambdas, DesignWavelength = lambdas[lambdas.Length / 2], SphericalZone = opt.GetD("sa-zone", 1.0), Continuation = opt.Has("continuation"),
        Surfaces = Parser.Surfaces(opt.Get("order", "") is { Length: > 0 } o ? o : throw new ArgumentException("give --order")),
        Targets = Parser.Targets(opt.Get("solve", "")),
    };
    var d = Builder.Build(spec, catalog);
    var r = Analysis.Run(d, catalog);

    Console.WriteLine($"Surface order {string.Join(" ", spec.Surfaces.Select(LensModel.Code))}: f {spec.Efl}, f/{spec.FNumber}, half field {spec.HalfField} deg");
    if (spec.Targets.Count > 0)
        Console.WriteLine("Solved: " + string.Join(", ", spec.Targets.Select(t => t.Radius
            ? $"{t.What} by the radius of surface {t.Knob} = {d.Radius(t.Knob - 1):G6}"
            : $"{t.What} by the thickness after surface {t.Knob} = {d.Thickness[t.Knob - 1]:G6}")));
    Console.WriteLine();
    Console.WriteLine("  surf  role        radius       thickness    glass");
    for (int k = 0; k < d.Count; k++)
    {
        var s = spec.Surfaces[k];
        string rad = d.Curvature[k] == 0 ? "inf" : d.Radius(k).ToString("F4");
        Console.WriteLine($"  {k + 1,4}  {s.Role + (k == d.StopSurface ? " (stop)" : ""),-18} {rad,10}  {d.Thickness[k],12:F4}    {(d.Glass[k] is { } g ? $"{g.Catalog}:{g.Name}" : "air")}");
    }
    Console.WriteLine($"  Petzval sum {r.PetzvalSum:E3} /mm (Petzval radius {(Math.Abs(r.PetzvalSum) < 1e-12 ? "inf" : (-1 / r.PetzvalSum).ToString("F1"))} mm)");

    Console.WriteLine();
    Console.WriteLine("Seidel sums by surface (AberrationCalculator): each concentric surface should have no S2, S3, S5; each aplanatic no S1, S2, S3");
    Console.WriteLine("  surf  role        S1 spherical  S2 coma       S3 astig      S4 Petzval    S5 distort    CL axial     CT lateral");
    var sd = r.Seidel;
    for (int k = 0; k < d.Count; k++)
        Console.WriteLine($"  {k + 1,4}  {spec.Surfaces[k].Role,-10}  {sd.S1[k + 1],12:E2}  {sd.S2[k + 1],12:E2}  {sd.S3[k + 1],12:E2}  {sd.S4[k + 1],12:E2}  {sd.S5[k + 1],12:E2}  {sd.CL[k + 1],11:E2}  {sd.CT[k + 1],11:E2}");
    Console.WriteLine($"   sum              {sd.TotalS1,12:E2}  {sd.TotalS2,12:E2}  {sd.TotalS3,12:E2}  {sd.TotalS4,12:E2}  {sd.TotalS5,12:E2}  {sd.TotalCL,11:E2}  {sd.TotalCT,11:E2}");

    Console.WriteLine();
    Console.WriteLine($"Real-ray RMS spot radius on the paraxial image plane, um (Airy radius {r.AiryRadius * 1000:F2} um)");
    Console.WriteLine("  field deg  " + string.Join("  ", spec.Wavelengths.Select(l => $"{l:F4} um".PadLeft(10))) + "  polychromatic");
    foreach (var f in r.Spots)
        Console.WriteLine($"  {f.FieldDeg,9:F3}  " + string.Join("  ", f.PerWavelength.Select(v => (v * 1000).ToString("F2").PadLeft(10))) + $"  {f.Polychromatic * 1000,13:F2}");
    Console.WriteLine($"At best focus, {r.BestFocusShift:F4} mm from the paraxial image:");
    foreach (var f in r.BestSpots)
        Console.WriteLine($"  {f.FieldDeg,9:F3}  " + string.Join("  ", f.PerWavelength.Select(v => (v * 1000).ToString("F2").PadLeft(10))) + $"  {f.Polychromatic * 1000,13:F2}");
    if (r.Vignetted > 0) Console.WriteLine($"  {r.Vignetted} rays failed");
    foreach (var w in r.Warnings) Console.WriteLine("  warning: " + w);

    if (opt.Has("export"))
    {
        var warnings = new List<string>();
        string name = "order_" + string.Join("", spec.Surfaces.Select(LensModel.Code)).Replace("@", "s").Replace("~", "b");
        foreach (var f in Exporters.Write(r.System, catalog, opt.Get("export", "."), name, Exporters.AllFormats, warnings)) Console.WriteLine("  wrote " + f);
        foreach (var w in warnings) Console.WriteLine("  warning: " + w);
    }
    return 0;
}

static int LayoutsCommand(Options opt)
{
    var lambdas = opt.GetList("lambda", new[] { LensSpec.LambdaD });
    var range = opt.GetList("index", new[] { 1.45, 1.80 });
    var settings = new LayoutSettings
    {
        Efl = opt.GetD("efl", 150), FNumber = opt.GetD("fno", 5.6), HalfField = opt.GetD("hfov", 8),
        Wavelength = lambdas[lambdas.Length / 2],
        MinElements = (int)opt.GetD("min-elements", 1), MaxElements = (int)opt.GetD("elements", 5),
        IndexMin = range[0], IndexMax = range[^1],
        Starts = (int)opt.GetD("starts", 4),
        MaxDistortion = opt.GetD("max-distortion", 1) / 100, MaxLength = opt.GetD("max-length", 2),
        MinCenter = opt.GetD("min-center", 0.01), MinEdge = opt.GetD("min-edge", 0.005), MinBackFocus = opt.GetD("min-bfl", 0.1), MinAirCenter = opt.GetD("min-air", 0.005),
        MinAirEdge = opt.GetD("min-air-edge", 0.005),
        Top = (int)opt.GetD("top", 50),
    };
    var catalog = LoadCatalog(opt.Get("catalogs", ""));
    if (opt.Has("cover"))
    {
        // --cover GLASS,THICKNESS[,GAP]: a plane cover glass, THICKNESS mm, its back face GAP mm (0.5) in front of the image.
        var parts = opt.Get("cover", "").Split(',', StringSplitOptions.TrimEntries);
        settings.SlabGlass = catalog.Find(parts[0]) ?? throw new ArgumentException($"--cover: glass '{parts[0]}' not found; name it CATALOG:NAME");
        settings.SlabThickness = parts.Length > 1 ? double.Parse(parts[1], CultureInfo.InvariantCulture) : throw new ArgumentException("--cover: give GLASS,THICKNESS[,GAP]");
        if (parts.Length > 2) settings.SlabGap = double.Parse(parts[2], CultureInfo.InvariantCulture);
    }
    settings.MaxChiefAngle = opt.GetD("max-cra", 0);
    Console.WriteLine($"Layouts: {settings.MinElements} to {settings.MaxElements} air-spaced singlets, f {settings.Efl}, f/{settings.FNumber}, half field {settings.HalfField} deg, "
                      + $"{settings.Wavelength} um; model glasses {settings.IndexMin}-{settings.IndexMax}; distortion within {settings.MaxDistortion * 100}%, length within {settings.MaxLength} f, "
                      + $"back focus at least {settings.MinBackFocus} f"
                      + (settings.SlabGlass is { } cg && settings.SlabThickness > 0 ? $"; cover glass {Layouts.Id(cg)} {settings.SlabThickness} mm, {settings.SlabGap} mm from the image" : "")
                      + (settings.MaxChiefAngle != 0 ? $"; chief ray within ±{Math.Abs(settings.MaxChiefAngle)} deg at the image" : ""));
    var r = Layouts.Run(settings, new ConsoleProgress());
    Console.WriteLine($"\r  {r.Layouts:N0} layouts, {r.Feasible:N0} within the limits; {r.Elapsed.TotalSeconds:F1} s");
    if (r.Ranked.Count == 0) { Console.WriteLine("No layout could be built within the limits."); return 1; }

    double airy = 1.22 * settings.Wavelength * settings.FNumber;
    void Row(string label, LayoutCandidate c) =>
        Console.WriteLine($"  {label,4}  {c.Rms * 1000,6:F2}  {c.FieldRms[0] * 1000,6:F2}  {c.FieldRms[1] * 1000,6:F2}  {c.FieldRms[2] * 1000,6:F2}  {c.Distortion * 100,6:F2}  {c.Length,6:F1}  {c.Layout,-22}  {string.Join(" ", c.Indices.Select(v => v.ToString("F3")))}");
    string head = $"         all   0 fld  0.7 fld  1 fld      %      mm  (Airy radius {airy:F2} um)";
    Console.WriteLine();
    Console.WriteLine("  best of each size: RMS spot um at best focus   distortion  length  layout                  indices");
    Console.WriteLine(head);
    foreach (var (k, c) in r.BestByElements) Row($"{k} el", c);
    Console.WriteLine();
    Console.WriteLine("  rank  RMS spot um at best focus   distortion  length  layout                  indices");
    Console.WriteLine(head);
    foreach (var c in r.Ranked.Take(25)) Row(c.Rank.ToString(), c);

    string outDir = opt.Get("out", "results");
    Console.WriteLine();
    Console.WriteLine("Ranked list: " + Layouts.WriteCsv(r, Path.Combine(outDir, "layouts.csv")));
    int export = (int)opt.GetD("export", 5);
    var picks = r.Ranked.Take(export).Concat(r.BestByElements.Values).Distinct();
    foreach (var c in picks)
    {
        var a = Analysis.Run(c.Design, catalog);
        var warnings = new List<string>();
        string name = (c.Rank > 0 ? $"rank{c.Rank:D3}_" : $"best{c.Layout.Elements}_") + c.Layout.ToString().Replace(" ", "-");
        Exporters.Write(a.System, catalog, Path.Combine(outDir, name), name, Exporters.AllFormats, warnings);
        Console.WriteLine($"  {name}: AberrationCalculator's trace, RMS spot at best focus {string.Join(" / ", a.BestSpots.Select(s => (s.Polychromatic * 1000).ToString("F2")))} um");
        foreach (var w in warnings) Console.WriteLine($"    {w}");
    }
    Console.WriteLine($"Exported to {Path.GetFullPath(outDir)}");

    if (!opt.Has("color")) return 0;
    settings.ColorLayouts = (int)opt.GetD("color", 50);
    var colorLambdas = opt.GetList("color-lambda", settings.ColorWavelengths);
    settings.ColorWavelengths = colorLambdas;
    if (opt.Has("vendors")) settings.Catalogs = opt.Get("vendors", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    settings.GlassIndexTolerance = opt.GetD("glass-tol", settings.GlassIndexTolerance);
    settings.PairIndexTolerance = opt.GetD("buried-tol", settings.PairIndexTolerance);
    settings.PairMinAbbeSplit = opt.GetD("abbe-split", settings.PairMinAbbeSplit);
    settings.ColorKeep = (int)opt.GetD("color-keep", settings.ColorKeep);
    settings.ColorRounds = (int)opt.GetD("color-rounds", settings.ColorRounds);
    if (opt.Has("common"))
    {
        string folder = opt.Get("catalogs", "") is { Length: > 0 } given ? given : CatalogLocator.Find() ?? "";
        var melt = GlassSet.MeltFrequencies(folder);
        settings.GlassFilter = g => GlassSet.IsCommon(g, melt);
    }
    var bases = r.Ranked.Take(settings.ColorLayouts).Concat(r.BestByElements.Values).Distinct().ToList();
    Console.WriteLine();
    Console.WriteLine($"Color: {bases.Count} layouts given real glasses ({string.Join(",", settings.Catalogs)}{(opt.Has("common") ? ", common glasses only" : "")}, index within {settings.GlassIndexTolerance}), "
                      + (colorLambdas.Distinct().Count() > 1 ? "a C~ buried surface for axial color and at most one M~ for lateral; "
                                                             : "one wavelength, so single glasses, the nearest in index, and no buried surfaces; ")
                      + $"{string.Join(", ", colorLambdas)} um");
    var cr = LayoutColor.Run(settings, bases, catalog, new ConsoleProgress());
    Console.WriteLine($"\r  {cr.Usable} glasses; {cr.Screened:N0} glass choices screened by paraxial color, {cr.Optimized} optimized by real rays, "
                      + $"{cr.Ranked.Count} within the limits; {cr.Elapsed.TotalSeconds:F1} s");
    if (cr.Ranked.Count == 0) { Console.WriteLine("No colored lens within the limits."); return 1; }
    double airyC = 1.22 * colorLambdas[colorLambdas.Length / 2] * settings.FNumber;
    void ColorRow(string label, ColorCandidate c) =>
        Console.WriteLine($"  {label,4}  {c.Rms * 1000,6:F2}  {c.FieldRms[0] * 1000,6:F2}  {c.FieldRms[1] * 1000,6:F2}  {c.FieldRms[2] * 1000,6:F2}  "
                          + $"{c.AxialColor * 1000,7:F1}  {c.LateralColor * 1000,6:F2}  {c.Distortion * 100,6:F2}  {c.Length,6:F1}  {c.Layout,-18}  {c.Glasses}");
    string colorHead = $"         all   0 fld  0.7 fld  1 fld   axial  lateral   dist  length   (Airy radius {airyC:F2} um; color in um)";
    Console.WriteLine();
    Console.WriteLine("  best glasses for each layout: polychromatic RMS spot um at best focus");
    Console.WriteLine(colorHead);
    var bestPerLayout = cr.Ranked.GroupBy(c => c.Layout.ToString()).Select(g => g.First()).OrderBy(c => c.Rms).ToList();
    foreach (var c in bestPerLayout) ColorRow(c.Rank.ToString(), c);

    string colorDir = Path.Combine(outDir, "color");
    Directory.CreateDirectory(colorDir);
    Console.WriteLine("Ranked list: " + LayoutColor.WriteCsv(cr.Ranked, Path.Combine(colorDir, "color.csv")));
    foreach (var c in bestPerLayout.Take(export))
    {
        var a = Analysis.Run(c.Design, catalog);
        var warnings = new List<string>();
        string name = $"color{c.Rank:D3}_" + c.Layout.ToString().Replace(" ", "-");
        Exporters.Write(a.System, catalog, Path.Combine(colorDir, name), name, Exporters.AllFormats, warnings);
        Console.WriteLine($"  {name}: AberrationCalculator's trace, polychromatic RMS spot at best focus {string.Join(" / ", a.BestSpots.Select(s => (s.Polychromatic * 1000).ToString("F2")))} um");
        Console.WriteLine($"    {c.Order}");
        foreach (var wn in warnings) Console.WriteLine($"    {wn}");
    }
    return 0;
}

static GlassCatalog LoadCatalog(string folder)
{
    string? dir = folder.Length > 0 ? folder : CatalogLocator.Find();
    if (dir is null || !Directory.Exists(dir))
        throw new ArgumentException("no glass catalogs found; looked in " + string.Join(", ", CatalogLocator.SearchedPlaces()));
    var catalog = new GlassCatalog();
    catalog.LoadFolder(dir);
    return catalog;
}

/// <summary>--name value pairs; a name with no value is a flag.</summary>
sealed class Options
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public static Options Parse(IEnumerable<string> args)
    {
        var o = new Options();
        var list = args.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (!list[i].StartsWith("--")) throw new ArgumentException($"unexpected '{list[i]}'");
            string name = list[i][2..];
            bool hasValue = i + 1 < list.Count && !list[i + 1].StartsWith("--");
            o._values[name] = hasValue ? list[++i] : "";
        }
        return o;
    }

    public bool Has(string name) => _values.ContainsKey(name);
    public string Get(string name, string fallback) => _values.TryGetValue(name, out var v) ? v : fallback;

    public double GetD(string name, double fallback) =>
        !_values.TryGetValue(name, out var v) ? fallback
        : double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d
        : throw new ArgumentException($"--{name} {v}: not a number");

    public double[] GetList(string name, double[] fallback) =>
        !_values.TryGetValue(name, out var v) ? fallback
        : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x =>
            double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d
            : throw new ArgumentException($"--{name} {v}: not a list of numbers")).ToArray();
}

/// <summary>Whole percentages on one console line, never going backwards (the search reports from several threads).</summary>
sealed class ConsoleProgress : IProgress<double>
{
    private int _shown = -1;
    private readonly object _lock = new();

    public void Report(double value)
    {
        int pct = (int)Math.Floor(value * 100);
        lock (_lock)
        {
            if (pct <= _shown) return;
            _shown = pct;
            Console.Write($"\r  {pct,3}%");
        }
    }
}
