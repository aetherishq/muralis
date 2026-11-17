using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Muralis.Models;
using Muralis.Resources;
using Muralis.Services;
using Muralis.Services.Sources;

namespace Muralis.Tests;

/// <summary>Issue #46 : un timeout HTTP était avalé comme une annulation (aucun
/// WebFetchFailed) et tout échec était réduit à « null » sans cause.</summary>
public sealed class FetchFailureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "muralis-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static WallpaperSourceConfig CustomSource() => new()
    {
        Id = "src",
        Name = "Test",
        RequestUrl = "https://example.invalid/api",
        ImageUrlJsonPath = "url",
    };

    private WebWallpaperFetcher Fetcher(HttpMessageHandler handler, TimeSpan timeout) =>
        new(new HttpClient(handler) { Timeout = timeout }, new ConfigService(_dir));

    [Fact]
    public async Task HttpTimeout_IsAFailure_NotACancellation()
    {
        var fetcher = Fetcher(new DelayHandler(TimeSpan.FromSeconds(10)), TimeSpan.FromMilliseconds(100));

        var result = await fetcher.FetchAsync(CustomSource(), monitor: null, CancellationToken.None);

        Assert.Null(result.Path);
        Assert.Equal(Strings.FetchFailure_Timeout, result.FailureReason);
    }

    [Fact]
    public async Task RequestedCancellation_StillPropagates()
    {
        var fetcher = Fetcher(new DelayHandler(TimeSpan.FromSeconds(10)), TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fetcher.FetchAsync(CustomSource(), monitor: null, cts.Token));
    }

    [Fact]
    public async Task HttpError_ReportsStatusCode()
    {
        var fetcher = Fetcher(new StatusHandler(HttpStatusCode.Unauthorized), TimeSpan.FromSeconds(5));

        var result = await fetcher.FetchAsync(CustomSource(), monitor: null, CancellationToken.None);

        Assert.Equal(string.Format(Strings.FetchFailure_HttpFormat, 401), result.FailureReason);
    }

    [Fact]
    public async Task UnexpectedJson_ReportsUnexpectedResponse()
    {
        var fetcher = Fetcher(new StatusHandler(HttpStatusCode.OK, """{ "other": 1 }"""), TimeSpan.FromSeconds(5));

        var result = await fetcher.FetchAsync(CustomSource(), monitor: null, CancellationToken.None);

        Assert.Equal(Strings.FetchFailure_UnexpectedResponse, result.FailureReason);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public void DescribeFailure_MapsExceptions(Exception exception, string expected)
    {
        Assert.Equal(expected, WebWallpaperFetcher.DescribeFailure(exception));
    }

    public static TheoryData<Exception, string> Failures() => new()
    {
        { new TaskCanceledException("timeout", new TimeoutException()), Strings.FetchFailure_Timeout },
        { new HttpRequestException("x", null, HttpStatusCode.TooManyRequests), string.Format(Strings.FetchFailure_HttpFormat, 429) },
        { new HttpRequestException("dns"), Strings.FetchFailure_Network },
        { new WebWallpaperFetcher.NoResultsException("none"), Strings.FetchFailure_NoResults },
        { new JsonException("bad"), Strings.FetchFailure_UnexpectedResponse },
        { new KeyNotFoundException(), Strings.FetchFailure_UnexpectedResponse },
        { new IOException("disk"), Strings.FetchFailure_Other },
    };

    private sealed class DelayHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(delay, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StatusHandler(HttpStatusCode status, string body = "") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
