using System.Runtime.InteropServices;
using Muralis.Services.Interop;

namespace Muralis.Services;

/// <summary>
/// Détecte un jeu / une vidéo / une présentation plein écran au premier plan, pour différer
/// les changements de fond (#41). Appelé seulement au moment d'une pose prévue (puis toutes
/// les ~30 s tant qu'une pose attend) : aucun polling permanent ; quelques microsecondes.
/// </summary>
public static class FullscreenDetector
{
    public static bool IsFullscreenAppInForeground()
    {
        // 1. L'avis de Windows (D3D exclusif, mode présentation, « occupé »).
        if (SHQueryUserNotificationState(out var state) == 0
            && state is QueryUserNotificationState.Busy
                or QueryUserNotificationState.RunningD3DFullScreen
                or QueryUserNotificationState.PresentationMode)
        {
            return true;
        }

        // 2. Repli : jeux en « borderless windowed », souvent ignorés par l'API ci-dessus.
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero || window == GetShellWindow() || IsShellClass(window))
            return false;

        GetWindowThreadProcessId(window, out uint processId);
        if (processId == (uint)Environment.ProcessId)
            return false;

        if (!GetWindowRect(window, out RECT windowRect))
            return false;

        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), ref monitorInfo))
            return false;

        long style = GetWindowLongPtr(window, GwlStyle).ToInt64();
        return CoversMonitor(windowRect, monitorInfo.rcMonitor, hasCaption: (style & WsCaption) == WsCaption);
    }

    /// <summary>Fenêtre sans barre de titre couvrant tout son moniteur. La barre de titre
    /// écarte une fenêtre normale maximisée (navigateur avec barre des tâches masquée).</summary>
    internal static bool CoversMonitor(RECT window, RECT monitor, bool hasCaption) =>
        !hasCaption
        && window.Left <= monitor.Left && window.Top <= monitor.Top
        && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    private static bool IsShellClass(IntPtr window)
    {
        var name = new char[64];
        int length = GetClassName(window, name, name.Length);
        var className = new string(name, 0, Math.Max(length, 0));
        return className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private enum QueryUserNotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningD3DFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        App = 7,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000;
    private const uint MonitorDefaultToNearest = 2;

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out QueryUserNotificationState state);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, [Out] char[] className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
}
