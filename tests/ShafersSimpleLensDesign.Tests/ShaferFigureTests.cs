using AberrationCalculator.Core.Glass;
using Xunit;

using ShafersSimpleLensDesign.Core;

namespace ShafersSimpleLensDesign.Tests;

/// <summary>
/// The method's claims, and the paper's figures, checked against the builder and against AberrationCalculator's own
/// Seidel sums and exact trace (D. Shafer, "Simple method for designing lenses", Proc. SPIE 237, 1980).
/// </summary>
public class ShaferFigureTests
{
    private static readonly GlassCatalog Catalog = CatalogLocator.LoadBundled();

    private static LensSpec Spec(string order, string solve = "", double efl = 100, double fno = 8, double hfov = 3) => new()
    {
        Efl = efl, FNumber = fno, HalfField = hfov, Surfaces = Parser.Surfaces(order), Targets = Parser.Targets(solve),
    };

    private const string Figure2 = "C SCHOTT:N-LAF2 4, C SCHOTT:N-BK7 3, S SCHOTT:N-BK7 3, A air";

    [Fact]
    public void Figure1_TheAplanaticBackHasTheFrontRadiusWhenTheIndexIsTheGoldenRatio()
    {
        // A concentric front surface centered on the stop at the back surface, which is aplanatic. The rules give
        // R2 = R1 n/((n - 1)(n + 1)), which is R1 exactly when n = n^2 - 1: the golden ratio, Shafer's "n = 1.62".
        foreach (var (glass, equal) in new[] { ("SCHOTT:N-SK16", true), ("SCHOTT:N-BK7", false), ("SCHOTT:N-LASF9", false) })
        {
            var d = Builder.Build(Spec($"C {glass} 1, A@ air"), Catalog);
            double n = d.Indices(LensSpec.LambdaD)[1];
            double ratio = d.Radius(1) / d.Radius(0);
            Assert.Equal(n / ((n - 1) * (n + 1)), ratio, 12);
            Assert.Equal(d.Radius(0), d.Thickness[0], 12);                // the front surface is centered on the stop
            if (equal) Assert.Equal(1.0, ratio, 2);
        }
    }

    [Fact]
    public void EverySurfaceDoesItsJobByAberrationCalculatorsSeidelSums()
    {
        foreach (var (order, solve) in new[] { ("C SCHOTT:N-SK16 1, A@ air", ""), (Figure2, "spherical:1, petzval:3"), (Figure2, "lsa:1, petzval:3") })
        {
            var d = Builder.Build(Spec(order, solve, 150, 5.6, 8), Catalog);
            var s = Analysis.Run(d, Catalog, 2).Seidel;
            double scale = new[] { s.S1, s.S4, s.S5 }.SelectMany(v => v).Max(Math.Abs);
            for (int k = 0; k < d.Count; k++)
            {
                int i = k + 1;     // AberrationCalculator's surface 0 is the object
                switch (d.Spec.Surfaces[k].Role)
                {
                    case Role.Concentric:
                        Assert.True(Math.Abs(s.S2[i]) + Math.Abs(s.S3[i]) + Math.Abs(s.S5[i]) < 1e-12 * scale, $"{order}: concentric surface {i} has field aberrations");
                        break;
                    case Role.Aplanatic:
                        Assert.True(Math.Abs(s.S1[i]) + Math.Abs(s.S2[i]) + Math.Abs(s.S3[i]) < 1e-12 * scale, $"{order}: aplanatic surface {i} has S1-S3");
                        break;
                }
            }
            Assert.True(Math.Abs(s.TotalS2) < 1e-12 * scale && Math.Abs(s.TotalS3) < 1e-12 * scale, $"{order}: coma or astigmatism left");
        }
    }

    [Fact]
    public void TheBuildersOwnSeidelSumsAreAberrationCalculatorsUpToOneScale()
    {
        var d = Builder.Build(Spec(Figure2, "lsa:1, petzval:3", 150, 5.6, 8), Catalog);
        var n = d.Indices(LensSpec.LambdaD);
        var mine = Aberrations.Surfaces(n, d.StopSurface, d.Thickness, d.Curvature);
        var ac = Analysis.Run(d, Catalog, 2).Seidel;
        // Same formulas, different normalization of the rays: each sum differs by one factor for the whole lens.
        double k1 = ac.S1[1] / mine[0].S1, k4 = ac.S4[1] / mine[0].S4;
        for (int k = 0; k < d.Count; k++)
        {
            Assert.Equal(ac.S1[k + 1], k1 * mine[k].S1, 9);
            Assert.Equal(ac.S4[k + 1], k4 * mine[k].S4, 9);
        }
    }

