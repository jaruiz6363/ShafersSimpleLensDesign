using AberrationCalculator.Core.Glass;
using Xunit;

using ShafersSimpleLensDesign.Core;

namespace ShafersSimpleLensDesign.Tests;

/// <summary>The layouts' color stage: real glasses, a C~ buried surface for axial color, an M~ for lateral.</summary>
public class LayoutColorTests
{
    private static readonly GlassCatalog Catalog = CatalogLocator.LoadBundled();

    [Fact]
    public void ASurfaceCenteredOnTheMarginalRaysAxialPointHasNoSphericalAberrationOrComa()
    {
        // Shafer's Figure 2 with its last surface centered on the marginal ray's axial point instead of aplanatic.
        var spec = new LensSpec { Efl = 150, FNumber = 5.6, HalfField = 8, Surfaces = Parser.Surfaces("C SCHOTT:N-LAF2 4, C SCHOTT:N-BK7 3, S SCHOTT:N-BK7 3, M air") };
        var d = Builder.Build(spec, Catalog);
        var s = Analysis.Run(d, Catalog).Seidel;
        int last = d.Count;
        double scale = Math.Abs(s.S1[1]) + Math.Abs(s.S4[1]);
        Assert.True(Math.Abs(s.S1[last]) + Math.Abs(s.S2[last]) < 1e-10 * scale);
        Assert.True(Math.Abs(s.S3[last]) > 1e-6 * scale);
        Assert.Equal("M", LensModel.Code(spec.Surfaces[^1]));
        Assert.Equal(Role.Marginal, Parser.Surfaces("C SCHOTT:N-BK7 1, M~ SCHOTT:F2 1, A air")[1].Role);
    }

    [Fact]
    public void WhereAnMSurfaceSitsInItsElementLeavesTheLateralColorAsItIs()
    {
        var settings = new LayoutSettings { IndexMax = 1.75 };
        var layout = new Layout(new[] { "CC", "AC", "AA" }, 1);
        var b = Layouts.Optimize(settings, layout)!;
        var g1 = Catalog.Find("SCHOTT:N-SK16")!;
        var g2 = Catalog.Find("SCHOTT:N-LAK22")!;     // nd 1.651 against 1.620: a model pair, far enough apart in Abbe number
        var single = Catalog.Find("SCHOTT:N-BK7")!;
        double Lateral(double fraction)
        {
            var glazing = new[] { new ElementGlass(single), new ElementGlass(g1, g2, 'M'), new ElementGlass(single) };
            var x = b.X.Take(6).Append(Math.Log(fraction / (1 - fraction))).ToArray();
            var (spec, glass) = Layouts.Lens(settings, layout, x, glazing);
            var d = Builder.Build(spec, glass);
            return Aberrations.Color(d.Spec, d.Glass, d.StopSurface, d.Thickness, d.Curvature).Lateral;
        }
        double a = Lateral(0.2), c = Lateral(0.8);
        Assert.True(Math.Abs(a - c) < 0.03 * Math.Abs(a), $"{a} against {c}");
    }

    [Fact]
    public void FourElementsAtF6GetRealGlassesAndKeepTheirRoles()
    {
        var settings = new LayoutSettings { MinElements = 4, MaxElements = 4, IndexMax = 1.8 };
        var r = Layouts.Run(settings);
        var cr = LayoutColor.Run(settings, r.Ranked.Take(10).ToList(), Catalog);
        Assert.NotEmpty(cr.Ranked);
        var best = cr.Ranked[0];
        Assert.True(best.Rms < 0.006, $"best {best.Rms * 1000:F1} um");
        Assert.Contains(best.Glazing, g => g.Buried == 'C');

        // Concentric surfaces keep no coma or astigmatism and aplanatic ones none either, real glasses and all.
        var d = best.Design;
        var s = Analysis.Run(d, Catalog).Seidel;
        double scale = Enumerable.Range(1, d.Count).Max(q => Math.Abs(s.S1[q]) + Math.Abs(s.S4[q]));
        for (int q = 1; q <= d.Count; q++)
            if (d.Spec.Surfaces[q - 1].Role is Role.Concentric or Role.Aplanatic)
                Assert.True(Math.Abs(s.S2[q]) + Math.Abs(s.S3[q]) < 1e-10 * scale, $"surface {q}");

        // The printed order, buried surfaces and all, rebuilds the same lens.
        var again = Builder.Build(new LensSpec
        {
            Efl = 150, FNumber = 6, HalfField = 8, DesignWavelength = LensSpec.LambdaD, Surfaces = Parser.Surfaces(best.Order),
        }, Catalog);
        for (int k = 0; k < d.Count; k++) Assert.Equal(d.Curvature[k], again.Curvature[k], 4);
    }
}

