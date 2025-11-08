using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Crl;
using CrlMonitor.Fetching;
using CrlMonitor.Models;
using CrlMonitor.Runner;
using CrlMonitor.Health;
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
        var (parsedCrl, _, _, _) = CrlMonitor.Tests.TestUtilities.CrlTestBuilder.BuildParsedCrl(false);
        var baseParsed = parsedCrl;
        var parser = new StubParser(baseParsed);
        var fetcher = new StubFetcher(Array.Empty<byte>());
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Valid");
        var healthEvaluator = new StubHealthEvaluator("Healthy");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator);
        var entry = CreateEntry("http://example.com/crl");

        var singleEntry = new[] { entry };
        var run = await runner.RunAsync(singleEntry, TimeSpan.Zero, 1, CancellationToken.None);

        Assert.Single(run.Results);
        Assert.Equal("OK", run.Results[0].Status);
        Assert.Null(run.Results[0].ErrorInfo);
        Assert.Empty(run.Diagnostics.RuntimeWarnings);
    }

    /// <summary>
    /// Ensures fetch failures add diagnostics instead of crashing.
    /// </summary>
    [Fact]
    public static async Task RunAsyncAddsWarningWhenFetcherFails()
    {
        var (parsedFail, _, _, _) = CrlMonitor.Tests.TestUtilities.CrlTestBuilder.BuildParsedCrl(false);
        var baseParsed = parsedFail;
        var parser = new StubParser(baseParsed);
        var fetcher = new StubFetcher(Array.Empty<byte>(), new InvalidOperationException("boom"));
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Unknown");
        var healthEvaluator = new StubHealthEvaluator("Expired");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator);
        var entry = CreateEntry("http://example.com/crl");

        var singleEntry = new[] { entry };
        var run = await runner.RunAsync(singleEntry, TimeSpan.Zero, 1, CancellationToken.None);

        Assert.Equal("ERROR", run.Results[0].Status);
        Assert.False(string.IsNullOrEmpty(run.Results[0].ErrorInfo));
        Assert.NotEmpty(run.Diagnostics.RuntimeWarnings);
    }

    /// <summary>
    /// Ensures disabled signature validation yields a warning.
    /// </summary>
    [Fact]
    public static async Task RunAsyncReturnsWarningWhenSignatureSkipped()
    {
        var (parsedWarn, _, _, _) = CrlMonitor.Tests.TestUtilities.CrlTestBuilder.BuildParsedCrl(false);
        var baseParsed = parsedWarn;
        var parser = new StubParser(baseParsed);
        var fetcher = new StubFetcher(Array.Empty<byte>());
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Skipped", "Signature validation disabled.");
        var healthEvaluator = new StubHealthEvaluator("Healthy");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator);
        var entry = CreateEntry("http://example.com/crl");

        var warningEntry = new[] { entry };
        var run = await runner.RunAsync(warningEntry, TimeSpan.Zero, 1, CancellationToken.None);

        Assert.Equal("WARNING", run.Results[0].Status);
        Assert.Equal("Signature validation disabled.", run.Results[0].ErrorInfo);
    }

    /// <summary>
    /// Expiring health overrides signature warnings.
    /// </summary>
    [Fact]
    public static async Task RunAsyncPrefersExpiringOverWarning()
    {
        var (parsedExpiring, _, _, _) = CrlTestBuilder.BuildParsedCrl(false);
        var baseParsed = parsedExpiring;
        var parser = new StubParser(baseParsed);
        var fetcher = new StubFetcher(Array.Empty<byte>());
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Skipped", "Signature validation disabled.");
        var healthEvaluator = new StubHealthEvaluator("Expiring");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator);
        var entry = CreateEntry("http://example.com/crl");

        var expiringEntry = new[] { entry };
        var run = await runner.RunAsync(expiringEntry, TimeSpan.Zero, 1, CancellationToken.None);

        Assert.Equal("EXPIRING", run.Results[0].Status);
        Assert.Equal("Health issue | Signature validation disabled.", run.Results[0].ErrorInfo);
    }

    /// <summary>
    /// Expired health overrides signature warnings.
    /// </summary>
    [Fact]
    public static async Task RunAsyncPrefersExpiredOverWarning()
    {
        var (parsedExpired, _, _, _) = CrlTestBuilder.BuildParsedCrl(false);
        var baseParsed = parsedExpired;
        var parser = new StubParser(baseParsed);
        var fetcher = new StubFetcher(Array.Empty<byte>());
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Skipped", "Signature validation disabled.");
        var healthEvaluator = new StubHealthEvaluator("Expired");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator);
        var entry = CreateEntry("http://example.com/crl");

        var expiredEntry = new[] { entry };
        var run = await runner.RunAsync(expiredEntry, TimeSpan.Zero, 1, CancellationToken.None);

        Assert.Equal("EXPIRED", run.Results[0].Status);
        Assert.Equal("Health issue | Signature validation disabled.", run.Results[0].ErrorInfo);
    }

    /// <summary>
    /// Ensures file-based fetches work end-to-end.
    /// </summary>
    [Fact]
    public static async Task RunAsyncReadsFromFileFetcher()
    {
        var (parsed, caCert, _, crlBytes) = CrlTestBuilder.BuildParsedCrl(false);
        var crlPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var caPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        await File.WriteAllBytesAsync(crlPath, crlBytes);
        await File.WriteAllBytesAsync(caPath, caCert.GetEncoded());

        try
        {
            var fetcher = new FileCrlFetcher();
            var resolver = new FetcherResolver(new[] { new FetcherMapping(FileSchemes, fetcher) });
            var runner = new CrlCheckRunner(
                resolver,
                new CrlParser(SignatureValidationMode.CaCertificate),
                new CrlSignatureValidator(),
                new CrlHealthEvaluator());

            var entry = new CrlConfigEntry(new Uri(crlPath), SignatureValidationMode.CaCertificate, caPath, 0.8, null);
            var fileEntries = new[] { entry };
            var run = await runner.RunAsync(fileEntries, TimeSpan.FromSeconds(5), 1, CancellationToken.None);

            Assert.Equal("OK", run.Results[0].Status);
        }
        finally
        {
            File.Delete(crlPath);
            File.Delete(caPath);
        }
    }

    /// <summary>
    /// Ensures max concurrency limit is honored.
    /// </summary>
    [Fact]
    public static async Task RunAsyncRespectsMaxParallelFetches()
    {
        var (parsedConcurrent, _, _, _) = CrlTestBuilder.BuildParsedCrl(false);
        var baseParsed = parsedConcurrent;
        var parser = new StubParser(baseParsed);
        var fetcher = new ConcurrentFetcher(TimeSpan.FromMilliseconds(50));
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Valid");
        var healthEvaluator = new StubHealthEvaluator("Healthy");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator);
        var entries = new[]
        {
            CreateEntry("http://a"),
            CreateEntry("http://b"),
            CreateEntry("http://c")
        };

        await runner.RunAsync(entries, TimeSpan.FromSeconds(1), 2, CancellationToken.None);

        Assert.Equal(2, fetcher.MaxConcurrency);
    }

    /// <summary>
    /// Ensures fetch timeout surfaces as ERROR.
    /// </summary>
    [Fact]
    public static async Task RunAsyncTimesOutFetch()
    {
        var (parsedTimeout, _, _, _) = CrlTestBuilder.BuildParsedCrl(false);
        var baseParsed = parsedTimeout;
        var parser = new StubParser(baseParsed);
        var fetcher = new TimeoutFetcher();
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Valid");
        var healthEvaluator = new StubHealthEvaluator("Healthy");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator);
        var entry = CreateEntry("http://slow");

        var timeoutEntries = new[] { entry };
        var run = await runner.RunAsync(timeoutEntries, TimeSpan.FromMilliseconds(50), 1, CancellationToken.None);

        Assert.Equal("ERROR", run.Results[0].Status);
        Assert.Contains("timed out", run.Results[0].ErrorInfo, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures user cancellation stops the run.
    /// </summary>
    [Fact]
    public static async Task RunAsyncHonorsCancellation()
    {
        var baseParsed = CrlTestBuilder.BuildParsedCrl(false).Parsed;
        var parser = new StubParser(baseParsed);
        var fetcher = new TimeoutFetcher();
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Valid");
        var healthEvaluator = new StubHealthEvaluator("Healthy");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator);
        var entry = CreateEntry("http://slow");
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var cancelEntries = new[] { entry };
        await Assert.ThrowsAsync<TaskCanceledException>(() => runner.RunAsync(cancelEntries, TimeSpan.FromSeconds(1), 1, cts.Token));
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
        private readonly string? _message;

        public StubSignatureValidator(string status) : this(status, null)
        {
        }

        public StubSignatureValidator(string status, string? message)
        {
            _status = status;
            _message = message;
        }

        public SignatureValidationResult Validate(ParsedCrl parsedCrl, CrlConfigEntry entry)
        {
            return new SignatureValidationResult(_status, _message);
        }
    }

    private sealed class StubHealthEvaluator : ICrlHealthEvaluator
    {
        private readonly string _status;

        public StubHealthEvaluator(string status)
        {
            _status = status;
        }

        public HealthEvaluationResult Evaluate(ParsedCrl parsedCrl, CrlConfigEntry entry, DateTime utcNow)
        {
            return new HealthEvaluationResult(_status, _status == "Healthy" ? null : "Health issue");
        }
    }

    private sealed class ConcurrentFetcher : ICrlFetcher
    {
        private readonly TimeSpan _delay;
        private int _current;
        public int MaxConcurrency { get; private set; }

        public ConcurrentFetcher(TimeSpan delay)
        {
            _delay = delay;
        }

        public async Task<FetchedCrl> FetchAsync(CrlConfigEntry entry, CancellationToken cancellationToken)
        {
            var inFlight = Interlocked.Increment(ref _current);
            MaxConcurrency = Math.Max(MaxConcurrency, inFlight);
            try
            {
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }

            return new FetchedCrl(Array.Empty<byte>(), TimeSpan.Zero, 0);
        }
    }

    private sealed class TimeoutFetcher : ICrlFetcher
    {
        public async Task<FetchedCrl> FetchAsync(CrlConfigEntry entry, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            return new FetchedCrl(Array.Empty<byte>(), TimeSpan.Zero, 0);
        }
    }

    private static readonly string[] FileSchemes = { "file" };
}