    [Fact]
    public void Figure2_TheTargetsAreMetAndTheResultIsOneLensWhateverTheStartingGaps()
    {
        var a = Builder.Build(Spec(Figure2, "lsa:1, petzval:3", 150, 5.6, 8), Catalog);
        var b = Builder.Build(Spec(Figure2.Replace("N-BK7 3, S", "N-BK7 20, S"), "lsa:1, petzval:3", 150, 5.6, 8), Catalog);
        var n = a.Indices(LensSpec.LambdaD);
        Assert.True(Math.Abs(Aberrations.Surfaces(n, a.StopSurface, a.Thickness, a.Curvature).Sum(v => v.Petzval)) < 1e-12);
        Assert.True(Math.Abs(Aberrations.RealSpherical(a.Spec, n, a.Thickness, a.Curvature)) < 1e-10);
        // Three gaps, one of them only the scale, two solved: the glasses decide the lens.
        for (int k = 0; k < a.Count; k++)
        {
            Assert.Equal(a.Curvature[k], b.Curvature[k], 9);
            Assert.Equal(a.Thickness[k], b.Thickness[k], 7);
        }
        Assert.Equal(150.0, Efl(a), 9);
    }

    [Fact]
    public void Figure2_CorrectingTheRealMarginalRayBeatsZeroThirdOrder()
    {
        var third = Analysis.Run(Builder.Build(Spec(Figure2, "spherical:1, petzval:3", 150, 5.6, 8), Catalog), Catalog);
        var real = Analysis.Run(Builder.Build(Spec(Figure2, "lsa:1, petzval:3", 150, 5.6, 8), Catalog), Catalog);
        Assert.True(real.BestSpots[0].PerWavelength[1] < 0.25 * third.BestSpots[0].PerWavelength[1]);
        // A flat-field anastigmat: the spot barely grows across the field.
        Assert.True(real.BestSpots[2].PerWavelength[1] < 1.3 * real.BestSpots[0].PerWavelength[1]);
    }

    [Fact]
    public void ABuriedSurfaceBetweenOneGlassChangesNothing()
    {
        // No index break and no dispersion break: whatever its radius, the lens is the lens without it.
        const string with = "C SCHOTT:N-LASF40 4, C SCHOTT:N-FK51A 3, S = 2.5, B=7 = 0.5, A air";
        const string without = "C SCHOTT:N-LASF40 4, C SCHOTT:N-FK51A 3, S = 3, A air";
        var a = Analysis.Run(Builder.Build(Spec(with, "lsa:1, petzval:3", 150, 5.6, 8), Catalog), Catalog, 6);
        var b = Analysis.Run(Builder.Build(Spec(without, "lsa:1, petzval:3", 150, 5.6, 8), Catalog), Catalog, 6);
        for (int f = 0; f < 3; f++) Assert.Equal(b.BestSpots[f].Polychromatic, a.BestSpots[f].Polychromatic, 9);
        Assert.Equal(b.Design.Thickness.Sum(), a.Design.Thickness.Sum(), 9);
    }

    [Fact]
    public void ABuriedSurfacesRadiusSolvesLateralColor()
    {
        var spec = Spec("C SCHOTT:N-LASF40 4, C SCHOTT:N-FK51A 3, S = 2.5, B SCHOTT:N-FK5 0.5, A air", "spherical:1, petzval:3, lateral:r4", 150, 5.6, 8);
        var d = Builder.Build(spec, Catalog);
        var col = Aberrations.ColorOf(d);
        Assert.True(Math.Abs(col.Lateral) < 1e-7, $"lateral color {col.Lateral} mm");
        Assert.True(Math.Abs(col.Axial) > 1e-3, "axial color was not a target and should remain");
        // AberrationCalculator's Seidel lateral color, first order in the dispersion, agrees to first order: the exact
        // paraxial difference that was solved differs from it by terms in the square of the dispersion.
        var s = Analysis.Run(d, Catalog, 2).Seidel;
        Assert.True(Math.Abs(s.TotalCT) < 1e-3 * s.CT.Max(Math.Abs), $"CT {s.TotalCT}");
    }

