using ShafersSimpleLensDesign.Core;
using Avalonia;

namespace ShafersSimpleLensDesign;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Without this, Windows 11 throttles the search the moment the window loses focus.
        WindowsPerformance.DisablePowerThrottling();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
