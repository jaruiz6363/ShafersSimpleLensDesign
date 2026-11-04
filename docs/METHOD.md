# Shafer's Simple Lens Design: the method

This is how the app finds its lenses, and what has been learned using it. For using the app see
[USER_GUIDE.md](USER_GUIDE.md).

## Shafer's method

David Shafer, "Simple method for designing lenses", Proc. SPIE 237, 1980 International Lens Design Conference. The
paper is cited here, not included.

Every surface that controls the monochromatic aberrations is one of two kinds:

- **Concentric about the chief ray (C).** The surface is centered where the chief ray crosses the axis, so the chief
  ray passes through it undeviated. It adds no coma, astigmatism or distortion; it adds spherical aberration and
  Petzval curvature. Its curvature is −ū/ȳ, from the paraxial chief ray's slope ū and height ȳ.
- **Aplanatic for the marginal ray (A).** The surface works at Abbe's aplanatic conjugates. It adds no spherical
  aberration, coma or astigmatism; it adds Petzval curvature and distortion, and changes the beam's convergence by the
  index ratio. Its curvature is −(n + n′)u/(n y). For a collimated beam it is flat.

So every lens built this way is free of coma and astigmatism by construction. What is left, spherical aberration,
Petzval curvature and distortion, is corrected by the distances in the lens. The radii are not free: each follows from
its rule and the paraxial rays. Surfaces ahead of the stop depend on the ones after them, so the radii are solved
together, by Newton's method, as the fixed point of the rules.

### Color: buried surfaces

Color is corrected by **buried surfaces**: cemented surfaces between two glasses of nearly one index and different
dispersions. With the indices equal they do nothing at the design wavelength, so the monochromatic correction is
left alone.

- **C~**, concentric about the chief ray: Ā = 0, so it changes axial color only, by −H (y/ȳ) Δ(δn/n). y/ȳ runs along
  the element, so where the surface sits sets how much.
- **M~**, centered on the marginal ray's axial point: A = 0, so it changes lateral color only, by H Δ(δn/n), wherever it
  sits in the element.

Here CL = Σ A y Δ(δn/n) and CT = Σ Ā y Δ(δn/n) are Seidel's axial and lateral color sums, and H is the Lagrange
invariant.

## The search

### Every layout, with model glasses

The app tries every layout of air-spaced singlets from the fewest to the most elements. Each element is CC, CA, AC or
AA (front and back surface), and the stop goes in any air space: 3 · 4^(N−1) · (N + 1) layouts for N elements, 5,802
for one to five. An element AA first is left out: an aplanatic surface in the collimated beam is flat, and so is the
next, so it would be a plane plate that only adds length.

Each layout is optimized with **model glasses**, an index and no dispersion, at the design wavelength. The variables
are the thicknesses, the air spaces and the indices; the radii never vary, so every result is still a Shafer lens. The
optimizer is Levenberg-Marquardt on the real-ray RMS spot at best focus over three fields, from several random starts.

The limits (distortion, length, back focus, chief ray angle, center and edge thicknesses, air spaces on the axis and
across the elements' shared height) are least-squares penalties in millimeters. While a lens ends outside them, they
are made ten times heavier, up to four times. Damping has a floor, so a variable that has stopped mattering, such as
an index at its bound, cannot freeze the optimizer. With both, 96% of colored candidates end within the limits, up from
21% without.

Each element's rim is the height of the highest of nine real rays (three fields, the pupil's edges and center) plus 4%,
and every face of an element, cemented pieces included, is cut to that rim, as the app draws it.

### Real glasses and color

The best layouts (50 by default, and the best of each size) are then given catalog glasses:

1. **One element is split by a C~ surface**, at most one other by an **M~**, and the rest take single glasses within
   0.03 of their model index. Split elements take pairs of glasses within 0.01 in index and at least 10 apart in Abbe
   number.
2. **A paraxial screen.** With the geometry fixed, paraxial color is linear in the glasses' dispersions, so every choice
   of glasses is screened in microseconds: hundreds of millions per run. The C~ surface's place is solved in closed form
   for no axial color, and the choice is judged by the lateral color and secondary spectrum left over.
