using AberrationCalculator.Core.Enums;
using AberrationCalculator.Core.Glass;
using AberrationCalculator.Core.Models;

namespace ShafersSimpleLensDesign.Core;

/// <summary>A built design as an AberrationCalculator lens: object at infinity, entrance pupil from the f-number,
/// fields on axis, at 0.7 and at full field, the image at the paraxial focus.</summary>
public static class LensModel
{
    public static OpticalSystem Build(Design d, GlassCatalog catalog, List<string> warnings)
    {
        var spec = d.Spec;
        var sys = new OpticalSystem
        {
            Title = "Surface order: " + string.Join(" ", spec.Surfaces.Select(Code)),
            Notes = $"f {spec.Efl}, f/{spec.FNumber}, half field {spec.HalfField} deg. Built by Shafer's Simple Lens Design after D. Shafer, Proc. SPIE 237 (1980).",
            Aperture = new Aperture(ApertureType.EPD, spec.Efl / spec.FNumber),
            FieldType = FieldType.ObjectAngle,
        };
        foreach (double l in spec.Wavelengths)
            sys.Wavelengths.Add(new Wavelength(l, 1.0, Math.Abs(l - spec.DesignWavelength) < 1e-9));
        if (!sys.Wavelengths.Any(w => w.IsPrimary)) sys.Wavelengths.Add(new Wavelength(spec.DesignWavelength, 1.0, true));
        foreach (double f in new[] { 0.0, 0.7, 1.0 }) sys.Fields.Add(new Field(f * spec.HalfField));
        sys.GlassCatalogs.AddRange(CatalogOrder(d.Glass.OfType<GlassData>().Where(g => !IsModel(g)).ToList(), catalog, warnings));

        sys.Surfaces.Add(new Surface { Index = 0, Comment = "Object", Radius = double.PositiveInfinity, Thickness = double.PositiveInfinity });
        for (int k = 0; k < d.Count; k++)
        {
            var s = spec.Surfaces[k];
            var glass = d.Glass[k];
            bool model = glass is not null && IsModel(glass);
            sys.Surfaces.Add(new Surface
            {
                Index = sys.Surfaces.Count,
                Comment = s.Role switch
                {
                    Role.Concentric => "Concentric",
                    Role.Aplanatic => "Aplanatic",
                    Role.Stop => "Stop",
                    Role.Buried => "Buried",
                    Role.Marginal => "Marginal",
                    _ => "Fixed",
                },
                Radius = d.Radius(k),
                Thickness = d.Thickness[k],
                Material = model ? null : glass?.Name,
                CatalogName = model ? null : glass?.Catalog,
                ModelIndexEnabled = model,
                ModelNd = model ? glass!.Nd : 0,
                IsStop = k == d.StopSurface,
                SemiDiameterMode = SemiDiameterMode.Auto,
            });
        }
        sys.Surfaces.Add(new Surface { Index = sys.Surfaces.Count, Comment = "Image", Radius = double.PositiveInfinity });
        return sys;
    }

    private static bool IsModel(GlassData g) => g.Catalog == GlassSet.ModelCatalog;

    /// <summary>The surface order's code for one surface: C, A, S, B or F, with @ at the stop.</summary>
    public static string Code(SurfaceSpec s) =>
        (s.Role switch { Role.Concentric => "C", Role.Aplanatic => "A", Role.Stop => "S", Role.Buried => "B", Role.Marginal => "M", _ => "F" }) + (s.Partner ? "~" : "") + (s.IsStop ? "@" : "");

    /// <summary>A catalog order under which every bare glass name resolves to its own vendor's entry.</summary>
    private static List<string> CatalogOrder(List<GlassData> glasses, GlassCatalog catalog, List<string> warnings)
    {
        var cats = glasses.Select(g => g.Catalog).Distinct().ToList();
        bool Resolves(List<string> order) => glasses.All(g => catalog.Find(g.Name, order)?.Catalog == g.Catalog);
        foreach (var order in Permutations(cats))
            if (Resolves(order)) return order;
        warnings.Add("no catalog order resolves every bare glass name to its own vendor");
        return cats;
    }

    private static IEnumerable<List<string>> Permutations(List<string> items)
    {
        if (items.Count <= 1) { yield return new List<string>(items); yield break; }
        for (int i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, k) => k != i).ToList();
            foreach (var p in Permutations(rest)) { p.Insert(0, items[i]); yield return p; }
        }
    }
}
