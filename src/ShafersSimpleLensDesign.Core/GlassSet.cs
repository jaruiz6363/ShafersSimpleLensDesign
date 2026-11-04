using AberrationCalculator.Core.Glass;

namespace ShafersSimpleLensDesign.Core;

/// <summary>A catalog glass as the search uses it: its index at the design wavelength and at each search wavelength,
/// and any other names the same catalog gives identical data.</summary>
public sealed class SearchGlass
{
    public required GlassData Data { get; init; }
    public required double NDesign { get; init; }
    public required double[] N { get; init; }
    public List<string> Aliases { get; } = new();

    public string Id => $"{Data.Catalog}:{Data.Name}";
    public string Label => Aliases.Count == 0 ? Id : $"{Id} (={string.Join("=", Aliases)})";

    public static SearchGlass From(GlassData g, double design, double[] lambdas) => new()
    {
        Data = g, NDesign = g.IndexAt(design), N = lambdas.Select(g.IndexAt).ToArray(),
    };
}

public static class GlassSet
{
    /// <summary>The vendor catalogs searched unless told otherwise.</summary>
    public static readonly string[] DefaultCatalogs = { "SCHOTT", "OHARA", "HOYA", "SUMITA", "CDGM" };

    /// <summary>
    /// The glasses a search may use: from the named catalogs; not obsolete (unless asked); not a glass whose catalog
    /// nd/Vd contradict its own dispersion data, since either could be wrong; with data covering the design wavelength
    /// and every search wavelength, giving a real index at each. Glasses one catalog lists twice with identical data
    /// (SUMITA's K-LaSFn9 and LASFN9) are kept once, the other name recorded as an alias. <paramref name="left"/> gets
    /// one line per kind of glass left out, with the count.
    /// </summary>
    public static List<SearchGlass> Usable(GlassCatalog catalog, IEnumerable<string> catalogs, double design, double[] lambdas,
        bool includeObsolete, List<string>? left = null)
    {
        double lo = Math.Min(lambdas.Min(), design), hi = Math.Max(lambdas.Max(), design);
        int obsolete = 0, corrected = 0, range = 0, aliases = 0;
        var kept = new List<SearchGlass>();
        foreach (var cat in catalogs)
        {
            var mine = new List<SearchGlass>();
            foreach (var g in catalog.InCatalog(cat).OrderBy(g => g.Name, StringComparer.Ordinal))
            {
                if (!includeObsolete && g.Status == GlassStatus.Obsolete) { obsolete++; continue; }
                if (g.ListedValuesCorrected) { corrected++; continue; }
                if (g.Nd <= 1.0 || g.LambdaMax <= 0 || g.LambdaMin > lo + 1e-6 || g.LambdaMax < hi - 1e-6) { range++; continue; }
                var sg = SearchGlass.From(g, design, lambdas);
                if (!(sg.NDesign > 1) || sg.N.Any(n => !(n > 1))) { range++; continue; }
                var twin = mine.FirstOrDefault(k => SameData(k.Data, g));
                if (twin != null) { twin.Aliases.Add(g.Name); aliases++; continue; }
                mine.Add(sg);
            }
            kept.AddRange(mine);
        }
        if (left != null)
        {
            if (obsolete > 0) left.Add($"{obsolete} obsolete");
            if (corrected > 0) left.Add($"{corrected} whose catalog nd/Vd contradict their dispersion data");
            if (range > 0) left.Add($"{range} without data over {lo}-{hi} um");
            if (aliases > 0) left.Add($"{aliases} second names for identical data");
        }
        return kept;
    }

    /// <summary>
    /// The melt frequency each catalog gives its glasses, by CATALOG:NAME: the ninth field of an .agf NM line, 1 for the
    /// glasses melted most often (AberrationCalculator keeps only the status before it). A glass without one is left out.
    /// </summary>
    public static Dictionary<string, int> MeltFrequencies(string folder)
    {
        var melt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in GlassCatalog.AgfFiles(folder))
        {
            string catalog = Path.GetFileNameWithoutExtension(file).ToUpperInvariant();
            foreach (var raw in File.ReadLines(file))
            {
                var parts = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 8 && parts[0].Equals("NM", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(parts[8], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int m) && m > 0)
                    melt[catalog + ":" + parts[1]] = m;
            }
        }
        return melt;
    }

    /// <summary>
    /// A common glass: one its maker marks preferred, or melts most often (melt frequency 1 or 2). Makers use the two
    /// differently: SCHOTT marks its current glasses preferred and gives no melt frequency, OHARA marks almost none
    /// preferred and ranks them all by melt frequency, so either one is enough.
    /// </summary>
    public static bool IsCommon(GlassData g, IReadOnlyDictionary<string, int> melt) =>
        g.Status == GlassStatus.Preferred || (melt.TryGetValue($"{g.Catalog}:{g.Name}", out int m) && m <= 2);

    /// <summary>The catalog name of model glasses.</summary>
    public const string ModelCatalog = "MODEL";

    /// <summary>A model glass of index <paramref name="nd"/> at every wavelength (the Schott formula with n² constant),
    /// for designing at one wavelength before real glasses are chosen. Exported as a model glass with no dispersion (Vd 0).</summary>
    public static GlassData Model(double nd) => new()
    {
        Name = nd.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), Catalog = ModelCatalog, Formula = DispersionFormula.Schott,
        Coefficients = new[] { nd * nd, 0, 0, 0, 0, 0 }, Nd = nd, Vd = 0, LambdaMin = 0.2, LambdaMax = 3,
    };

    private static bool SameData(GlassData a, GlassData b) =>
        a.Formula == b.Formula && a.Coefficients.Length == b.Coefficients.Length
        && a.Coefficients.Zip(b.Coefficients).All(p => Math.Abs(p.First - p.Second) <= 1e-12 * Math.Max(1, Math.Abs(p.First)));
}
