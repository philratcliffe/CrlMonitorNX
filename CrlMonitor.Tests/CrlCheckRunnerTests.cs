using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Crl;
using CrlMonitor.Fetching;
using CrlMonitor.Models;
using CrlMonitor.Runner;
using CrlMonitor.Tests.TestUtilities;
using CrlMonitor.Validation;
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
        var baseParsed = CrlMonitor.Tests.TestUtilities.CrlTestBuilder.BuildParsedCrl(false).Parsed;
        var parser = new StubParser(baseParsed);
        var fetcher = new StubFetcher(Array.Empty<byte>());
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Valid");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator);
        var entry = CreateEntry("http://example.com/crl");

        var run = await runner.RunAsync(new[] { entry }, CancellationToken.None);

        Assert.Single(run.Results);
        Assert.True(run.Results[0].Succeeded);
        Assert.Equal("Valid", run.Results[0].SignatureStatus);
        Assert.Empty(run.Diagnostics.RuntimeWarnings);
    }

    /// <summary>
    /// Ensures fetch failures add diagnostics instead of crashing.
    /// </summary>
    [Fact]
    public static async Task RunAsyncAddsWarningWhenFetcherFails()
    {
        var baseParsed = CrlMonitor.Tests.TestUtilities.CrlTestBuilder.BuildParsedCrl(false).Parsed;
        var parser = new StubParser(baseParsed);
        var fetcher = new StubFetcher(Array.Empty<byte>(), new InvalidOperationException("boom"));
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Unknown");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator);
        var entry = CreateEntry("http://example.com/crl");

        var run = await runner.RunAsync(new[] { entry }, CancellationToken.None);

        Assert.False(run.Results[0].Succeeded);
        Assert.Equal("Unknown", run.Results[0].SignatureStatus);
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
        private readonly ParsedCrl _parsed;

        public StubParser(ParsedCrl parsed)
        {
            _parsed = parsed;
        }

        public ParsedCrl Parse(byte[] crlBytes)
        {
            return _parsed;
        }
    }

    private sealed class StubSignatureValidator : ICrlSignatureValidator
    {
        private readonly string _status;

        public StubSignatureValidator(string status)
        {
            _status = status;
        }

        public SignatureValidationResult Validate(ParsedCrl parsedCrl, CrlConfigEntry entry)
        {
            return new SignatureValidationResult(_status, null);
        }
    }
}
