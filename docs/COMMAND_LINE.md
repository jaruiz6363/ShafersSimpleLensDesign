# Shafer's Simple Lens Design: command line

`shafer-lens.exe` is installed beside the app. It does what the app does, for scripts and batch runs, and it can
rebuild one lens from its surface order. The installer can add it to PATH; otherwise run it from the install folder.

```
shafer-lens layouts [options]                        every layout of air-spaced singlets, then real glasses and color
shafer-lens build  --order "<surfaces>" [options]    build one lens from its surface order (as the app prints it)
shafer-lens --help
```

Options are `--name value`; a name alone is a flag. Numbers use a decimal point. Check the spelling of options: one
the program does not know is ignored.

---

## layouts

```
shafer-lens layouts --efl 150 --fno 5.6 --hfov 8 --color 50 --out results
```

The same search as **Run** in the app: every layout of 1 to `--elements` air-spaced singlets optimized with model
glasses at the design wavelength, then, with `--color N`, the best N layouts (and the best of each size) given real
glasses and their color corrected.

**Unlike the app, the limits are in focal lengths, not millimeters, and the color stage is off unless `--color` is
given.** At 150 mm, `--min-bfl 0.1` is 15 mm.

### The lens and the layouts

| Option | Default | Meaning |
|---|---|---|
| `--efl` | 150 | Focal length. |
| `--fno` | 5.6 | F-number. |
| `--hfov` | 8 | Half field, degrees. |
| `--lambda` | 0.5875618 | Design wavelength, µm. Given a list, the middle one. |
| `--min-elements`, `--elements` | 1, 5 | Fewest and most elements. |
| `--index` | 1.45,1.80 | Model glass index range. |
| `--starts` | 4 | Random starts per layout. |
| `--top` | 50 | Layouts listed and written to the CSV. |

### The limits

| Option | Default | Meaning |
|---|---|---|
| `--max-distortion` | 1 | Percent, at full field. |
| `--max-length` | 2 | First vertex to image, focal lengths. |
| `--min-bfl` | 0.1 | Back focus, the air path, focal lengths. |
| `--min-center` | 0.01 | Element center thickness, focal lengths. |
| `--min-edge` | 0.005 | Element edge thickness, focal lengths. |
| `--min-air` | 0.005 | Air space on the axis, focal lengths. |
| `--min-air-edge` | 0.005 | Air space between elements across their shared height, focal lengths. |
| `--max-cra` | off | Largest chief ray angle at the image, degrees, either sign. |
| `--cover GLASS,T[,GAP]` | none | A cover glass: `SCHOTT:N-BK7,1.1,0.5` is 1.1 mm of N-BK7, its back face 0.5 mm (the default) from the image. Thickness and gap in mm. |

### Real glasses and color

| Option | Default | Meaning |
|---|---|---|
| `--color N` | off | Give the best N layouts, and the best of each size, real glasses. N is required: `--color 50`. |
| `--color-lambda` | 0.4861327,0.5875618,0.6562725 | Wavelengths for color, µm. One wavelength gives each element the nearest single glass, no buried surfaces. |
| `--vendors` | SCHOTT,OHARA,HOYA,SUMITA,CDGM | Catalogs to choose from. |
| `--common` | off | Common glasses only (preferred, or melt frequency 1 or 2). |
| `--glass-tol` | 0.03 | A real glass's index may differ from the model glass's by this much. |
| `--buried-tol` | 0.01 | The two glasses either side of a buried surface may differ in index by this much. |
| `--abbe-split` | 10 | ... and must differ in Abbe number by at least this much. |
| `--color-keep` | 16 | Glass choices optimized by real rays per layout. |
| `--color-rounds` | 1 | Rounds of choosing glasses. Later rounds choose again for the thicknesses each lens was optimized to; slower, and at f/4 little better. |

### Output

