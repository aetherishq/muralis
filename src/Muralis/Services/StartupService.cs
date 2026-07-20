using System.Diagnostics;
using Microsoft.Win32;

namespace Muralis.Services;

/// <summary>
/// Démarrage automatique avec Windows via la clé <c>HKCU\...\Run</c> (pas de droits admin,
/// pas de tâche planifiée — cf. AGENTS.md). L'état est toujours lu directement du registre,
/// jamais mis en miroir dans config.json, pour éviter toute désynchronisation.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Muralis";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) is not null;
    }

    public static void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable)
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName
                ?? Environment.ProcessPath
                ?? throw new InvalidOperationException("Chemin de l'exécutable introuvable.");
            key.SetValue(AppName, $"\"{exePath}\" --minimized");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }
}
