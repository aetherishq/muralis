using System.Runtime.InteropServices;
using Muralis.Models;
using Muralis.Services.Interop;

namespace Muralis.Services;

/// <summary>
/// Une instance <c>IDesktopWallpaper</c> le temps d'une opération, libérée explicitement
/// (sinon les RCW ne partent qu'au GC). Volontairement pas un singleton : recréer l'instance
/// rend l'app tolérante à un redémarrage d'Explorer (<c>RPC_E_DISCONNECTED</c>).
/// </summary>
public sealed class DesktopWallpaperSession : IDisposable
{
    private readonly object _com = new DesktopWallpaperClass();

    public IDesktopWallpaper Api => (IDesktopWallpaper)_com;

    /// <summary>Position globale changée seulement si elle diffère : un <c>SetPosition</c>
    /// peut à lui seul redessiner le bureau (review m5).</summary>
    /// <returns>Vrai si la position a été modifiée.</returns>
    public bool EnsurePosition(DesktopWallpaperPosition position)
    {
        if (Api.GetPosition() == position)
            return false;
        Api.SetPosition(position);
        return true;
    }

    public void Dispose() => Marshal.FinalReleaseComObject(_com);
}