public class CommonGlassTests
{
    [Fact]
    public void ACommonGlassIsPreferredByItsMakerOrMeltedOften()
    {
        var catalog = CatalogLocator.LoadBundled();
        var melt = GlassSet.MeltFrequencies(CatalogLocator.Find()!);
        Assert.True(GlassSet.IsCommon(catalog.Find("SCHOTT:N-BK7")!, melt));    // preferred, no melt frequency
        Assert.True(GlassSet.IsCommon(catalog.Find("OHARA:S-BSL7")!, melt));    // standard, melted most often
        Assert.False(GlassSet.IsCommon(catalog.Find("OHARA:S-NPH7")!, melt));   // standard, melted less often (3)
    }
}

public class ColorRoundTests
{
    [Fact]
    public void PickingTheGlassesAgainForTheOptimizedThicknessesNeverLosesTheBestLens()
    {
        var catalog = CatalogLocator.LoadBundled();
        var settings = new LayoutSettings { MinElements = 3, MaxElements = 3, Starts = 2, IndexMax = 1.8 };
        var bases = Layouts.Run(settings).Ranked.Take(5).ToList();
        var once = LayoutColor.Run(settings, bases, catalog);
        settings.ColorRounds = 2;
        var twice = LayoutColor.Run(settings, bases, catalog);
        Assert.True(twice.Optimized > once.Optimized);
        Assert.True(twice.Ranked[0].Rms <= once.Ranked[0].Rms + 1e-9, $"{twice.Ranked[0].Rms} against {once.Ranked[0].Rms}");

        // The stand-in for picking glasses again has the colored lens's radii, the buried surfaces aside, to within what
        // one index for a split element's two glasses (their mean) allows.
        var c = once.Ranked[0];
        var stand = LayoutColor.Equivalent(settings, c)!;
        var colored = c.Design.Curvature.Where((_, q) => !c.Design.Spec.Surfaces[q].IsBuried).ToArray();
        double scale = colored.Max(Math.Abs);
        for (int q = 0; q < colored.Length; q++)
            Assert.True(Math.Abs(colored[q] - stand.Design.Curvature[q]) < 0.02 * scale, $"surface {q + 1}: {colored[q]} against {stand.Design.Curvature[q]}");
    }

    [Fact]
    public void AtOneWavelengthEachElementTakesTheNearestSingleGlass()
    {
        var settings = new LayoutSettings { MinElements = 3, MaxElements = 3, Starts = 2, ColorWavelengths = new[] { LensSpec.LambdaD }, ColorWeights = new[] { 1.0 } };
        var r = Layouts.Run(settings);
        var cr = LayoutColor.Run(settings, r.Ranked.Take(3).ToList(), CatalogLocator.LoadBundled());
        Assert.NotEmpty(cr.Ranked);
        foreach (var c in cr.Ranked)
        {
            Assert.All(c.Glazing, g => Assert.False(g.Split));
            for (int i = 0; i < c.Glazing.Length; i++)
                Assert.True(Math.Abs(c.Glazing[i].Front.Nd - c.Base.Indices[i]) <= settings.GlassIndexTolerance + 1e-9);
        }
        Assert.True(cr.Ranked[0].Rms < 2 * r.Ranked[0].Rms, $"{cr.Ranked[0].Rms * 1000:F2} um with real glasses, {r.Ranked[0].Rms * 1000:F2} with model");
    }
}
