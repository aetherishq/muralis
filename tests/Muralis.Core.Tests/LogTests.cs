using System.IO;
using Muralis.Services;

namespace Muralis.Core.Tests;

/// <summary>Journal fichier (issue #46). <see cref="Log"/> est statique : ces tests ne
/// tournent pas en parallèle des autres classes qui l'initialisent.</summary>
[Collection(nameof(LogCollection))]
public sealed class LogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "muralis-log-" + Guid.NewGuid().ToString("N"));

    public LogTests() => Log.Initialize(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string LogFile => Path.Combine(_dir, "muralis.log");

    [Fact]
    public void Write_AppendsLevelMessageAndException()
    {
        Log.Warn("fetch failed", new InvalidOperationException("boom"));

        string text = File.ReadAllText(LogFile);
        Assert.Contains("[WARN] fetch failed", text);
        Assert.Contains("System.InvalidOperationException: boom", text);
    }

    [Fact]
    public void Write_RotatesPastMaxSize()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(LogFile, new string('x', (int)Log.MaxBytes));

        Log.Info("after rotation");

        Assert.True(File.Exists(Path.Combine(_dir, "muralis.1.log")));
        Assert.Contains("after rotation", File.ReadAllText(LogFile));
        Assert.True(new FileInfo(LogFile).Length < Log.MaxBytes);
    }

    [Fact]
    public void Write_NeverThrows_WhenDirectoryIsUnusable()
    {
        // Un fichier à la place du dossier : CreateDirectory lève, le journal doit avaler.
        string blocked = Path.Combine(Path.GetTempPath(), "muralis-log-blocked-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocked, "not a directory");
        try
        {
            Log.Initialize(blocked);
            Log.Error("lost", new Exception());
        }
        finally
        {
            File.Delete(blocked);
            Log.Initialize(_dir);
        }
    }
}

[CollectionDefinition(nameof(LogCollection), DisableParallelization = true)]
public sealed class LogCollection;
