using AberrationCalculator.Core.Glass;
using Xunit;

using ShafersSimpleLensDesign.Core;

namespace ShafersSimpleLensDesign.Tests;

/// <summary>The layouts' specifications: a cover glass before the image, the back focus without it, image-side
/// telecentricity.</summary>
public class SpecificationTests
{
    private static readonly GlassCatalog Catalog = CatalogLocator.LoadBundled();

    [Fact]
    public void ACoverGlassSitsItsGapFromTheImageAndTheOrderRebuildsTheLensWithIt()
    {
        var settings = new LayoutSettings
        {
            MinElements = 2, MaxElements = 2, Starts = 2, SlabGlass = Catalog.Find("SCHOTT:N-BK7"), SlabThickness = 1.1, SlabGap = 0.5,
        };
        var b = Layouts.Run(settings).Ranked[0];
        var d = b.Design;

        // The last two surfaces are the slab's plane faces, 1.1 mm of N-BK7, its back face 0.5 mm from the paraxial image.
        Assert.Equal(0, d.Curvature[^1]);
        Assert.Equal(0, d.Curvature[^2]);
        Assert.Equal("N-BK7", d.Glass[^2]!.Name);
        Assert.Equal(1.1, d.Thickness[^2], 9);
        Assert.Equal(0.5, d.Thickness[^1], 9);
        var n = d.Indices(LensSpec.LambdaD);
        var y = Builder.MarginalHeights(n, d.Curvature, d.Thickness);
        double u = 0, yy = 1;
        for (int q = 0; q < d.Count; q++) { u = (n[q] * u - yy * (n[q + 1] - n[q]) * d.Curvature[q]) / n[q + 1]; if (q < d.Count - 1) yy += d.Thickness[q] * u; }
        Assert.Equal(0, yy + d.Thickness[^1] * u, 9);                       // the marginal ray crosses the axis at the image

        // The order, with its F=0 cover glass, builds the same lens.
        var again = Builder.Build(new LensSpec
        {
            Efl = 150, FNumber = settings.FNumber, HalfField = 8, Wavelengths = new[] { LensSpec.LambdaD }, Surfaces = Parser.Surfaces(b.Order),
        }, Catalog);
        Assert.Equal(d.Count, again.Count);
        for (int q = 0; q < d.Count; q++) Assert.Equal(d.Curvature[q], again.Curvature[q], 4);
        Assert.Equal(0.5, again.Thickness[^1], 2);                       // the order prints 6 figures: a micrometer or two
    }

    [Fact]
    public void ALimitOnTheChiefRaysAngleAtTheImageIsNamedAndMet()
    {
        var free = new LayoutSettings { MinElements = 2, MaxElements = 2, Starts = 2 };
        var layout = new Layout(new[] { "CC", "AC" }, 1);
        var b = Layouts.Optimize(free, layout)!;
        var held = new LayoutSettings { MinElements = 2, MaxElements = 2, Starts = 2, MaxChiefAngle = 0.5 };
        var why = new List<string>();
        Layouts.Evaluate(held, layout, b.X, why: why);
        Assert.Contains(why, w => w.StartsWith("chief ray angle"));

        // Within 3 degrees, if any lens of that layout can be.
        held.MaxChiefAngle = 3;
        if (Layouts.Optimize(held, layout) is { } c)
        {
            var left = new List<string>();
            Layouts.Evaluate(held, layout, c.X, why: left);
            Assert.DoesNotContain(left, w => w.StartsWith("chief ray angle"));
        }
    }

