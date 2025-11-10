using CrlMonitor.Crl;
using CrlMonitor.Fetching;
using CrlMonitor.Models;
using CrlMonitor.Runner;
using CrlMonitor.Health;
using CrlMonitor.Tests.TestUtilities;
using CrlMonitor.Validation;
using CrlMonitor.State;

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
        var (parsedCrl, _, _, _) = CrlTestBuilder.BuildParsedCrl(false);
        var baseParsed = parsedCrl;
        var parser = new StubParser(baseParsed);
        var fetcher = new StubFetcher([]);
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Valid");
        var healthEvaluator = new StubHealthEvaluator("Healthy");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator, new NullStateStore());
        var entry = CreateEntry("http://example.com/crl");

        var singleEntry = new[] { entry };
        var run = await runner.RunAsync(singleEntry, TimeSpan.Zero, 1, CancellationToken.None).ConfigureAwait(true);

        _ = Assert.Single(run.Results);
        Assert.Equal(CrlStatus.Ok, run.Results[0].Status);
        Assert.Null(run.Results[0].ErrorInfo);
        Assert.Empty(run.Diagnostics.RuntimeWarnings);
    }

    /// <summary>
    /// Ensures fetch failures add diagnostics instead of crashing.
    /// </summary>
    [Fact]
    public static async Task RunAsyncAddsWarningWhenFetcherFails()
    {
        var (parsedFail, _, _, _) = CrlTestBuilder.BuildParsedCrl(false);
        var baseParsed = parsedFail;
        var parser = new StubParser(baseParsed);
        var fetcher = new StubFetcher([], new InvalidOperationException("boom"));
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Unknown");
        var healthEvaluator = new StubHealthEvaluator("Expired");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator, new NullStateStore());
        var entry = CreateEntry("http://example.com/crl");

        var singleEntry = new[] { entry };
        var run = await runner.RunAsync(singleEntry, TimeSpan.Zero, 1, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(CrlStatus.Error, run.Results[0].Status);
        Assert.False(string.IsNullOrEmpty(run.Results[0].ErrorInfo));
        Assert.NotEmpty(run.Diagnostics.RuntimeWarnings);
    }

    /// <summary>
    /// Ensures oversized payloads produce warnings rather than errors.
    /// </summary>
    [Fact]
    public static async Task RunAsyncReturnsWarningWhenCrlTooLarge()
    {
        var parsed = CrlTestBuilder.BuildParsedCrl(false).Parsed;
        var parser = new StubParser(parsed);
        var entry = CreateEntry("http://example.com/oversize");
        var fetcher = new StubFetcher([], new CrlTooLargeException(entry.Uri, entry.MaxCrlSizeBytes, entry.MaxCrlSizeBytes + 1));
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Valid");
        var healthEvaluator = new StubHealthEvaluator("Healthy");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator, new NullStateStore());

        var run = await runner.RunAsync(new[] { entry }, TimeSpan.Zero, 1, CancellationToken.None).ConfigureAwait(true);

        var result = Assert.Single(run.Results);
        Assert.Equal(CrlStatus.Warning, result.Status);
        Assert.Contains("Skipped: CRL exceeded", result.ErrorInfo, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(run.Diagnostics.RuntimeWarnings);
    }

    /// <summary>
    /// Ensures disabled signature validation yields a warning.
    /// </summary>
    [Fact]
    public static async Task RunAsyncReturnsWarningWhenSignatureSkipped()
    {
        var (parsedWarn, _, _, _) = CrlTestBuilder.BuildParsedCrl(false);
        var baseParsed = parsedWarn;
        var parser = new StubParser(baseParsed);
        var fetcher = new StubFetcher([]);
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Skipped", "Signature validation disabled.");
        var healthEvaluator = new StubHealthEvaluator("Healthy");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator, new NullStateStore());
        var entry = CreateEntry("http://example.com/crl");

        var warningEntry = new[] { entry };
        var run = await runner.RunAsync(warningEntry, TimeSpan.Zero, 1, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(CrlStatus.Warning, run.Results[0].Status);
        Assert.Equal("Signature validation disabled.", run.Results[0].ErrorInfo);
    }

    /// <summary>
    /// Expiring health overrides signature warnings.
    /// </summary>
    [Fact]
    public static async Task RunAsyncPrefersExpiringOverWarning()
    {
        var now = DateTime.UtcNow;
        var (parsedExpiring, _, _, _) = CrlTestBuilder.BuildParsedCrl(false, now, now.AddMinutes(10));
        var baseParsed = parsedExpiring;
        var parser = new StubParser(baseParsed);
        var fetcher = new StubFetcher([]);
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Skipped", "Signature validation disabled.");
        var healthEvaluator = new StubHealthEvaluator("Expiring");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator, new NullStateStore());
        var entry = CreateEntry("http://example.com/crl");

        var expiringEntry = new[] { entry };
        var run = await runner.RunAsync(expiringEntry, TimeSpan.Zero, 1, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(CrlStatus.Expiring, run.Results[0].Status);
        Assert.Equal("Health issue | Signature validation disabled.", run.Results[0].ErrorInfo);
    }

    /// <summary>
    /// Expired health overrides signature warnings.
    /// </summary>
    [Fact]
    public static async Task RunAsyncPrefersExpiredOverWarning()
    {
        var nowExpired = DateTime.UtcNow;
        var (parsedExpired, _, _, _) = CrlTestBuilder.BuildParsedCrl(false, nowExpired.AddDays(-1), nowExpired.AddMinutes(-10));
        var baseParsed = parsedExpired;
        var parser = new StubParser(baseParsed);
        var fetcher = new StubFetcher([]);
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Skipped", "Signature validation disabled.");
        var healthEvaluator = new StubHealthEvaluator("Expired");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator, new NullStateStore());
        var entry = CreateEntry("http://example.com/crl");

        var expiredEntry = new[] { entry };
        var run = await runner.RunAsync(expiredEntry, TimeSpan.Zero, 1, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(CrlStatus.Expired, run.Results[0].Status);
        Assert.Equal("Health issue | Signature validation disabled.", run.Results[0].ErrorInfo);
    }

    /// <summary>
    /// Ensures file-based fetches work end-to-end.
    /// </summary>
    [Fact]
    public static async Task RunAsyncReadsFromFileFetcher()
    {
        var (_, caCert, _, crlBytes) = CrlTestBuilder.BuildParsedCrl(false);
        var crlPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var caPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        await File.WriteAllBytesAsync(crlPath, crlBytes).ConfigureAwait(true);
        await File.WriteAllBytesAsync(caPath, caCert.GetEncoded()).ConfigureAwait(true);

        try
        {
            var fetcher = new FileCrlFetcher();
            var resolver = new FetcherResolver(new[] { new FetcherMapping(FileSchemes, fetcher) });
            var runner = new CrlCheckRunner(
                resolver,
                new CrlParser(SignatureValidationMode.CaCertificate),
                new CrlSignatureValidator(),
                new CrlHealthEvaluator(),
                new NullStateStore());

            var entry = new CrlConfigEntry(new Uri(crlPath), SignatureValidationMode.CaCertificate, caPath, 0.8, null, 10 * 1024 * 1024);
            var fileEntries = new[] { entry };
            var run = await runner.RunAsync(fileEntries, TimeSpan.FromSeconds(5), 1, CancellationToken.None).ConfigureAwait(true);

            Assert.Equal(CrlStatus.Ok, run.Results[0].Status);
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
        var fetcher = new ConcurrentFetcher(TimeSpan.FromMilliseconds(200));
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Valid");
        var healthEvaluator = new StubHealthEvaluator("Healthy");
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator, new NullStateStore());
        var entries = new[]
        {
            CreateEntry("http://a"),
            CreateEntry("http://b"),
            CreateEntry("http://c")
        };

        _ = await runner.RunAsync(entries, TimeSpan.FromSeconds(1), 2, CancellationToken.None).ConfigureAwait(true);

        Assert.True(fetcher.MaxConcurrency >= 2, $"Expected concurrency >= 2, saw {fetcher.MaxConcurrency}");
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
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator, new NullStateStore());
        var entry = CreateEntry("http://slow");

        var timeoutEntries = new[] { entry };
        var run = await runner.RunAsync(timeoutEntries, TimeSpan.FromMilliseconds(50), 1, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(CrlStatus.Error, run.Results[0].Status);
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
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator, new NullStateStore());
        var entry = CreateEntry("http://slow");
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var cancelEntries = new[] { entry };
        _ = await Assert.ThrowsAsync<TaskCanceledException>(() => runner.RunAsync(cancelEntries, TimeSpan.FromSeconds(1), 1, cts.Token)).ConfigureAwait(true);
    }

    /// <summary>
    /// Successful runs persist last fetch times.
    /// </summary>
    [Fact]
    public static async Task RunAsyncPersistsLastFetchTimestamp()
    {
        var parsed = CrlTestBuilder.BuildParsedCrl(false).Parsed;
        var parser = new StubParser(parsed);
        var fetcher = new StubFetcher([]);
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Valid");
        var healthEvaluator = new StubHealthEvaluator("Healthy");
        var stateStore = new RecordingStateStore {
            LastFetchToReturn = DateTime.UtcNow.AddHours(-3)
        };
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator, stateStore);
        var entry = CreateEntry("http://example.com/crl");

        var run = await runner.RunAsync(new[] { entry }, TimeSpan.Zero, 1, CancellationToken.None).ConfigureAwait(true);

        _ = Assert.NotNull(stateStore.LastSavedAt);
        Assert.Equal(stateStore.LastFetchToReturn, run.Results[0].PreviousFetchUtc);
    }

    /// <summary>
    /// State failures surface in diagnostics.
    /// </summary>
    [Fact]
    public static async Task RunAsyncAddsWarningWhenStateStoreFails()
    {
        var parsed = CrlTestBuilder.BuildParsedCrl(false).Parsed;
        var parser = new StubParser(parsed);
        var fetcher = new StubFetcher([]);
        var resolver = new StubResolver(fetcher);
        var signatureValidator = new StubSignatureValidator("Valid");
        var healthEvaluator = new StubHealthEvaluator("Healthy");
        var stateStore = new RecordingStateStore {
            LoadException = new IOException("load failed"),
            SaveException = new IOException("save failed")
        };
        var runner = new CrlCheckRunner(resolver, parser, signatureValidator, healthEvaluator, stateStore);
        var entry = CreateEntry("http://example.com/crl");

        var run = await runner.RunAsync(new[] { entry }, TimeSpan.Zero, 1, CancellationToken.None).ConfigureAwait(true);

        Assert.NotEmpty(run.Diagnostics.StateWarnings);
    }

    private static CrlConfigEntry CreateEntry(string uri)
    {
        return new CrlConfigEntry(new Uri(uri), SignatureValidationMode.None, null, 0.8, null, 10 * 1024 * 1024);
    }

    private sealed class StubResolver(ICrlFetcher fetcher) : IFetcherResolver
    {
        private readonly ICrlFetcher _fetcher = fetcher;

        public ICrlFetcher Resolve(Uri uri)
        {
            return this._fetcher;
        }
    }

    private sealed class StubFetcher(byte[] content, Exception? exception = null) : ICrlFetcher
    {
        private readonly byte[] _content = content;
        private readonly Exception? _exception = exception;

        public Task<FetchedCrl> FetchAsync(CrlConfigEntry entry, CancellationToken cancellationToken)
        {
            return this._exception != null ? throw this._exception : Task.FromResult(new FetchedCrl(this._content, TimeSpan.Zero, this._content.Length));
        }
    }

    private sealed class StubParser(ParsedCrl parsed) : ICrlParser
    {
        private readonly ParsedCrl _parsed = parsed;

        public ParsedCrl Parse(byte[] crlBytes)
        {
            return this._parsed;
        }
    }

    private sealed class StubSignatureValidator(string status, string? message) : ICrlSignatureValidator
    {
        private readonly string _status = status;
        private readonly string? _message = message;

        public StubSignatureValidator(string status) : this(status, null)
        {
        }

        public SignatureValidationResult Validate(ParsedCrl parsedCrl, CrlConfigEntry entry)
        {
            return new SignatureValidationResult(this._status, this._message);
        }
    }

    private sealed class StubHealthEvaluator(string status) : ICrlHealthEvaluator
    {
        private readonly string _status = status;

        public HealthEvaluationResult Evaluate(ParsedCrl parsedCrl, CrlConfigEntry entry, DateTime utcNow)
        {
            return new HealthEvaluationResult(this._status, this._status == "Healthy" ? null : "Health issue");
        }
    }

    private sealed class ConcurrentFetcher(TimeSpan delay) : ICrlFetcher
    {
        private readonly TimeSpan _delay = delay;
        private int _current;
        private int _maxConcurrency;
        public int MaxConcurrency => Volatile.Read(ref this._maxConcurrency);

        public async Task<FetchedCrl> FetchAsync(CrlConfigEntry entry, CancellationToken cancellationToken)
        {
            var inFlight = Interlocked.Increment(ref this._current);
            this.UpdateMax(inFlight);
            try
            {
                await Task.Delay(this._delay, cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                _ = Interlocked.Decrement(ref this._current);
            }

            return new FetchedCrl([], TimeSpan.Zero, 0);
        }

        private void UpdateMax(int candidate)
        {
            var snapshot = Volatile.Read(ref this._maxConcurrency);
            while (candidate > snapshot)
            {
                var previous = Interlocked.CompareExchange(ref this._maxConcurrency, candidate, snapshot);
                if (previous == snapshot)
                {
                    return;
                }

                snapshot = previous;
            }
        }
    }

    private sealed class TimeoutFetcher : ICrlFetcher
    {
        public async Task<FetchedCrl> FetchAsync(CrlConfigEntry entry, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(true);
            return new FetchedCrl([], TimeSpan.Zero, 0);
        }
    }

    private static readonly string[] FileSchemes = ["file"];

    private sealed class NullStateStore : IStateStore
    {
        public Task<DateTime?> GetLastFetchAsync(Uri uri, CancellationToken cancellationToken)
        {
            return Task.FromResult<DateTime?>(null);
        }

        public Task SaveLastFetchAsync(Uri uri, DateTime fetchedAtUtc, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<DateTime?> GetLastReportSentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<DateTime?>(null);
        }

        public Task SaveLastReportSentAsync(DateTime sentAtUtc, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<DateTime?> GetAlertCooldownAsync(string key, CancellationToken cancellationToken)
        {
            return Task.FromResult<DateTime?>(null);
        }

        public Task SaveAlertCooldownAsync(string key, DateTime triggeredAtUtc, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStateStore : IStateStore
    {
        public Exception? LoadException { get; set; }
        public Exception? SaveException { get; set; }
        public DateTime? LastSavedAt { get; private set; }
        public DateTime? LastFetchToReturn { get; set; }

        public Task<DateTime?> GetLastFetchAsync(Uri uri, CancellationToken cancellationToken)
        {
            return this.LoadException != null ? throw this.LoadException : Task.FromResult(this.LastFetchToReturn);
        }

        public Task SaveLastFetchAsync(Uri uri, DateTime fetchedAtUtc, CancellationToken cancellationToken)
        {
            if (this.SaveException != null)
            {
                throw this.SaveException;
            }

            this.LastSavedAt = fetchedAtUtc;
            return Task.CompletedTask;
        }

        public Task<DateTime?> GetLastReportSentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<DateTime?>(null);
        }

        public Task SaveLastReportSentAsync(DateTime sentAtUtc, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<DateTime?> GetAlertCooldownAsync(string key, CancellationToken cancellationToken)
        {
            return Task.FromResult<DateTime?>(null);
        }

        public Task SaveAlertCooldownAsync(string key, DateTime triggeredAtUtc, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
