using System.Net;
using CrlMonitor.Crl;
using CrlMonitor.Fetching;

namespace CrlMonitor.Tests;

/// <summary>
/// Verifies HTTP fetcher behaviour.
/// </summary>
public sealed class HttpCrlFetcherTests
{
    /// <summary>
    /// Ensures 200 responses flow through to the consumer.
    /// </summary>
    [Fact]
    public async Task FetchAsyncReturnsContent()
    {
        var responseBytes = new byte[] { 1, 2, 3 };
        using var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new ByteArrayContent(responseBytes)
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var fetcher = new HttpCrlFetcher(httpClient);
        var entry = new CrlConfigEntry(new Uri("http://localhost/crl"), SignatureValidationMode.None, null, 0.8, null, 10 * 1024 * 1024);

        var fetched = await fetcher.FetchAsync(entry, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(responseBytes, fetched.Content);
        Assert.Equal(responseBytes.Length, fetched.ContentLength);
        Assert.True(fetched.Duration >= TimeSpan.Zero);
        Assert.True(handler.RequestCount > 0);
    }

    /// <summary>
    /// Ensures non-success statuses bubble up as failures.
    /// </summary>
    [Fact]
    public async Task FetchAsyncThrowsOnNonSuccess()
    {
        using var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var httpClient = new HttpClient(handler);
        var fetcher = new HttpCrlFetcher(httpClient);
        var entry = new CrlConfigEntry(new Uri("http://localhost/fail"), SignatureValidationMode.None, null, 0.8, null, 10 * 1024 * 1024);

        _ = await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.FetchAsync(entry, CancellationToken.None)).ConfigureAwait(true);
    }

    /// <summary>
    /// Ensures the fetcher enforces the configured size limit.
    /// </summary>
    [Fact]
    public async Task FetchAsyncThrowsWhenContentTooLarge()
    {
        var responseBytes = new byte[] { 1, 2, 3 };
        using var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new ByteArrayContent(responseBytes)
        });
        using var httpClient = new HttpClient(handler);
        var fetcher = new HttpCrlFetcher(httpClient);
        var entry = new CrlConfigEntry(new Uri("http://localhost/oversize"), SignatureValidationMode.None, null, 0.8, null, 2);

        _ = await Assert.ThrowsAsync<CrlTooLargeException>(() => fetcher.FetchAsync(entry, CancellationToken.None)).ConfigureAwait(true);
    }

    private sealed class StubHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.RequestCount++;
            return Task.FromResult(this._responseFactory());
        }
    }
}
