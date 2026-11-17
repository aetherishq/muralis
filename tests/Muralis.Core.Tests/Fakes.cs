using System.Collections.Concurrent;
using Muralis.Models;
using Muralis.Services;

namespace Muralis.Core.Tests;

/// <summary>Implémentations de test des abstractions de plateforme du cœur.</summary>
internal static class Fakes
{
    /// <summary>« Chiffrement » réversible et reconnaissable : seul un blob produit ici se
    /// déchiffre (comme un blob DPAPI d'un autre profil, une valeur étrangère donne null).</summary>
    public static readonly ISecretProtector Secrets = new SecretProtector();

    private sealed class SecretProtector : ISecretProtector
    {
        public string Protect(string plain) => "enc:" + plain;

        public string? Unprotect(string blob) => blob.StartsWith("enc:", StringComparison.Ordinal) ? blob[4..] : null;
    }

    /// <summary>Composition instantanée : renvoie l'image source.</summary>
    public sealed class Composer : IImageComposer
    {
        public Task<string> ComposeAsync(string sourcePath, int targetWidth, int targetHeight, DesktopWallpaperPosition mode) =>
            Task.FromResult(sourcePath);
    }

    /// <summary>Bureau en mémoire : enregistre les lots de poses.</summary>
    public sealed class Desktop : IDesktopBackend
    {
        public List<MonitorInfo> Monitors { get; } = [];

        public ConcurrentQueue<IReadOnlyList<PoseRequest>> Batches { get; } = new();

        public ConcurrentDictionary<string, string> Current { get; } = new();

        public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors;

        public void ApplyPoses(IReadOnlyList<PoseRequest> batch)
        {
            Batches.Enqueue(batch);
            foreach (var request in batch)
            {
                Current[request.DeviceId ?? string.Empty] = request.Path;
                request.Succeed();
            }
        }

        public string? GetWallpaper(string deviceId) => Current.GetValueOrDefault(deviceId);
    }
}