    [Fact]
    public void Figure3_WithBlankBuriedPlanesIsTheSameLensAsWithout()
    {
        // Figure 5 is Figure 3 with each element made an achromat by a buried surface. With the buried surfaces between one
        // glass (no break at all) it must be Figure 3 exactly: a thickness knob that runs into a buried surface sets the
        // whole element, the buried surface keeping its place in proportion.
        var fig3 = Builder.Build(Spec("C SUMITA:K-LaFK65 3, A air 2, S air 4.5, C OHARA:SK-1300 1.5, C air", "lsa:3, petzval:4", 150, 5.6, 8), Catalog);
        var fig5 = Builder.Build(Spec("C SUMITA:K-LaFK65 1.5, B = 1.5, A air 2, S air 4.5, C OHARA:SK-1300 0.75, B = 0.75, C air", "lsa:4, petzval:5", 150, 5.6, 8), Catalog);
        var keep = new[] { 0, 2, 3, 4, 6 };     // Figure 5's surfaces that are Figure 3's
        for (int i = 0; i < keep.Length; i++) Assert.Equal(fig3.Curvature[i], fig5.Curvature[keep[i]], 10);
        Assert.Equal(fig3.Thickness[0], fig5.Thickness[0] + fig5.Thickness[1], 9);
        Assert.Equal(fig3.Thickness[3], fig5.Thickness[4] + fig5.Thickness[5], 9);
        Assert.Equal(fig5.Thickness[4], fig5.Thickness[5], 9);     // the shell's buried plane stays in the middle
    }

    [Fact]
    public void Figure5_TwoBuriedSurfacesCorrectAxialAndLateralColor()
    {
        var d = Builder.Build(Spec("C SUMITA:K-LaFK58 1.5, B SUMITA:K-CD45(M) 1.5, A air 2, S air 4.5, C SUMITA:K-PFK80(M) 0.75, B CDGM:H-K3 0.75, C air",
            "lsa:4, petzval:5, axial:r2, lateral:r6", 150, 5.6, 8), Catalog);
        var col = Aberrations.ColorOf(d);
        Assert.True(Math.Abs(col.Axial) < 1e-7 && Math.Abs(col.Lateral) < 1e-7, $"color {col}");
        var withColor = Analysis.Run(d, Catalog);
        var without = Analysis.Run(Builder.Build(Spec("C SUMITA:K-LaFK58 3, A air 2, S air 4.5, C SUMITA:K-PFK80(M) 1.5, C air", "lsa:3, petzval:4", 150, 5.6, 8), Catalog), Catalog);
        for (int f = 0; f < 3; f++)
            Assert.True(withColor.BestSpots[f].Polychromatic < 0.5 * without.BestSpots[f].Polychromatic,
                $"field {f}: {withColor.BestSpots[f].Polychromatic} against {without.BestSpots[f].Polychromatic} without the buried surfaces");
    }

    [Fact]
    public void Figure6_TheSplitFrontElementsShareOneGlass()
    {
        // "=1": the second front element is the first element's glass, as when Shafer splits one element in two.
        var d = Builder.Build(Spec("C SCHOTT:N-BK7 2, A air 0.2, C =1 2, A air 1.5, S air 4.5, C SCHOTT:N-BK7 1.5, C air", "", 150, 4, 8), Catalog);
        Assert.Same(d.Glass[0], d.Glass[2]);
        Assert.Null(d.Glass[1]);
        var ex = Assert.Throws<ArgumentException>(() => Builder.Build(Spec("C =3 2, A air 1, S air 1, C SCHOTT:N-BK7 1, C air"), Catalog));
        Assert.Contains("earlier surface", ex.Message);
    }

    [Fact]
    public void ARealSphericalTargetAtAZoneBringsThatZonesRayToFocus()
    {
        var spec = Spec(Figure2, "lsa@0.85:1, petzval:3", 150, 5.6, 8);
        var d = Builder.Build(spec, Catalog);
        var n = d.Indices(LensSpec.LambdaD);
        Assert.True(Math.Abs(Aberrations.RealSpherical(spec, n, d.Thickness, d.Curvature, 0.85)) < 1e-10);
        Assert.True(Math.Abs(Aberrations.RealSpherical(spec, n, d.Thickness, d.Curvature, 1.0)) > 1e-6);
    }

    [Fact]
    public void ABuriedSurfaceThatKeepsItsRoleAddsNoComaOrAstigmatism()
    {
        // A concentric interface between partner glasses (C~) inside the shell: whatever the index step, AberrationCalculator
        // finds no coma or astigmatism there, and the lens as a whole stays free of them.
        var d = Builder.Build(Spec("C SUMITA:K-LaFK58 3, A air 2, S air 4.5, C SUMITA:K-PFK80(M) 0.75, C~ CDGM:H-K3 0.75, C air", "lsa:3, petzval:4", 150, 5.6, 8), Catalog);
        Assert.True(d.Spec.Surfaces[4].Partner);
        var s = Analysis.Run(d, Catalog, 2).Seidel;
        double scale = s.S1.Max(Math.Abs);
        Assert.True(Math.Abs(s.S2[5]) + Math.Abs(s.S3[5]) < 1e-12 * scale, $"S2 {s.S2[5]}, S3 {s.S3[5]}");
        Assert.True(Math.Abs(s.TotalS2) + Math.Abs(s.TotalS3) < 1e-12 * scale);
        Assert.True(Math.Abs(s.CT[5]) < 1e-12 * s.CT.Max(Math.Abs), "a concentric interface changes axial color only");
        Assert.True(Math.Abs(s.CL[5]) > 1e-6 * s.CL.Max(Math.Abs));
    }

