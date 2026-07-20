using System.Runtime.InteropServices;
using Muralis.Models;

namespace Muralis.Services.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
}

/// <summary>
/// Interface COM <c>IDesktopWallpaper</c> (Windows 8+). API native qui gère le multi-écran
/// et permet d'assigner un fond <b>par moniteur</b> via <see cref="SetWallpaper"/>.
///
/// IMPORTANT : les méthodes sont déclarées dans l'ordre exact de la vtable native — ne pas
/// réordonner, insérer ou supprimer. Les méthodes non utilisées en M2 (slideshow) sont tout
/// de même déclarées pour préserver l'alignement des slots vtable ; elles seront exploitées en M3.
/// Sans PreserveSig : le HRESULT est vérifié automatiquement (COMException levée en cas d'échec)
/// et le paramètre <c>[out, retval]</c> natif devient la valeur de retour managée.
/// </summary>
[ComImport]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDesktopWallpaper
{
    void SetWallpaper(
        [MarshalAs(UnmanagedType.LPWStr)] string? monitorID,
        [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetMonitorDevicePathAt(uint monitorIndex);

    uint GetMonitorDevicePathCount();

    RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);

    void SetBackgroundColor(uint color);

    uint GetBackgroundColor();

    void SetPosition(DesktopWallpaperPosition position);

    DesktopWallpaperPosition GetPosition();

    // --- Slideshow (M3) : slots vtable préservés, signatures simplifiées ---
    void SetSlideshow(IntPtr items);

    IntPtr GetSlideshow();

    void SetSlideshowOptions(int options, uint slideshowTick);

    void GetSlideshowOptions(out int options, out uint slideshowTick);

    void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, int direction);

    int GetStatus();

    void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
}

/// <summary>CoClass concrète du service Desktop Wallpaper.</summary>
[ComImport]
[Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
public class DesktopWallpaperClass
{
}
