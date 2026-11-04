using ShafersSimpleLensDesign.Core;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using AberrationCalculator.Core.Glass;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShafersSimpleLensDesign.ViewModels;

public partial class WavelengthRow : ObservableObject
{
    [ObservableProperty] private double _micrometers;
    [ObservableProperty] private double _weight = 1.0;
}

public partial class CheckItem : ObservableObject
{
    public string Name { get; init; } = "";
    [ObservableProperty] private bool _isSelected;
}

/// <summary>One row of the table of lenses with real glasses.</summary>
public sealed class ColorRow
{
    public required ColorCandidate Candidate { get; init; }
    public int Rank => Candidate.Rank;
    public string Layout => Candidate.Layout.ToString();
    public int Elements => Candidate.Layout.Elements;
    public double Rms => Candidate.Rms * 1000;
    /// <summary>The Airy radius at the primary wavelength, µm, and the RMS spot as a multiple of it.</summary>
    public required double Airy { get; init; }
    public double RmsPerAiry => Rms / Airy;
    public double Rms0 => Candidate.FieldRms[0] * 1000;
    public double Rms07 => Candidate.FieldRms[1] * 1000;
    public double Rms1 => Candidate.FieldRms[2] * 1000;
    public double Axial => Candidate.AxialColor * 1000;
    public double Lateral => Candidate.LateralColor * 1000;
    public double Distortion => Candidate.Distortion * 100;
    public double Length => Candidate.Length;
    public double BackFocus => Candidate.BackFocus;
    public double ChiefAngle => Candidate.ChiefAngle;
    public string Glasses => Candidate.Glasses;
}

/// <summary>One row of the table of layouts with model glasses, at the design wavelength.</summary>
public sealed class LayoutRow
{
    public required LayoutCandidate Candidate { get; init; }
    public int Rank => Candidate.Rank;
    public string Layout => Candidate.Layout.ToString();
    public int Elements => Candidate.Layout.Elements;
    public double Rms => Candidate.Rms * 1000;
    /// <summary>The Airy radius at the primary wavelength, µm, and the RMS spot as a multiple of it.</summary>
    public required double Airy { get; init; }
    public double RmsPerAiry => Rms / Airy;
    public double Rms0 => Candidate.FieldRms[0] * 1000;
    public double Rms07 => Candidate.FieldRms[1] * 1000;
    public double Rms1 => Candidate.FieldRms[2] * 1000;
    public double Distortion => Candidate.Distortion * 100;
    public double Length => Candidate.Length;
    public double BackFocus => Candidate.BackFocus;
    public double ChiefAngle => Candidate.ChiefAngle;
    public string Indices => string.Join(" ", Candidate.Indices.Select(v => v.ToString("F3", CultureInfo.InvariantCulture)));
}

