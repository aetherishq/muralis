using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Muralis.Models;
using Muralis.Resources;
using Muralis.Services;
using Muralis.Services.Sources;

namespace Muralis.Tests;

/// <summary>Issue #45 : téléchargements concurrents de la même URL, cache empoisonné par
/// un fichier partiel ou non-image, taille non bornée.</summary>
public sealed class WebCacheTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3, 4];
    private const string ImageUrl = "https://img.example.invalid/full/picture.png";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "muralis-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static WallpaperSourceConfig Source() => new()
    {
        Id = "daily",
        Name = "Daily",
        Kind = SourceKind.Daily,
        RequestUrl = "https://api.example.invalid/today",
        ImageUrlJsonPath = "url",
    };

    private WebWallpaperFetcher Fetcher(FakeServer server) =>
        new(new HttpClient(server) { Timeout = TimeSpan.FromSeconds(10) }, new ConfigService(_dir));

    private string CacheDir => Path.Combine(_dir, "webcache", "daily");

    private string[] CacheFiles() => Directory.Exists(CacheDir) ? Directory.GetFiles(CacheDir) : [];

    [Fact]
    public async Task TwoScreensOnSameSource_ShareOneDownload()
    {
        // Bing sur deux écrans : les deux cibles démarrent et tickent ensemble.
        var server = new FakeServer(Png, imageDelay: TimeSpan.FromMilliseconds(300));
        var fetcher = Fetcher(server);

        var results = await Task.WhenAll(
            fetcher.FetchAsync(Source(), monitor: null, CancellationToken.None),
            fetcher.FetchAsync(Source(), monitor: null, CancellationToken.None));

        Assert.All(results, r => Assert.Null(r.FailureReason));
        Assert.Equal(results[0].Path, results[1].Path);
        Assert.Equal(1, server.ImageRequests);
        Assert.Equal(Png, File.ReadAllBytes(results[0].Path!));
        Assert.Single(CacheFiles()); // aucun .part résiduel
    }

    [Fact]
    public async Task HtmlServedAsOk_IsRejected_AndNeverCached()
    {
        // Portail captif : 200 + page HTML à la place de l'image.
        var server = new FakeServer("<html>login</html>"u8.ToArray(), contentType: "text/html");

        var result = await Fetcher(server).FetchAsync(Source(), monitor: null, CancellationToken.None);

        Assert.Null(result.Path);
        Assert.Equal(Strings.FetchFailure_InvalidImage, result.FailureReason);
        Assert.Empty(CacheFiles());
    }

    [Fact]
    public async Task AfterRejection_NextFetchDownloadsAgain()
    {
        var server = new FakeServer("<html>login</html>"u8.ToArray(), contentType: "text/html");
        var fetcher = Fetcher(server);
        await fetcher.FetchAsync(Source(), monitor: null, CancellationToken.None);

        server.Image = Png; // connecté : la même URL sert enfin l'image
        var result = await fetcher.FetchAsync(Source(), monitor: null, CancellationToken.None);

        Assert.NotNull(result.Path);
        Assert.Equal(2, server.ImageRequests);
    }

    [Fact]
    public async Task PoisonedFileFromOlderVersion_IsDownloadedAgain()
    {
        var server = new FakeServer(Png);
        var fetcher = Fetcher(server);
        string path = (await fetcher.FetchAsync(Source(), monitor: null, CancellationToken.None)).Path!;
        File.WriteAllText(path, "<html>partial or poisoned</html>");

        var result = await fetcher.FetchAsync(Source(), monitor: null, CancellationToken.None);

        Assert.Equal(path, result.Path);
        Assert.Equal(Png, File.ReadAllBytes(path));
        Assert.Equal(2, server.ImageRequests);
    }

    [Fact]
    public async Task ValidCachedFile_IsNotDownloadedAgain()
    {
        var server = new FakeServer(Png);
        var fetcher = Fetcher(server);

        await fetcher.FetchAsync(Source(), monitor: null, CancellationToken.None);
        await fetcher.FetchAsync(Source(), monitor: null, CancellationToken.None);

        Assert.Equal(1, server.ImageRequests);
    }

    [Fact]
    public async Task OversizedImage_IsRejectedFromContentLength()
    {
        var server = new FakeServer(Png) { AnnouncedLength = WebWallpaperFetcher.MaxImageBytes + 1 };

        var result = await Fetcher(server).FetchAsync(Source(), monitor: null, CancellationToken.None);

        Assert.Equal(Strings.FetchFailure_InvalidImage, result.FailureReason);
        Assert.Empty(CacheFiles());
    }

    [Fact]
    public async Task Invalidate_DropsTheFile_SoItIsDownloadedAgain()
    {
        var server = new FakeServer(Png);
        var fetcher = Fetcher(server);
        string path = (await fetcher.FetchAsync(Source(), monitor: null, CancellationToken.None)).Path!;

        fetcher.Invalidate(path);
        Assert.False(File.Exists(path));

        await fetcher.FetchAsync(Source(), monitor: null, CancellationToken.None);
        Assert.Equal(2, server.ImageRequests);
    }

    [Fact]
    public void Invalidate_IgnoresFilesOutsideTheCache()
    {
        string outside = Path.Combine(Path.GetTempPath(), "muralis-outside-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(outside, Png);
        try
        {
            Fetcher(new FakeServer(Png)).Invalidate(outside);
            Assert.True(File.Exists(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, true)]
    [InlineData(new byte[] { 0x42, 0x4D, 0, 0 }, true)]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, true)]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x41, 0x56, 0x45 }, false)] // RIFF/WAVE
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C }, false)]                              // <html
    [InlineData(new byte[] { }, false)]
    public void ImageSignature_RecognizesSupportedFormats(byte[] header, bool expected)
    {
        Assert.Equal(expected, ImageSignature.IsImage(header));
    }

    /// <summary>Sert le JSON de la source puis l'image ; compte les requêtes d'image.</summary>
    private sealed class FakeServer(byte[] image, TimeSpan imageDelay = default, string contentType = "image/png") : HttpMessageHandler
    {
        private int _imageRequests;

        public byte[] Image { get; set; } = image;

        public long? AnnouncedLength { get; init; }

        public int ImageRequests => _imageRequests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host == "api.example.invalid")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{ "url": "{{ImageUrl}}" }""") };

            Interlocked.Increment(ref _imageRequests);
            if (imageDelay > TimeSpan.Zero)
                await Task.Delay(imageDelay, ct);

            var content = new ByteArrayContent(Image);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            if (AnnouncedLength is { } length)
                content.Headers.ContentLength = length;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}
