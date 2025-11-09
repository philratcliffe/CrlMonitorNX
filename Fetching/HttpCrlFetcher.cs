using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CrlMonitor.Fetching;

internal sealed class HttpCrlFetcher : ICrlFetcher
{
    private readonly HttpClient _httpClient;

    public HttpCrlFetcher(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<FetchedCrl> FetchAsync(CrlConfigEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var request = new HttpRequestMessage(HttpMethod.Get, entry.Uri);
        var start = DateTime.UtcNow;
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var limit = entry.MaxCrlSizeBytes;
        var declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength.HasValue && declaredLength.Value > limit)
        {
            throw new CrlTooLargeException(entry.Uri, limit, declaredLength.Value);
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var bytes = await CrlContentLimiter.ReadAllBytesAsync(stream, entry.Uri, limit, cancellationToken).ConfigureAwait(false);
        var elapsed = DateTime.UtcNow - start;
        return new FetchedCrl(bytes, elapsed, bytes.LongLength);
    }
}
