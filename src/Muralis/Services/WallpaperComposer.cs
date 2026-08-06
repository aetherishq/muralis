using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Muralis.Models;

namespace Muralis.Services;

/// <summary>
/// Compose une image source vers un PNG dimensionné exactement aux pixels d'un moniteur,
/// en appliquant le <see cref="DesktopWallpaperPosition"/> demandé (Fill/Fit/Stretch/Center/Tile).
///
/// Raison d'être : <c>IDesktopWallpaper::SetPosition</c> est global à tous les écrans. En pré-composant
/// à la taille pixel de chaque moniteur, on obtient un mode d'affichage réellement indépendant par écran,
/// que l'on assigne ensuite avec une position globale neutre (Fill → rendu 1:1 car l'image est déjà à la
/// bonne taille). Le mode Span est traité en amont par <see cref="WallpaperService"/>.
/// </summary>
public class WallpaperComposer
{
    private readonly ConfigService _config;

    public WallpaperComposer(ConfigService config)
    {
        _config = config;
    }

    /// <summary>
    /// Compose <paramref name="sourcePath"/> vers un PNG de <paramref name="targetWidth"/> ×
    /// <paramref name="targetHeight"/> pixels selon <paramref name="mode"/>. Retourne le chemin du PNG
    /// (mis en cache : une même combinaison source/taille/mode n'est pas recomposée).
    /// </summary>
    public string Compose(string sourcePath, int targetWidth, int targetHeight, DesktopWallpaperPosition mode)
    {
        Directory.CreateDirectory(_config.ComposedDirectory);

        string outputPath = Path.Combine(
            _config.ComposedDirectory,
            CacheKey(sourcePath, targetWidth, targetHeight, mode) + ".png");

        if (File.Exists(outputPath))
        {
            // « Touch » : les composés du cycle actif restent jeunes — l'éviction du
            // cache (CacheMaintenanceService, plus anciens d'abord) devient signifiante.
            try { File.SetLastWriteTimeUtc(outputPath, DateTime.UtcNow); }
            catch (Exception) { /* meilleur effort */ }
            return outputPath;
        }

        var source = LoadBitmap(sourcePath);
        var rendered = Render(source, targetWidth, targetHeight, mode);
        if (IsEmptyRender(rendered))
        {
            // Un seul retry immédiat : le device se rétablit parfois dans la foulée,
            // et un écran en mode fixe n'aurait pas de tick suivant pour retenter.
            rendered = Render(source, targetWidth, targetHeight, mode);
            if (IsEmptyRender(rendered))
                throw new InvalidOperationException(
                    $"Rendu vide pour '{sourcePath}' ({targetWidth}x{targetHeight}) : RenderTargetBitmap n'a rien produit.");
        }
        SavePng(rendered, outputPath);
        return outputPath;
    }

    private static BitmapSource LoadBitmap(string path)
    {
        // Décodage STRICT via stream. Avec BitmapImage.UriSource, le décodage peut être
        // différé jusqu'au RenderTargetBitmap.Render, qui avale l'échec (fichier OneDrive
        // en cours d'hydratation, verrou antivirus/sync…) et rend la toile noire — mise en
        // cache pour toujours (issue #20). Ici tout échec lève : le diaporama garde le fond
        // courant et retente au tick suivant. OnLoad décode intégralement puis libère le fichier.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static RenderTargetBitmap Render(BitmapSource source, int tw, int th, DesktopWallpaperPosition mode)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var target = new Rect(0, 0, tw, th);

            // Fond noir : visible en Fit (letterbox), Center et Tile (bords non couverts).
            dc.DrawRectangle(Brushes.Black, null, target);

            double sw = source.PixelWidth;
            double sh = source.PixelHeight;

            switch (mode)
            {
                case DesktopWallpaperPosition.Stretch:
                    dc.DrawImage(source, target);
                    break;

                case DesktopWallpaperPosition.Fit:
                    DrawScaledCentered(dc, source, tw, th, Math.Min(tw / sw, th / sh));
                    break;

                case DesktopWallpaperPosition.Fill:
                    dc.PushClip(new RectangleGeometry(target));
                    DrawScaledCentered(dc, source, tw, th, Math.Max(tw / sw, th / sh));
                    dc.Pop();
                    break;

                case DesktopWallpaperPosition.Center:
                    dc.PushClip(new RectangleGeometry(target));
                    DrawScaledCentered(dc, source, tw, th, 1.0);
                    dc.Pop();
                    break;

                case DesktopWallpaperPosition.Tile:
                    var brush = new ImageBrush(source)
                    {
                        TileMode = TileMode.Tile,
                        Stretch = Stretch.None,
                        ViewportUnits = BrushMappingMode.Absolute,
                        Viewport = new Rect(0, 0, sw, sh),
                    };
                    brush.Freeze();
                    dc.DrawRectangle(brush, null, target);
                    break;

                default:
                    dc.DrawImage(source, target);
                    break;
            }
        }

        var rtb = new RenderTargetBitmap(tw, th, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>
    /// Détecte un rendu vide. Un rendu réussi est intégralement opaque (fond noir posé en premier
    /// dans <see cref="Render"/>) : des échantillons entièrement transparents signifient que
    /// <c>RenderTargetBitmap.Render</c> n'a rien rendu — échec silencieux de WPF quand le render
    /// thread perd le device D3D (reset de driver, sortie de veille…, issue #35). Mis en cache,
    /// un tel PNG s'affiche en noir sur le bureau, pour toujours. Le fond opaque garantit
    /// zéro faux positif, y compris pour une image source légitimement noire.
    /// </summary>
    private static bool IsEmptyRender(BitmapSource bmp)
    {
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        var samples = new[] { (0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1), (w / 2, h / 2) };
        var pixel = new byte[4]; // Pbgra32 : B, G, R, A
        foreach ((int x, int y) in samples)
        {
            bmp.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            if (pixel[3] != 0)
                return false;
        }
        return true;
    }

    private static void DrawScaledCentered(DrawingContext dc, BitmapSource source, int tw, int th, double scale)
    {
        double w = source.PixelWidth * scale;
        double h = source.PixelHeight * scale;
        double x = (tw - w) / 2.0;
        double y = (th - h) / 2.0;
        dc.DrawImage(source, new Rect(x, y, w, h));
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        // Écriture atomique : un kill en pleine écriture ne doit jamais laisser un PNG
        // partiel que le cache resservirait indéfiniment.
        string tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            encoder.Save(stream);
        File.Move(tmp, path, overwrite: true);
    }

    private static string CacheKey(string sourcePath, int tw, int th, DesktopWallpaperPosition mode)
    {
        long stamp = 0;
        try { stamp = File.GetLastWriteTimeUtc(sourcePath).Ticks; } catch { /* source volatile */ }

        // « v3 » : invalide les caches antérieurs à la garde anti-rendu-vide, potentiellement
        // empoisonnés par des PNG transparents (issue #35). « v2 » : décodage strict (issue #20).
        string raw = $"v3|{sourcePath.ToLowerInvariant()}|{stamp}|{tw}x{th}|{mode}";
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