| Option | Default | Meaning |
|---|---|---|
| `--out` | results | Output folder. |
| `--export` | 5 | Lenses written: the best N layouts and the best of each size, and with `--color`, the best N colored lenses. |
| `--catalogs` | the installed catalogs | A folder of `.agf` glass catalogs. |

Written to `--out`:

| File | What |
|---|---|
| `layouts.csv` | The ranked layouts with model glasses: spot by field, distortion, length, indices, surface order. |
| `rank001_<layout>\`, `best3_<layout>\` | The exported model-glass lenses, six formats each. |
| `color\color.csv` | Every lens with real glasses, ranked: spot, color, distortion, length, back focus, chief ray angle, glasses, surface order. |
| `color\color001_<layout>\` | The exported lenses with real glasses. |

The console also prints each exported lens's spot as traced by AberrationCalculator, as a check on the search's own
trace.

### Examples

```
shafer-lens layouts --color 50
shafer-lens layouts --fno 8 --hfov 10 --max-cra 3 --cover SCHOTT:N-BK7,1.1 --color 50 --out f8-telecentric
shafer-lens layouts --elements 4 --color 10 --color-lambda 0.5875618 --out mono
shafer-lens layouts --elements 6 --min-elements 6 --color 50 --out six
```

---

## build

```
shafer-lens build --order "S air 29.0, A SUMITA:K-LaFK50 39.6, C air 29.8, ..." --efl 150 --fno 5.6 --hfov 8 --export lens
```

Builds one lens from its surface order: the text under the drawing in the app, the `order` column of the CSVs, or one
written by hand. Each radius follows from its role, so the order needs no radii. It prints the prescription, the Seidel
sums surface by surface (each concentric surface should show no coma, astigmatism or distortion, each aplanatic one no
spherical aberration, coma or astigmatism), and the real-ray spots, and with `--export` writes the lens files.

Give the same `--efl`, `--fno` and `--hfov` as the run the order came from: the thicknesses are scaled to the focal
length.

### The surface order

Surfaces separated by commas, each `ROLE GLASS THICKNESS`:

| Part | Values |
|---|---|
| ROLE | `C` concentric about the chief ray; `A` aplanatic for the marginal ray; `M` centered on the marginal ray's axial point; `S` a stop plane; `F=radius` a fixed radius (`F=0` flat); `B` a buried surface whose radius is solved. A trailing `~` marks a buried surface (`C~`, `M~`); a trailing `@` puts the stop on that surface. |
| GLASS | The medium after the surface: `CATALOG:NAME`, `MODEL:index` (no dispersion), `air`, `=` (the glass before again) or `=j` (the glass after surface j). |
| THICKNESS | To the next surface; omit on the last. |

Shafer's Figure 1: `--order "C SCHOTT:N-SK16 1, A@ air"`.

### Options

| Option | Default | Meaning |
|---|---|---|
| `--order` | (required) | The surface order. |
| `--efl`, `--fno`, `--hfov` | 100, 8, 3 | Focal length, f-number, half field in degrees. |
| `--lambda` | 0.4861,0.5876,0.6563 | Wavelengths, µm; the middle one is the design wavelength. |
| `--solve "petzval:2, lsa:1"` | none | Solve aberrations, each by the thickness after the surface named: `petzval`, `spherical` (third order), `lsa` (the real marginal ray to the paraxial focus), `coma`, `astigmatism`, `distortion`, `axial`, `lateral`. `rN` varies the radius of buried surface N instead. |
| `--sa-zone` | 1 | The pupil zone `lsa` brings to focus (0.85 balances better). |
| `--continuation` | off | If the targets cannot be solved directly, reach them in steps. |
| `--export <folder>` | none | Write the lens in the six formats. |
| `--catalogs` | the installed catalogs | A folder of `.agf` glass catalogs. |

### Exit codes

0 done; 1 (`layouts`) nothing came within the limits; 2 a mistake in the options or the order (the message says
which).
