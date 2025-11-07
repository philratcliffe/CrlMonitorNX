using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Crl;
using CrlMonitor.Fetching;
using Xunit;

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
        using var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(responseBytes)
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var fetcher = new HttpCrlFetcher(httpClient);
        var entry = new CrlConfigEntry(new Uri("http://localhost/crl"), SignatureValidationMode.None, null, 0.8, null);

        var fetched = await fetcher.FetchAsync(entry, CancellationToken.None);

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
        var entry = new CrlConfigEntry(new Uri("http://localhost/fail"), SignatureValidationMode.None, null, 0.8, null);

        await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.FetchAsync(entry, CancellationToken.None));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responseFactory;

        public StubHandler(Func<HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_responseFactory());
        }
    }
}
