# Building Shafer's Simple Lens Design

## You do not need to build it

Ready-made programs for Windows, macOS and Linux are attached to every release on the
[Releases page](https://github.com/jaruiz6363/ShafersSimpleLensDesign/releases):

| File | System |
|---|---|
| `ShafersSimpleLensDesign-Setup-<version>.exe` | Windows 10 and 11, x64: installer |
| `ShafersSimpleLensDesign-<version>-macOS-arm64.zip` | macOS 11 or later on Apple Silicon: `ShafersSimpleLensDesign.app` |
| `ShafersSimpleLensDesign-<version>-x86_64.AppImage` | Linux x86_64: one file, no installation |
| `SHA256SUMS.txt` | Checksums of the three |

Each contains the app, the command line `shafer-lens`, the glass catalogs and these documents. The .NET runtime is
included, so nothing else needs installing. The [user guide](USER_GUIDE.md#1-installing-and-starting) explains how to
install each one.

Build from source to change the program, or to run it on a system without a ready-made package.

## What you need

- Either of:
  - on Windows, **Visual Studio 2022**, version 17.8 or later, with the ".NET desktop development" workload;
  - on Windows, macOS or Linux, the **.NET 8 SDK** or newer.
- **git**.

Nothing else needs fetching. AberrationCalculator, which supplies the optics underneath (lens model, glass catalogs,
ray traces, Seidel coefficients and the writers for every lens format), is part of this repository: a `git subtree`
in `external/AberrationCalculator`. There are no machine-specific paths, and a plain clone builds as it is. The
NuGet packages (Avalonia, CommunityToolkit.Mvvm, xUnit) are restored on the first build.

## Get the source

```
git clone https://github.com/jaruiz6363/ShafersSimpleLensDesign.git
cd ShafersSimpleLensDesign
```

## Build in Visual Studio

1. Open `ShafersSimpleLensDesign.sln`.
2. The app, **ShafersSimpleLensDesign**, is the startup project. For the command line, right-click
   **ShafersSimpleLensDesign.Cli** and choose **Set as Startup Project**; give it arguments under
   **Debug > ShafersSimpleLensDesign.Cli Debug Properties**, for example `layouts --elements 3 --color 10`.
3. Choose **Release** for full speed (the search is several times slower in Debug), then build and run (F5 or
   Ctrl+F5).
4. **Test > Run All Tests** runs the tests (about a minute).

## Build from the command line

On Windows, macOS or Linux:

```
dotnet build ShafersSimpleLensDesign.sln -c Release
dotnet test tests/ShafersSimpleLensDesign.Tests -c Release
dotnet run --project src/ShafersSimpleLensDesign -c Release                  the app
dotnet run --project src/ShafersSimpleLensDesign.Cli -c Release -- --help    the command line
```

The glass catalogs are copied beside the programs by the build, from `external/AberrationCalculator/catalogs/Glass`.

## Projects

| Project | What |
|---|---|
| `src/ShafersSimpleLensDesign` | The app (Avalonia 11, MVVM). `Views/LensView.cs` draws the lens. |
| `src/ShafersSimpleLensDesign.Core` | The library: Shafer's rules (`Builder`), the layout search (`Layouts`), the color stage (`LayoutColor`), the fast ray trace and spot merit (`SkewTrace`), the drawing (`LensDrawing`), export (`Exporters`). |
| `src/ShafersSimpleLensDesign.Cli` | The command line, `shafer-lens` ([COMMAND_LINE.md](COMMAND_LINE.md)). |
| `tests/ShafersSimpleLensDesign.Tests` | xUnit tests: the rules against AberrationCalculator's Seidel sums, the trace, the search's limits, the color stage, the geometry. |
| `external/AberrationCalculator` | AberrationCalculator, as a git subtree. |

## AberrationCalculator

AberrationCalculator is carried as a `git subtree`, so a clone has it with no extra step. To take a newer version:

```
git subtree pull --prefix external/AberrationCalculator https://github.com/jaruiz6363/AberrationCalculator.git main --squash
```

Then build and run the tests.

## A stand-alone copy for another system

`dotnet publish` makes a folder that runs without .NET installed:

```
dotnet publish src/ShafersSimpleLensDesign -c Release -r linux-x64 --self-contained -o publish
dotnet publish src/ShafersSimpleLensDesign.Cli -c Release -r linux-x64 --self-contained -o publish
```

Runtimes: `win-x64`, `linux-x64`, `osx-arm64` (Apple Silicon) or `osx-x64` (Intel Mac). Copy `docs` beside the
programs for Help > User Guide. On macOS a bare folder runs from Terminal; the release's `.app` bundle and its
signing are made by the release scripts.

## The release packages

The installer, the AppImage and the macOS app are built from a tagged commit by separate packaging scripts, which
publish the programs self-contained for each system, test them on each (the same search must find the same lens and
export all six formats), and package them.
