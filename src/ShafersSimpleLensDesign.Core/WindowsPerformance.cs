using System.Runtime.InteropServices;

namespace ShafersSimpleLensDesign.Core;

/// <summary>
/// Opts the process out of Windows 11 power throttling (EcoQoS), as LensHH-LT and MonoSearch do.
/// <para>Windows 11 throttles the threads of a process that is not the foreground app, parking them on efficiency
/// cores and capping their clock. A search running on every core then slows the moment the window loses focus, and a
/// command-line run in a background terminal can be throttled throughout. Opting out keeps the search at full speed
/// whatever has focus. It costs nothing when idle (throttling only affects running threads). Call once at start-up;
/// a silent no-op off Windows or on builds without the API.</para>
/// </summary>
public static class WindowsPerformance
{
    // PROCESS_POWER_THROTTLING_STATE (processthreadsapi.h)
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    private const int ProcessPowerThrottling = 4;                    // PROCESS_INFORMATION_CLASS
    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(IntPtr hProcess, int processInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE processInformation, int processInformationSize);

    /// <summary>Never throttle this process's execution speed. Returns false if Windows refused or lacks the API.</summary>
    public static bool DisablePowerThrottling()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            // Take control of EXECUTION_SPEED and set its state bit off: "never EcoQoS-throttle this process".
            ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
            StateMask = 0,
        };
        try
        {
            return SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state,
                Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;   // Windows before 10 1809: keep the default behavior
        }
    }
}
