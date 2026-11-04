using AberrationCalculator.Core.Glass;
using Xunit;

using ShafersSimpleLensDesign.Core;

namespace ShafersSimpleLensDesign.Tests;

/// <summary>The layout search: every layout of air-spaced singlets at one wavelength, model glasses, thicknesses optimized.</summary>
public class LayoutTests
{
    private static readonly GlassCatalog Catalog = CatalogLocator.LoadBundled();

    [Fact]
    public void ThereAreFourKindsOfElementButAPlatePlacesNoneFirstAndNPlusOnePlacesForTheStop()
    {
        for (int k = 1; k <= 4; k++)
            Assert.Equal(3 * (1 << (2 * (k - 1))) * (k + 1), Layouts.All(k, k).Count());
        Assert.DoesNotContain(Layouts.All(1, 3), l => l.Kinds[0] == "AA");
        Assert.Equal("CC S AC", new Layout(new[] { "CC", "AC" }, 1).ToString());
    }

    [Fact]
    public void AModelGlassHasOneIndexAtEveryWavelengthAndCanBeNamedInAnOrder()
    {
        var g = GlassSet.Model(1.7);
        Assert.Equal(1.7, g.IndexAt(0.4), 12);
        Assert.Equal(1.7, g.IndexAt(0.9), 12);
        var spec = new LensSpec { Surfaces = Parser.Surfaces("C MODEL:1.7 1, A@ air") };
        Assert.Equal(1.7, Builder.ResolveGlasses(spec, Catalog)[0]!.IndexAt(LensSpec.LambdaD), 12);
    }

    [Fact]
    public void TheSpotResidualsSumToTheMeritsSquare()
    {
        // Shafer's Figure 2, at three wavelengths.
        var spec = new LensSpec { Efl = 150, FNumber = 5.6, HalfField = 8, Surfaces = Parser.Surfaces("C SCHOTT:N-LAF2 4, C SCHOTT:N-BK7 3, S SCHOTT:N-BK7 3, A air") };
        var d = Builder.Build(spec, Catalog);
        var r = SpotMerit.Residuals(d, out var merit)!;
        Assert.Equal(merit.Rms * merit.Rms, r.Sum(v => v * v), 12);
        Assert.Equal(SpotMerit.Of(d)!.Value.Rms, merit.Rms, 12);
    }

    [Fact]
    public void TwoElementsAtF6AreOptimizedWithinTheLimitsAndStayShafersLenses()
    {
        var settings = new LayoutSettings { MinElements = 2, MaxElements = 2, Starts = 2 };
        var r = Layouts.Run(settings);
        Assert.NotEmpty(r.Ranked);
        var best = r.Ranked[0];
        Assert.True(best.Rms < 0.02, $"best {best.Rms * 1000:F1} um");
        Assert.True(Math.Abs(best.Distortion) <= settings.MaxDistortion + 1e-6);
        Assert.True(best.Length <= settings.MaxLength * settings.Efl + 1e-6);

        // Every surface keeps its role: coma and astigmatism are zero whatever the thicknesses and indices became.
        var d = best.Design;
        var s = Analysis.Run(d, Catalog).Seidel;
        double scale = Enumerable.Range(1, d.Count).Max(q => Math.Abs(s.S1[q]) + Math.Abs(s.S4[q]));
        for (int q = 1; q <= d.Count; q++) Assert.True(Math.Abs(s.S2[q]) + Math.Abs(s.S3[q]) < 1e-10 * scale, $"surface {q}");

        // The printed order rebuilds the same lens.
        var again = Builder.Build(new LensSpec
        {
            Efl = 150, FNumber = 6, HalfField = 8, Wavelengths = new[] { LensSpec.LambdaD }, DesignWavelength = LensSpec.LambdaD,
            Surfaces = Parser.Surfaces(best.Order),
        }, Catalog);
        for (int k = 0; k < d.Count; k++) Assert.Equal(d.Curvature[k], again.Curvature[k], 4);
    }
}

public class LayoutLimitTests
{
    [Fact]
    public void ALensOutsideTheLimitsSaysWhichAndTheMinimizationBringsItInside()
    {
        var settings = new LayoutSettings { MinElements = 2, MaxElements = 2 };
        var layout = new Layout(new[] { "CC", "AC" }, 1);
        var b = Layouts.Optimize(settings, layout)!;

        // Allowed 5% less length than the best lens has, it is outside that limit, by name, and the minimization, its
        // penalties growing, brings it inside.
        var shorter = new LayoutSettings { MinElements = 2, MaxElements = 2, MaxLength = 0.95 * b.Length / settings.Efl };
        var why = new List<string>();
        Assert.NotNull(Layouts.Evaluate(shorter, layout, b.X, why: why));
        Assert.Contains(why, w => w.StartsWith("length"));
        var e = Layouts.Minimize(shorter, layout, b.X)!;
        var left = new List<string>();
        Layouts.Evaluate(shorter, layout, e.X, why: left);
        Assert.True(e.Violation <= 1e-4, $"still outside by {e.Violation} mm: {string.Join("; ", left)}");
    }
}