/// <summary>
/// The specification and search settings as the window edits them, in millimeters and degrees, and the last result: the
/// layouts with model glasses at the design wavelength, then the best of them with real glasses and their color corrected.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    // ── Lens ────────────────────────────────────────────────────────────────────
    [ObservableProperty] private double _focalLength = 150;
    [ObservableProperty] private double _fNumber = 5.6;
    [ObservableProperty] private double _halfField = 8;

    /// <summary>The wavelengths and their weights, as MonoSearch's table; the primary row is the design wavelength, at
    /// which the layouts are found with model glasses. Color is corrected over them all.</summary>
    public ObservableCollection<WavelengthRow> Wavelengths { get; } = new()
    {
        new WavelengthRow { Micrometers = LensSpec.LambdaF }, new WavelengthRow { Micrometers = LensSpec.LambdaD },
        new WavelengthRow { Micrometers = LensSpec.LambdaC },
    };
    [ObservableProperty] private WavelengthRow? _selectedWavelength;
    [ObservableProperty] private int _primaryWavelength = 2;

    [RelayCommand]
    private void AddWavelength()
    {
        var row = new WavelengthRow { Micrometers = 0.55 };
        Wavelengths.Add(row);
        SelectedWavelength = row;
    }

    [RelayCommand]
    private void RemoveWavelength()
    {
        if (SelectedWavelength is null || Wavelengths.Count <= 1) return;
        int at = Wavelengths.IndexOf(SelectedWavelength);
        var primary = PrimaryWavelength >= 1 && PrimaryWavelength <= Wavelengths.Count ? Wavelengths[PrimaryWavelength - 1] : null;
        Wavelengths.RemoveAt(at);
        PrimaryWavelength = primary is not null && Wavelengths.Contains(primary) ? Wavelengths.IndexOf(primary) + 1 : Math.Min(PrimaryWavelength, Wavelengths.Count);
    }

    /// <summary>Sorts the rows by wavelength, the primary following its row.</summary>
    [RelayCommand]
    private void SortWavelengths()
    {
        var primary = PrimaryWavelength >= 1 && PrimaryWavelength <= Wavelengths.Count ? Wavelengths[PrimaryWavelength - 1] : null;
        var sorted = Wavelengths.OrderBy(w => w.Micrometers).ToList();
        Wavelengths.Clear();
        foreach (var w in sorted) Wavelengths.Add(w);
        if (primary is not null) PrimaryWavelength = Wavelengths.IndexOf(primary) + 1;
    }

    // ── Specifications ──────────────────────────────────────────────────────────
    [ObservableProperty] private double _minBackFocus = 15;
    [ObservableProperty] private double _maxLength = 300;
    [ObservableProperty] private double _maxDistortion = 1;
    [ObservableProperty] private bool _limitChiefAngle;
    [ObservableProperty] private double _maxChiefAngle = 10;
    [ObservableProperty] private bool _coverGlass;
    [ObservableProperty] private string _coverGlassName = "SCHOTT:N-BK7";
    [ObservableProperty] private double _coverThickness = 1.1;
    [ObservableProperty] private double _coverGap = 0.5;

    // ── Construction ────────────────────────────────────────────────────────────
    [ObservableProperty] private int _minElements = 1;
    [ObservableProperty] private int _maxElements = 5;
    [ObservableProperty] private double _indexMin = 1.45;
    [ObservableProperty] private double _indexMax = 1.80;
    [ObservableProperty] private double _minCenter = 1.5;
    [ObservableProperty] private double _minEdge = 0.75;
    [ObservableProperty] private double _minAir = 0.75;
    [ObservableProperty] private double _minAirEdge = 0.75;
    [ObservableProperty] private int _starts = 4;

    // ── Color ──────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _correctColor = true;
    [ObservableProperty] private int _colorLayouts = 50;
    [ObservableProperty] private bool _commonGlasses;
    public ObservableCollection<CheckItem> Vendors { get; } =
        new(GlassSet.DefaultCatalogs.Select(c => new CheckItem { Name = c, IsSelected = true }));

    // ── Export ──────────────────────────────────────────────────────────────────
    [ObservableProperty] private int _exportCount = 3;
    [ObservableProperty] private string _outputFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ShafersSimpleLensDesign", "results");

    // ── State ───────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _statusText = "Set the specification and press Run.";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(CancelCommand))] private bool _isRunning;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanExport))] private bool _hasResults;
    public bool CanExport => HasResults && !IsRunning;

    public ObservableCollection<ColorRow> ColorResults { get; } = new();
    public ObservableCollection<LayoutRow> LayoutResults { get; } = new();
    [ObservableProperty] private ColorRow? _selectedColor;
    [ObservableProperty] private LayoutRow? _selectedLayout;
    [ObservableProperty] private int _resultsTab;
    /// <summary>Every choice of glasses found, or (the default) the best of each layout only.</summary>
    [ObservableProperty] private bool _allGlassChoices;
    private List<ColorCandidate> _allColor = new();
    [ObservableProperty] private LensDrawing? _drawing;
    [ObservableProperty] private string _selectedText = "";

    private GlassCatalog? _catalog;
    private CancellationTokenSource? _cancel;
    private LayoutSettings? _lastSettings;
    /// <summary>The last run's Airy radius, 1.22 λ N at the primary wavelength, µm.</summary>
    private double _airy = 1;

    public string Title => "Shafer's Simple Lens Design";

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(CanExport));
    partial void OnAllGlassChoicesChanged(bool value) => FillColor();

    /// <summary>The table of lenses with real glasses: the best of each layout, or every choice of glasses (the best 500).</summary>
    private void FillColor()
    {
        ColorResults.Clear();
        var rows = AllGlassChoices ? _allColor.Take(500) : _allColor.GroupBy(c => c.Layout.ToString()).Select(g => g.First());
        foreach (var c in rows) ColorResults.Add(new ColorRow { Candidate = c, Airy = _airy });
    }
    partial void OnSelectedColorChanged(ColorRow? value) { if (value is not null) Show(value.Candidate.Design, Describe(value.Candidate)); }
    partial void OnSelectedLayoutChanged(LayoutRow? value) { if (value is not null) Show(value.Candidate.Design, Describe(value.Candidate)); }

    private void Show(Design d, string text)
    {
        try { Drawing = LensDrawing.Of(d, HalfField); }
        catch (Exception) { Drawing = null; }
        SelectedText = text;
    }

    private string Describe(ColorCandidate c) =>
        $"{c.Layout}   RMS spot {c.Rms * 1000:F2} µm, {c.Rms * 1000 / _airy:F2} × the Airy radius {_airy:F2} µm ({string.Join(" / ", c.FieldRms.Select(v => (v * 1000).ToString("F2")))} at 0 / 0.7 / 1 field), "
        + $"axial color {c.AxialColor * 1000:F1} µm, lateral {c.LateralColor * 1000:F2} µm, distortion {c.Distortion * 100:F2}%, "
        + $"length {c.Length:F1} mm, back focus {c.BackFocus:F1} mm, chief ray {c.ChiefAngle:F1}° at the image\n{c.Glasses}\n{c.Order}";

    private string Describe(LayoutCandidate c) =>
        $"{c.Layout}   RMS spot {c.Rms * 1000:F2} µm, {c.Rms * 1000 / _airy:F2} × the Airy radius {_airy:F2} µm ({string.Join(" / ", c.FieldRms.Select(v => (v * 1000).ToString("F2")))} at 0 / 0.7 / 1 field) "
        + $"at the design wavelength, model glasses {string.Join(" ", c.Indices.Select(v => v.ToString("F3")))}, distortion {c.Distortion * 100:F2}%, "
        + $"length {c.Length:F1} mm, back focus {c.BackFocus:F1} mm, chief ray {c.ChiefAngle:F1}° at the image\n{c.Order}";

    private GlassCatalog Catalog => _catalog ??= CatalogLocator.LoadBundled();

    /// <summary>The settings for the search, from the window's: lengths in millimeters become fractions of the focal length.</summary>
    private LayoutSettings Settings()
    {
        double f = FocalLength;
        if (!(f > 0)) throw new ArgumentException("The focal length must be positive.");
        if (!(FNumber > 0)) throw new ArgumentException("The f-number must be positive.");
        if (MinElements < 1 || MaxElements < MinElements || MaxElements > 6) throw new ArgumentException("Elements: from 1 to at most 6, the least first.");
        if (!(IndexMin >= 1.3 && IndexMax > IndexMin)) throw new ArgumentException("The model glass index range must rise from at least 1.3.");
        if (Wavelengths.Count == 0) throw new ArgumentException("Give at least one wavelength.");
        if (Wavelengths.Any(w => !(w.Micrometers > 0.2 && w.Micrometers < 3)))
            throw new ArgumentException("Every wavelength must be in micrometers, between 0.2 and 3.");
        if (Wavelengths.Any(w => !(w.Weight >= 0)) || Wavelengths.All(w => w.Weight == 0)) throw new ArgumentException("Weights must be 0 or more, not all 0.");
        if (PrimaryWavelength < 1 || PrimaryWavelength > Wavelengths.Count)
            throw new ArgumentException($"The primary wavelength is a row number, 1 to {Wavelengths.Count}.");
        double design = Wavelengths[PrimaryWavelength - 1].Micrometers;
        var rows = Wavelengths.OrderBy(w => w.Micrometers).ToArray();
        var s = new LayoutSettings
        {
            Efl = f, FNumber = FNumber, HalfField = HalfField, Wavelength = design,
            ColorWavelengths = rows.Select(w => w.Micrometers).ToArray(), ColorWeights = rows.Select(w => w.Weight).ToArray(),
            MinElements = MinElements, MaxElements = MaxElements, IndexMin = IndexMin, IndexMax = IndexMax, Starts = Math.Max(1, Starts),
            MaxDistortion = MaxDistortion / 100, MaxLength = MaxLength / f, MinBackFocus = MinBackFocus / f,
            MinCenter = MinCenter / f, MinEdge = MinEdge / f, MinAirCenter = MinAir / f, MinAirEdge = MinAirEdge / f,
            MaxChiefAngle = LimitChiefAngle ? MaxChiefAngle : 0,
            ColorLayouts = CorrectColor ? ColorLayouts : 0,
            Catalogs = Vendors.Where(v => v.IsSelected).Select(v => v.Name).ToArray(),
            Top = 200,
        };
        if (CorrectColor && s.Catalogs.Length == 0) throw new ArgumentException("Select at least one glass vendor.");
        if (CoverGlass)
        {
            s.SlabGlass = Catalog.Find(CoverGlassName.Trim())
                          ?? throw new ArgumentException($"Cover glass '{CoverGlassName}' not found: name it CATALOG:NAME, e.g. SCHOTT:N-BK7.");
            s.SlabThickness = CoverThickness;
            s.SlabGap = CoverGap;
        }
        if (CommonGlasses && CatalogLocator.Find() is { } folder)
        {
            var melt = GlassSet.MeltFrequencies(folder);
            s.GlassFilter = g => GlassSet.IsCommon(g, melt);
        }
        return s;
    }

    private bool CanRun() => !IsRunning;
    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        LayoutSettings settings;
        try { settings = Settings(); }
        catch (ArgumentException ex) { StatusText = ex.Message; return; }

        IsRunning = true;
        Progress = 0;
        // Nothing of the last run stays on view while this one works.
        _allColor = new();
        SelectedColor = null;
        SelectedLayout = null;
        ColorResults.Clear();
        LayoutResults.Clear();
        Drawing = null;
        SelectedText = "";
        SummaryText = "";
        HasResults = false;
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        var clock = Stopwatch.StartNew();
        _airy = 1.22 * settings.Wavelength * settings.FNumber;
        bool color = settings.ColorLayouts > 0;
        var monoProgress = new Progress<double>(p => Progress = 100 * (color ? 0.75 : 1) * p);
        var colorProgress = new Progress<double>(p => Progress = 75 + 25 * p);
        try
        {
            int count = Layouts.All(settings.MinElements, settings.MaxElements).Count();
            StatusText = $"Optimizing {count:N0} layouts with model glasses at {settings.Wavelength} µm...";
            var r = await Task.Run(() => Layouts.Run(settings, monoProgress, token), token);
            foreach (var c in r.Ranked) LayoutResults.Add(new LayoutRow { Candidate = c, Airy = _airy });
            string summary = $"{r.Layouts:N0} layouts, {r.Feasible:N0} within the limits ({r.Elapsed.TotalSeconds:F0} s). Best with model glasses: "
                             + string.Join(", ", r.BestByElements.Select(kv => $"{kv.Key} el {kv.Value.Rms * 1000:F2} µm"));
            if (color && r.Ranked.Count > 0)
            {
                var bases = r.Ranked.Take(settings.ColorLayouts).Concat(r.BestByElements.Values).Distinct().ToList();
                StatusText = settings.ColorWavelengths.Distinct().Count() > 1
                    ? $"Giving the best {bases.Count} layouts real glasses and correcting their color..."
                    : $"Giving the best {bases.Count} layouts real glasses (one wavelength: the nearest single glasses)...";
                var cr = await Task.Run(() => LayoutColor.Run(settings, bases, Catalog, colorProgress, token), token);
                _allColor = cr.Ranked;
                FillColor();
                summary += $"\nReal glasses: {cr.Usable} glasses, {cr.Screened:N0} choices screened, {cr.Ranked.Count} lenses within the limits "
                           + $"({cr.Elapsed.TotalSeconds:F0} s)."
                           + (cr.Ranked.Count > 0
                               ? " Best with real glasses: " + string.Join(", ", cr.Ranked.GroupBy(c => c.Layout.Elements).OrderBy(g => g.Key)
                                   .Select(g => $"{g.Key} el {g.Min(c => c.Rms) * 1000:F2} µm"))
                               : "");
            }
            ResultsTab = ColorResults.Count > 0 ? 0 : 1;
            if (ColorResults.Count > 0) SelectedColor = ColorResults[0];
            else if (LayoutResults.Count > 0) SelectedLayout = LayoutResults[0];
            double airy = _airy;
            SummaryText = summary + $"  Airy radius {airy:F2} µm.";
            StatusText = $"Done in {clock.Elapsed.TotalSeconds:F0} s.";
            _lastSettings = settings;
            HasResults = LayoutResults.Count > 0;
            Progress = 100;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Stopped.";
            HasResults = LayoutResults.Count > 0;
        }
        catch (Exception ex)
        {
            StatusText = "The search failed: " + ex.Message;
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cancel?.Cancel();

    /// <summary>Writes each design to its own folder in every lens format, and returns a line for each.</summary>
    public async Task<string> ExportAsync(IEnumerable<object> rows)
    {
        var picked = rows.ToList();
        if (picked.Count == 0) return "Nothing selected to export.";
        string folder = OutputFolder;
        StatusText = $"Exporting {picked.Count} lens(es)...";
        var lines = await Task.Run(() =>
        {
            var done = new List<string>();
            foreach (var row in picked)
            {
                var (design, name) = row switch
                {
                    ColorRow c => (c.Candidate.Design, $"color{c.Rank:D3}_{c.Layout.Replace(' ', '-')}"),
                    LayoutRow l => (l.Candidate.Design, $"model{l.Rank:D3}_{l.Layout.Replace(' ', '-')}"),
                    _ => (null, ""),
                };
                if (design is null) continue;
                var warnings = new List<string>();
                var a = Analysis.Run(design, Catalog);
                Exporters.Write(a.System, Catalog, Path.Combine(folder, name), name, Exporters.AllFormats, warnings);
                done.Add(name);
            }
            return done;
        });
        StatusText = $"Exported {lines.Count} to {folder}";
        return StatusText;
    }

    public IEnumerable<object> TopRows() =>
        ResultsTab == 0 ? ColorResults.Take(ExportCount) : LayoutResults.Take(ExportCount);

    /// <summary>Saves the shown table as CSV.</summary>
    public string SaveTable(string path)
    {
        string full = ResultsTab == 0
            ? LayoutColor.WriteCsv(ColorResults.Select(r => r.Candidate), path)
            : Layouts.WriteCsv(new LayoutResult
            {
                Settings = _lastSettings ?? new LayoutSettings(), Ranked = LayoutResults.Select(r => r.Candidate).ToList(),
                BestByElements = new(), Layouts = 0, Feasible = 0, Elapsed = TimeSpan.Zero,
            }, path);
        StatusText = "Saved " + full;
        return full;
    }
}
