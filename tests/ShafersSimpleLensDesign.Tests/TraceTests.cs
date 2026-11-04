using AberrationCalculator.Core.Glass;
using Xunit;

using ShafersSimpleLensDesign.Core;

namespace ShafersSimpleLensDesign.Tests;

/// <summary>The fast skew trace and spot merit against AberrationCalculator's, and paraxial color.</summary>
public class TraceTests
{
    private static readonly GlassCatalog Catalog = CatalogLocator.LoadBundled();

    private const string Figure2 = "C SCHOTT:N-LASF40 4, C SCHOTT:N-FK51A 3, S = 3, A air";

    private static LensSpec Spec(string order) => new()
    {
        Efl = 150, FNumber = 5.6, HalfField = 8, Surfaces = Parser.Surfaces(order), Targets = Parser.Targets("lsa:1, petzval:3"),
    };

    [Fact]
    public void TheFastSkewTraceAgreesWithAberrationCalculatorsTrace()
    {
        var d = Builder.Build(Spec(Figure2), Catalog);
        var nD = d.Indices(LensSpec.LambdaD);
        var (yb, _) = Builder.ChiefRay(nD, d.StopSurface, d.Thickness, d.Curvature);
        var a = Analysis.Run(d, Catalog, 2);
        var sys = a.System;
        var n = AberrationCalculator.Core.Glass.IndexResolver.Build(sys, Catalog, LensSpec.LambdaD);
        // The same rays, through the same paraxial entrance pupil, land in the same place.
        foreach (double field in new[] { 0.0, 5.6, 8.0 })
        {
            var par = AberrationCalculator.Core.RayTrace.ParaxialTrace.Trace(sys, n, field);
            foreach (var (px, py) in new[] { (0.0, 1.0), (0.0, -1.0), (0.7, 0.3), (-0.5, -0.5) })
            {
                var mine = SkewTrace.Trace(nD, d.Thickness, d.Curvature, field, px, py, -yb[0], d.Spec.Efl / (2 * d.Spec.FNumber));
                var ac = AberrationCalculator.Core.RayTrace.RealRayTrace.Trace(sys, n, par, field, py, px, false);
                Assert.True(mine.Ok && ac.Ok);
                Assert.Equal(ac.Y, mine.Y, 9);
                Assert.Equal(ac.Z, mine.X, 9);
            }
        }
    }

    [Fact]
    public void TheSearchMeritIsCloseToTheFullAnalysisAtBestFocus()
    {
        var d = Builder.Build(Spec(Figure2), Catalog);
        var fast = SpotMerit.Of(d)!.Value;
        var full = Analysis.Run(d, Catalog, 40);
        for (int f = 0; f < 3; f++)
            Assert.Equal(full.BestSpots[f].Polychromatic, fast.FieldRms[f], 0.05 * full.BestSpots[f].Polychromatic);
    }

    [Fact]
    public void ColorIsTheShortestWavelengthsFocusLessTheLongests()
    {
        var d = Builder.Build(Spec(Figure2), Catalog);
        var col = Aberrations.ColorOf(d);
        var (yF, uF) = Builder.Marginal(d.Spec, d.Indices(LensSpec.LambdaF), d.Curvature, d.Thickness);
        var (yC, uC) = Builder.Marginal(d.Spec, d.Indices(LensSpec.LambdaC), d.Curvature, d.Thickness);
        Assert.Equal(-yF / uF - -yC / uC, col.Axial, 12);
    }
}
