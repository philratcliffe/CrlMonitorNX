using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.DirectoryServices.Protocols;
using CrlMonitor.Crl;
using CrlMonitor.Diagnostics;
using CrlMonitor.Fetching;
using CrlMonitor.Models;
using CrlMonitor.Validation;
using CrlMonitor.Health;
using CrlMonitor.State;

namespace CrlMonitor.Runner;

internal sealed class CrlCheckRunner
{
    private readonly IFetcherResolver _fetcherResolver;
    private readonly ICrlParser _parser;
    private readonly ICrlSignatureValidator _signatureValidator;
    private readonly ICrlHealthEvaluator _healthEvaluator;
    private readonly IStateStore _stateStore;

    public CrlCheckRunner(
        IFetcherResolver fetcherResolver,
        ICrlParser parser,
        ICrlSignatureValidator signatureValidator,
        ICrlHealthEvaluator healthEvaluator,
        IStateStore stateStore)
    {
        _fetcherResolver = fetcherResolver ?? throw new ArgumentNullException(nameof(fetcherResolver));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _signatureValidator = signatureValidator ?? throw new ArgumentNullException(nameof(signatureValidator));
        _healthEvaluator = healthEvaluator ?? throw new ArgumentNullException(nameof(healthEvaluator));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public async Task<CrlCheckRun> RunAsync(
        IReadOnlyList<CrlConfigEntry> entries,
        TimeSpan fetchTimeout,
        int maxParallelFetches,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var diagnostics = new RunDiagnostics();
#pragma warning disable CA1031
        var maxParallel = Math.Max(1, maxParallelFetches);
        using var semaphore = new SemaphoreSlim(maxParallel);
        var tasks = new List<Task>();
        var results = new CrlCheckResult[entries.Count];

        for (var index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localIndex = index;
            var entry = entries[localIndex];
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    results[localIndex] = await ProcessEntryAsync(entry, fetchTimeout, diagnostics, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
#pragma warning restore CA1031
        return new CrlCheckRun(results, diagnostics, DateTime.UtcNow);
    }

#pragma warning disable CA1031
    private async Task<CrlCheckResult> ProcessEntryAsync(
        CrlConfigEntry entry,
        TimeSpan fetchTimeout,
        RunDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var previousFetch = await TryGetLastFetchAsync(entry, diagnostics, cancellationToken).ConfigureAwait(false);
        TimeSpan? downloadDuration = null;
        long? contentLength = null;
        try
        {
            var fetcher = _fetcherResolver.Resolve(entry.Uri);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (fetchTimeout > TimeSpan.Zero)
            {
                timeoutCts.CancelAfter(fetchTimeout);
            }

            var fetched = await fetcher.FetchAsync(entry, timeoutCts.Token).ConfigureAwait(false);
            downloadDuration = fetched.Duration;
            contentLength = fetched.ContentLength;
            var parsed = _parser.Parse(fetched.Content);
            var signature = _signatureValidator.Validate(parsed, entry);
            var health = _healthEvaluator.Evaluate(parsed, entry, DateTime.UtcNow);
            stopwatch.Stop();

            var status = DetermineStatus(entry, diagnostics, signature, health);
            var errorInfo = BuildErrorInfo(signature, health, status);
            var completedAt = DateTime.UtcNow;
            await TrySaveLastFetchAsync(entry, diagnostics, completedAt, cancellationToken).ConfigureAwait(false);

            return new CrlCheckResult(
                entry.Uri,
                status,
                stopwatch.Elapsed,
                parsed,
                errorInfo,
                previousFetch,
                downloadDuration,
                contentLength,
                completedAt,
                signature.Status);
        }
        catch (CrlTooLargeException ex)
        {
            stopwatch.Stop();
            var message = BuildOversizeStatusMessage(ex);
            diagnostics.AddRuntimeWarning(BuildProcessingErrorMessage(entry.Uri, message));
            return new CrlCheckResult(
                entry.Uri,
                CrlStatus.Warning,
                stopwatch.Elapsed,
                null,
                message,
                previousFetch,
                downloadDuration,
                contentLength,
                DateTime.UtcNow,
                null);
        }
        catch (OperationCanceledException) when (fetchTimeout > TimeSpan.Zero)
        {
            stopwatch.Stop();
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            var msg = $"Fetch timed out after {fetchTimeout.TotalSeconds:F1}s";
            diagnostics.AddRuntimeWarning(BuildProcessingErrorMessage(entry.Uri, msg));
            return new CrlCheckResult(
                entry.Uri,
                CrlStatus.Error,
                stopwatch.Elapsed,
                null,
                msg,
                previousFetch,
                downloadDuration,
                contentLength,
                DateTime.UtcNow,
                null);
        }
        catch (LdapException ldapEx)
        {
            stopwatch.Stop();
            var friendly = ConvertLdapException(entry.Uri, ldapEx);
            diagnostics.AddRuntimeWarning(BuildProcessingErrorMessage(entry.Uri, friendly));
            return new CrlCheckResult(
                entry.Uri,
                CrlStatus.Error,
                stopwatch.Elapsed,
                null,
                friendly,
                previousFetch,
                downloadDuration,
                contentLength,
                DateTime.UtcNow,
                null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var message = BuildProcessingErrorMessage(entry.Uri, ex.Message);
            diagnostics.AddRuntimeWarning(message);
            return new CrlCheckResult(
                entry.Uri,
                CrlStatus.Error,
                stopwatch.Elapsed,
                null,
                ex.Message,
                previousFetch,
                downloadDuration,
                contentLength,
                DateTime.UtcNow,
                null);
        }
    }

    private static string ConvertLdapException(Uri uri, LdapException ex)
    {
        return ex.ErrorCode switch
        {
            53 or 91 or 92 => "Could not connect to LDAP host.",
            _ => $"LDAP error {ex.ErrorCode}: {ex.Message}"
        };
    }

    private static string BuildProcessingErrorMessage(Uri uri, string reason)
    {
        return $"Failed to process '{uri}': {reason}";
    }

    private static string BuildOversizeStatusMessage(CrlTooLargeException ex)
    {
        var message = $"Skipped: CRL exceeded {FormatSize(ex.LimitBytes)} limit.";
        if (ex.ObservedBytes.HasValue)
        {
            message += $" Observed {FormatSize(ex.ObservedBytes.Value)}.";
        }

        return message;
    }

    private static CrlStatus DetermineStatus(
        CrlConfigEntry entry,
        RunDiagnostics diagnostics,
        SignatureValidationResult signature,
        HealthEvaluationResult health)
    {
        var healthStatus = (health.Status?.Trim().ToUpperInvariant()) switch
        {
            "EXPIRED" => CrlStatus.Expired,
            "EXPIRING" => CrlStatus.Expiring,
            "UNKNOWN" => CrlStatus.Warning,
            _ => CrlStatus.Ok
        };

        if (healthStatus == CrlStatus.Expired)
        {
            diagnostics.AddRuntimeWarning($"CRL '{entry.Uri}' expired: {health.Message}");
        }
        else if (healthStatus == CrlStatus.Warning && !string.IsNullOrWhiteSpace(health.Message))
        {
            diagnostics.AddRuntimeWarning($"CRL '{entry.Uri}' health unknown: {health.Message}");
        }

        if (!string.Equals(signature.Status, "Valid", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(signature.Status, "Skipped", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.AddSignatureWarning($"Signature validation skipped for '{entry.Uri}': {signature.ErrorMessage}");
                return healthStatus == CrlStatus.Ok ? CrlStatus.Warning : healthStatus;
            }

            diagnostics.AddSignatureWarning($"Signature validation failed for '{entry.Uri}': {signature.ErrorMessage}");
            return CrlStatus.Error;
        }

        return healthStatus;
    }

    private static string? BuildErrorInfo(
        SignatureValidationResult signature,
        HealthEvaluationResult health,
        CrlStatus status)
    {
        var parts = new List<string>();
        if (status != CrlStatus.Ok && !string.IsNullOrWhiteSpace(health.Message))
        {
            parts.Add(health.Message!);
        }

        if (!string.Equals(signature.Status, "Valid", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(signature.ErrorMessage))
        {
            parts.Add(signature.ErrorMessage!);
        }

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string FormatSize(long bytes)
    {
        const double OneKilobyte = 1024d;
        const double OneMegabyte = OneKilobyte * 1024d;
        if (bytes >= OneMegabyte)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.##} MB", bytes / OneMegabyte);
        }

        if (bytes >= OneKilobyte)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.##} KB", bytes / OneKilobyte);
        }

        return string.Format(CultureInfo.InvariantCulture, "{0} bytes", bytes);
    }

    #pragma warning disable CA1031
    private async Task<DateTime?> TryGetLastFetchAsync(
        CrlConfigEntry entry,
        RunDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _stateStore.GetLastFetchAsync(entry.Uri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            diagnostics.AddStateWarning($"Failed to read state for '{entry.Uri}': {ex.Message}");
            return null;
        }
    }

    private async Task TrySaveLastFetchAsync(
        CrlConfigEntry entry,
        RunDiagnostics diagnostics,
        DateTime fetchedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await _stateStore.SaveLastFetchAsync(entry.Uri, fetchedAtUtc, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            diagnostics.AddStateWarning($"Failed to update state for '{entry.Uri}': {ex.Message}");
        }
    }
#pragma warning restore CA1031
}
