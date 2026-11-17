using System.IO;
using System.Text;

namespace Muralis.Services;

/// <summary>
/// Journal fichier minimal : <c>%LocalAppData%\Muralis\logs\muralis.log</c>, rotation à
/// <see cref="MaxBytes"/> vers <c>muralis.1.log</c> (une seule génération conservée).
/// Statique et sans dépendance (pas de DI en V1, cf. AGENTS.md) ; une abstraction
/// <c>ILogger</c> viendra avec l'extraction de Muralis.Core (#52). Ne lève jamais : un
/// journal indisponible ne doit pas casser l'app. Avant <see cref="Initialize"/> (tests),
/// les appels sont sans effet. Messages en anglais : c'est un artefact technique, joint
/// tel quel aux rapports de bug (issue #46).
/// </summary>
public static class Log
{
    /// <summary>Taille au-delà de laquelle le fichier courant est archivé.</summary>
    public const long MaxBytes = 1024 * 1024;

    private static readonly object Gate = new();
    private static string? _path;

    /// <summary>Dossier des journaux (null tant que non initialisé).</summary>
    public static string? Directory { get; private set; }

    public static void Initialize(string directory)
    {
        lock (Gate)
        {
            Directory = directory;
            _path = Path.Combine(directory, "muralis.log");
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        lock (Gate)
        {
            if (_path is null)
                return;

            try
            {
                System.IO.Directory.CreateDirectory(Directory!);
                RotateIfNeeded();

                var line = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [").Append(level).Append("] ")
                    .Append(message);
                if (exception is not null)
                    line.Append(Environment.NewLine).Append("    ").Append(exception.ToString().Replace(Environment.NewLine, Environment.NewLine + "    "));
                line.Append(Environment.NewLine);

                File.AppendAllText(_path, line.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
                // Journal indisponible (disque plein, droits…) : on n'a nulle part où le dire.
            }
        }
    }

    private static void RotateIfNeeded()
    {
        var info = new FileInfo(_path!);
        if (!info.Exists || info.Length < MaxBytes)
            return;
        File.Move(_path!, Path.Combine(Directory!, "muralis.1.log"), overwrite: true);
    }
}