    [Fact]
    public void TheChiefRaysAngleIsSignedByWhichWayItGoesAndANegativeLimitHoldsItsSize()
    {
        var free = new LayoutSettings { MinElements = 3, MaxElements = 3, Starts = 2 };
        var r = Layouts.Run(free);
        Assert.Contains(r.Ranked, c => c.ChiefAngle > 0.5);         // the stop ahead puts the exit pupil ahead of the image
        foreach (var c in r.Ranked.Where(c => Math.Abs(c.ChiefAngle) > 0.5))
        {
            // Paraxially: going away from the axis if its slope after the last surface has the sign of its height there.
            var d = c.Design;
            var n = d.Indices(LensSpec.LambdaD);
            var (yb, ub) = Builder.ChiefRay(n, d.StopSurface, d.Thickness, d.Curvature);
            int k = d.Count - 1;
            double after = (n[k] * ub[k] - yb[k] * (n[k + 1] - n[k]) * d.Curvature[k]) / n[k + 1];
            double image = yb[k] + d.Thickness[k] * after;
            Assert.Equal(Math.Sign(after * image), Math.Sign(c.ChiefAngle));
        }

        var layout = r.Ranked.First(c => Math.Abs(c.ChiefAngle) > 5);
        foreach (double limit in new[] { 3.0, -3.0 })
        {
            var why = new List<string>();
            Layouts.Evaluate(new LayoutSettings { MaxChiefAngle = limit }, layout.Layout, layout.X, why: why);
            Assert.Contains(why, w => w.StartsWith("chief ray angle"));
        }
    }

    [Fact]
    public void NoTwoElementsAsDrawnComeCloserThanTheLeastAirEdge()
    {
        // Near-telecentric at f/8, as the lens with a steep meniscus that reached into the next element.
        var settings = new LayoutSettings { MinElements = 4, MaxElements = 4, Starts = 2, FNumber = 8, MaxChiefAngle = 3, MinAirEdge = 0.005 };
        var r = Layouts.Run(settings);
        Assert.NotEmpty(r.Ranked);
        foreach (var c in r.Ranked)
        {
            // Measured on the drawing: each glass outline is its front face from −h to h (41 points), then its back face back.
            var regions = LensDrawing.Of(c.Design, settings.HalfField).Regions.Select(g => g.Outline).ToList();
            for (int i = 0; i + 1 < regions.Count; i++)
            {
                var back = regions[i].Skip(41).Reverse().ToList();
                var front = regions[i + 1].Take(41).ToList();
                if (Math.Abs(back[20].Z - front[20].Z) < 1e-9) continue;        // cemented
                double reach = Math.Min(back[^1].Y, front[^1].Y);
                for (int k = 0; k <= 20; k++)
                {
                    double y = reach * k / 20;
                    double gap = At(front, y) - At(back, y);
                    Assert.True(gap >= 0.005 * settings.Efl - 2e-3, $"{c.Layout}: {gap:F3} mm between elements at {y:F1} mm from the axis");
                }
            }
        }

        // The face's z at height y, between its points.
        static double At(List<Pt> face, double y)
        {
            int j = Math.Clamp(face.FindIndex(p => p.Y >= y), 1, face.Count - 1);
            var (a, b) = (face[j - 1], face[j]);
            return a.Z + (b.Z - a.Z) * (y - a.Y) / (b.Y - a.Y);
        }
    }
}

public class DrawingTests
{
    [Fact]
    public void TheDrawingHasAnOutlinePerRunOfGlassAndRaysThatReachTheImage()
    {
        var catalog = CatalogLocator.LoadBundled();
        var spec = new LensSpec { Efl = 150, FNumber = 5.6, HalfField = 8, Surfaces = Parser.Surfaces("C SCHOTT:N-LAF2 4, C SCHOTT:N-BK7 3, S SCHOTT:N-BK7 3, A air") };
        var d = Builder.Build(spec, catalog);
        var drawing = LensDrawing.Of(d, 8);
        Assert.Equal(3, drawing.Regions.Count);                        // N-LAF2, then N-BK7 either side of the stop
        Assert.Equal(9, drawing.Rays.Count);                           // three fields, three rays each
        double image = d.Thickness.Sum();
        Assert.All(drawing.Rays, r => Assert.Equal(image, r.Points[^1].Z, 9));
        // The axial field's edge ray lands where the builder's own exact trace puts it.
        var n = d.Indices(LensSpec.LambdaD);
        var exact = Meridional.Trace(n, d.Thickness, d.Curvature, 150 / (2 * 5.6));
        double landing = exact.Y + exact.M / exact.N * (d.Thickness[^1] - exact.Z);
        var edge = drawing.Rays.Where(r => r.Field == 0).Select(r => r.Points[^1].Y).Max();
        Assert.Equal(Math.Abs(landing), edge, 6);                     // the rays above and below the axis land either side
    }
}