    [Fact]
    public void APositionKnobMovesASurfaceWithinItsElement()
    {
        const string order = "C SUMITA:K-LaFK58 3, A air 2, S air 4.5, C SUMITA:K-PFK80(M) 0.75, C~ CDGM:H-K3 0.75, C air";
        var spec = Spec(order, "lsa:3, petzval:4, axial:p5", 150, 5.6, 8);
        try
        {
            var d = Builder.Build(spec, Catalog);
            Assert.True(Math.Abs(Aberrations.ColorOf(d).Axial) < 1e-7);
        }
        catch (InvalidOperationException ex)
        {
            // Whether this pair can reach zero axial color depends on the glasses; the knob itself must keep the element whole.
            Assert.Contains("targets", ex.Message);
        }
        Assert.Throws<ArgumentException>(() => Builder.Build(Spec(order, "lsa:3, petzval:4, axial:p1", 150, 5.6, 8), Catalog));
        Assert.Throws<ArgumentException>(() => Builder.Build(Spec(order, "lsa:3, petzval:4, axial:p6", 150, 5.6, 8), Catalog));
    }

    [Fact]
    public void Figure7_AFreeCementedSurfaceSolvesAxialColorAndTheStopPositionItsComa()
    {
        var spec = Spec("C HOYA:FD300 2, A air 0.5, C =1 3, F=1.853 SUMITA:K-PFK85 3, S = 3, A air", "lsa:2, petzval:5, axial:r4, coma:p5", 150, 4, 8);
        var d = Builder.Build(spec, Catalog);
        var n = d.Indices(LensSpec.LambdaD);
        var s = Aberrations.Surfaces(n, d.StopSurface, d.Thickness, d.Curvature);
        Assert.True(Math.Abs(s.Sum(v => v.S2)) < 1e-9, $"coma {s.Sum(v => v.S2)}");
        Assert.True(Math.Abs(Aberrations.ColorOf(d).Axial) < 1e-6);
        Assert.Equal(150.0, Efl(d), 9);
    }

    [Fact]
    public void ContinuationReachesFromAStartNewtonCannotTheLensNewtonFindsFromAGoodOne()
    {
        const string solve = "lsa:2, petzval:5, axial:r4, coma:p5";
        var good = Builder.Build(Spec("C HOYA:FD300 2, A air 0.5, C =1 3, F=1.853 SUMITA:K-PFK85 3, S = 3, A air", solve, 150, 4, 8), Catalog);
        var far = Spec("C HOYA:FD300 2, A air 0.5, C =1 3, F=1.853 SUMITA:K-PFK85 4, S = 2, A air", solve, 150, 4, 8);
        Assert.Throws<InvalidOperationException>(() => Builder.Build(far, Catalog));
        far.Continuation = true;
        var d = Builder.Build(far, Catalog);
        for (int k = 0; k < d.Count; k++)
        {
            Assert.Equal(good.Curvature[k], d.Curvature[k], 7);
            Assert.Equal(good.Thickness[k], d.Thickness[k], 5);
        }
    }

    [Fact]
    public void ContinuationSaysWhichThicknessRunsOut()
    {
        var spec = Spec("C HOYA:FD300 2, A air 0.5, C =1 3, F=1.853 SUMITA:K-PFK85 5, S = 1, A air", "lsa:2, petzval:5, axial:r4, coma:p5", 150, 4, 8);
        spec.Continuation = true;
        var ex = Assert.Throws<InvalidOperationException>(() => Builder.Build(spec, Catalog));
        Assert.Contains("reaches zero", ex.Message);
    }

    [Fact]
    public void AConcentricSurfaceAtTheStopIsRefused()
    {
        var ex = Assert.Throws<ArgumentException>(() => Builder.Build(Spec("C@ SCHOTT:N-BK7 1, A air"), Catalog));
        Assert.Contains("stop", ex.Message);
    }

    private static double Efl(Design d)
    {
        var (_, u) = Builder.Marginal(d.Spec, d.Indices(LensSpec.LambdaD), d.Curvature, d.Thickness);
        return -1 / u;
    }
}