3. **A real-ray polish.** The best 16 choices for each layout are optimized by real rays over all the wavelengths,
   weighted, with the radii still from the rules and the same limits.

With one wavelength there is no color to correct: each element takes the single glass nearest its model index.

## What has been learned

Measured at 150 mm, ±8°, F, d and C, over SCHOTT, OHARA, HOYA, SUMITA and CDGM, during development (November 2026).
Later changes to the limits, such as the edge clearance, move these figures a little.

**With model glasses**, at f/6, d line (about two minutes, all layouts of one to five elements):

| Elements | Best layout | RMS spot µm at best focus (0 / 0.7 / 1 field) |
|---|---|---|
| 1 | S AC | 37.8 / 30.8 / 37.9 |
| 2 | CC S AC | 8.4 / 5.6 / 9.6 |
| 3 | CC S AC AA | 3.1 / 3.2 / 3.9 |
| 4 | S CC AC AC AA | 0.40 / 0.54 / 0.70 |

The Airy radius at f/6 is 4.3 µm: three elements are diffraction limited with model glasses, four have a tenth of it.

**With real glasses**, f/5.6 default run (about 70 s in all): the best lens is CA CC S AA AC, 4.9 µm RMS, 1.2 times the
4.0 µm Airy radius.

**Color enough layouts.** How sharp a layout is at one wavelength does not predict how well it takes color: at f/6,
coloring the best 10 layouts found 5.1 µm, the best 50 found 2.8 µm, and the best 200 nothing better.

**Cap the model index.** With indices up to 1.95, the monochromatic optimum puts every element in dense flint
(Vd 17–32). That leaves millimeters of axial color, more than one C~ surface can take out, and the best colored lens was
28 µm. Caps from 1.70 to 1.85 all gave 3.8–5 µm with the best 10 layouts colored.

**Common glasses cost little.** Keeping to glasses their maker marks preferred or melts most often leaves 457 of 974;
at f/6 the best lens went from 2.29 / 2.92 / 3.10 µm to 2.45 / 2.99 / 3.30 µm.

**Fast lenses meet the secondary spectrum.** At f/4 (Airy radius 2.9 µm) the best monochromatic lens is 2.6 µm and the
best colored one 6.5 µm. What is left is the secondary spectrum of ordinary glasses: F − C is −70 µm across the pupil,
and d falls about 80 µm from F and C together. Choosing the glasses again for the optimized thicknesses
(`--color-rounds`), admitting glasses of anomalous partial dispersion from further off their model index, and weighting
the secondary spectrum more in the screen each gained little or nothing. Real glasses cost nothing at the d line; the
limit at f/4 is that the sharpest layouts at one wavelength are not the ones that take color. This is why f/5.6 is the
default.

## Checks

- **Every surface does its job.** For every lens, AberrationCalculator's Seidel sums surface by surface show no coma,
  astigmatism or distortion at each concentric surface and no spherical aberration, coma or astigmatism at each
  aplanatic one (`shafer-lens build` prints them).
- **Shafer's figures.** The rules reproduce the paper: Figure 1's two radii are equal at n = 1.618, the golden ratio
  (Shafer's "1.62"), and the targets of Figures 2, 3, 5, 6 and 7 are met.
- **The fast trace.** The search's own skew ray trace lands rays where AberrationCalculator's does, to 1e-9 mm, and
  its spot merit is within 5% of AberrationCalculator's analysis at best focus.
- **The geometry.** Tests check that no two elements as drawn come closer than the least air edge, and that a negative
  chief ray limit holds the same as a positive one.

The tests run on Windows and Linux on every change (`dotnet test tests/ShafersSimpleLensDesign.Tests`).

## AberrationCalculator

The optics underneath (lens model, glass catalogs, paraxial and exact ray traces, Seidel coefficients, and the writers
for every lens format) are AberrationCalculator's, carried as a git subtree in `external/AberrationCalculator`. This
app adds what is its own: building lenses from Shafer's rules, the layout search and the color stage.
