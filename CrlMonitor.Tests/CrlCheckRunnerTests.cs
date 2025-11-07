using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Crl;
using CrlMonitor.Fetching;
using CrlMonitor.Models;
using CrlMonitor.Runner;
using Xunit;

namespace CrlMonitor.Tests;

/// <summary>
/// Covers the CrlCheckRunner orchestration logic.
/// </summary>
public static class CrlCheckRunnerTests
{
    /// <summary>
    /// Ensures successful fetches surface as succeeded results.
    /// </summary>
    [Fact]
    public static async Task RunAsyncReturnsSuccessResult()
    {
        var parser = new StubParser();
        var fetcher = new StubFetcher(Array.Empty<byte>());
        var resolver = new StubResolver(fetcher);
        var runner = new CrlCheckRunner(resolver, parser);
        var entry = CreateEntry("http://example.com/crl");

        var run = await runner.RunAsync(new[] { entry }, CancellationToken.None);

        Assert.Single(run.Results);
        Assert.True(run.Results[0].Succeeded);
        Assert.Empty(run.Diagnostics.RuntimeWarnings);
    }

    /// <summary>
    /// Ensures fetch failures add diagnostics instead of crashing.
    /// </summary>
    [Fact]
    public static async Task RunAsyncAddsWarningWhenFetcherFails()
    {
        var parser = new StubParser();
        var fetcher = new StubFetcher(Array.Empty<byte>(), new InvalidOperationException("boom"));
        var resolver = new StubResolver(fetcher);
        var runner = new CrlCheckRunner(resolver, parser);
        var entry = CreateEntry("http://example.com/crl");

        var run = await runner.RunAsync(new[] { entry }, CancellationToken.None);

        Assert.False(run.Results[0].Succeeded);
        Assert.NotEmpty(run.Diagnostics.RuntimeWarnings);
    }

    private static CrlConfigEntry CreateEntry(string uri)
    {
        return new CrlConfigEntry(new Uri(uri), SignatureValidationMode.None, null, 0.8, null);
    }

    private sealed class StubResolver : IFetcherResolver
    {
        private readonly ICrlFetcher _fetcher;

        public StubResolver(ICrlFetcher fetcher)
        {
            _fetcher = fetcher;
        }

        public ICrlFetcher Resolve(Uri uri)
        {
            return _fetcher;
        }
    }

    private sealed class StubFetcher : ICrlFetcher
    {
        private readonly byte[] _content;
        private readonly Exception? _exception;

        public StubFetcher(byte[] content, Exception? exception = null)
        {
            _content = content;
            _exception = exception;
        }

        public Task<FetchedCrl> FetchAsync(CrlConfigEntry entry, CancellationToken cancellationToken)
        {
            if (_exception != null)
            {
                throw _exception;
            }

            return Task.FromResult(new FetchedCrl(_content, TimeSpan.Zero, _content.Length));
        }
    }

    private sealed class StubParser : ICrlParser
    {
        public ParsedCrl Parse(byte[] crlBytes)
        {
            return new ParsedCrl("CN=Test", DateTime.UtcNow, DateTime.UtcNow.AddDays(1), Array.Empty<string>(), false, "Skipped", null);
        }
    }
}
