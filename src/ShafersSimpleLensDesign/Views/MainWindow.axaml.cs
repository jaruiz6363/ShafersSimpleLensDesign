using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ShafersSimpleLensDesign.ViewModels;

namespace ShafersSimpleLensDesign.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private MainViewModel Vm => (MainViewModel)DataContext!;

    private void ExitClick(object? sender, RoutedEventArgs e) => Close();

    private async void ExportTopClick(object? sender, RoutedEventArgs e) => await Vm.ExportAsync(Vm.TopRows());

    private async void ExportSelectedClick(object? sender, RoutedEventArgs e)
    {
        var grid = Vm.ResultsTab == 0 ? ColorGrid : LayoutGrid;
        await Vm.ExportAsync(grid.SelectedItems.Cast<object>());
    }

    private async void SaveTableClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the table as CSV",
            SuggestedFileName = Vm.ResultsTab == 0 ? "color.csv" : "layouts.csv",
            DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } },
        });
        if (file?.TryGetLocalPath() is { } path) Vm.SaveTable(path);
    }

    private async void BrowseOutputClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Output folder" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) Vm.OutputFolder = path;
    }

    private void OpenFolderClick(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Vm.OutputFolder);
        string opener = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        Process.Start(new ProcessStartInfo(opener, $"\"{Vm.OutputFolder}\"") { UseShellExecute = false });
    }

    /// <summary>Opens the user guide installed beside the program, or, run from source, the one on GitHub.</summary>
    private void UserGuideClick(object? sender, RoutedEventArgs e)
    {
        string guide = Path.Combine(AppContext.BaseDirectory, "docs", "USER_GUIDE.md");
        string target = File.Exists(guide) ? guide : "https://github.com/jaruiz6363/ShafersSimpleLensDesign/blob/main/docs/USER_GUIDE.md";
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Vm.StatusText = $"Could not open the user guide ({target}): {ex.Message}"; }
    }

    private async void AboutClick(object? sender, RoutedEventArgs e)
    {
        var version = typeof(MainViewModel).Assembly.GetName().Version;
        var about = new Window
        {
            Title = "About Shafer's Simple Lens Design", Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new TextBlock
            {
                Margin = new Avalonia.Thickness(20), TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Text = $"Shafer's Simple Lens Design {version?.ToString(3)}\n\nLens synthesis by surface order, after D. Shafer, \"Simple method for designing lenses,\" "
                       + "Proc. SPIE 237 (1980): every surface concentric about the chief ray or aplanatic for the marginal ray, color by "
                       + "buried surfaces. Every layout of air-spaced singlets is optimized with model glasses, then the best are given real "
                       + "glasses and their color corrected.\n\nAnalyzed on AberrationCalculator.\n\nCopyright (c) 2026 Javier A Ruiz. MIT license.",
            },
        };
        await about.ShowDialog(this);
    }
}
