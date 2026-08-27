using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FHT.Access.App.Services;

/// <summary>
/// Ensures only one FHT Access instance runs per machine.
/// </summary>
public static class SingleInstanceGuard
{
    private const string MutexName = @"Global\FHT.Access.SingleInstance";
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, out var createdNew);
        return createdNew;
    }

    public static void BringExistingToFront()
    {
        var current = Process.GetCurrentProcess();
        var existing = Process.GetProcessesByName(current.ProcessName)
            .FirstOrDefault(p => p.Id != current.Id);
        if (existing is null)
            return;

        var handle = existing.MainWindowHandle;
        if (handle == IntPtr.Zero)
            return;

        ShowWindow(handle, SW_RESTORE);
        SetForegroundWindow(handle);
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
