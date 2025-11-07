using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.DirectoryServices.Protocols;
using CrlMonitor.Crl;
using CrlMonitor.Diagnostics;
using CrlMonitor.Fetching;
using CrlMonitor.Models;
using CrlMonitor.Validation;
using CrlMonitor.Health;

namespace CrlMonitor.Runner;

internal sealed class CrlCheckRunner
{
    private readonly IFetcherResolver _fetcherResolver;
    private readonly ICrlParser _parser;
    private readonly ICrlSignatureValidator _signatureValidator;
    private readonly ICrlHealthEvaluator _healthEvaluator;

    public CrlCheckRunner(
        IFetcherResolver fetcherResolver,
        ICrlParser parser,
        ICrlSignatureValidator signatureValidator,
        ICrlHealthEvaluator healthEvaluator)
    {
        _fetcherResolver = fetcherResolver ?? throw new ArgumentNullException(nameof(fetcherResolver));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _signatureValidator = signatureValidator ?? throw new ArgumentNullException(nameof(signatureValidator));
        _healthEvaluator = healthEvaluator ?? throw new ArgumentNullException(nameof(healthEvaluator));
    }

    public async Task<CrlCheckRun> RunAsync(
        IReadOnlyList<CrlConfigEntry> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var diagnostics = new RunDiagnostics();
        var results = new List<CrlCheckResult>(entries.Count);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
#pragma warning disable CA1031 // Runner must degrade per-URI failures into diagnostics so the process continues.
#pragma warning disable CA1031
            try
            {
                var fetcher = _fetcherResolver.Resolve(entry.Uri);
                var fetched = await fetcher.FetchAsync(entry, cancellationToken).ConfigureAwait(false);
                var parsed = _parser.Parse(fetched.Content);
                var signature = _signatureValidator.Validate(parsed, entry);
                var health = _healthEvaluator.Evaluate(parsed, entry, DateTime.UtcNow);
                stopwatch.Stop();

                var status = DetermineStatus(entry, diagnostics, signature, health);
                var errorInfo = BuildErrorInfo(signature, health, status);

                results.Add(new CrlCheckResult(entry.Uri, status, stopwatch.Elapsed, parsed, errorInfo));
            }
            catch (LdapException ldapEx)
            {
                stopwatch.Stop();
                var friendly = ConvertLdapException(entry.Uri, ldapEx);
                diagnostics.AddRuntimeWarning($"Failed to process '{entry.Uri}': {friendly}");
                results.Add(new CrlCheckResult(entry.Uri, "ERROR", stopwatch.Elapsed, null, friendly));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var message = $"Failed to process '{entry.Uri}': {ex.Message}";
                diagnostics.AddRuntimeWarning(message);
                results.Add(new CrlCheckResult(entry.Uri, "ERROR", stopwatch.Elapsed, null, ex.Message));
            }
#pragma warning restore CA1031
        }

        return new CrlCheckRun(results, diagnostics);
    }

    private static string ConvertLdapException(Uri uri, LdapException ex)
    {
        return ex.ErrorCode switch
        {
            53 or 91 or 92 => $"Could not connect to LDAP host {uri.Host}",
            _ => $"LDAP error {ex.ErrorCode}: {ex.Message}"
        };
    }

    private static string DetermineStatus(
        CrlConfigEntry entry,
        RunDiagnostics diagnostics,
        SignatureValidationResult signature,
        HealthEvaluationResult health)
    {
        var healthStatus = string.Equals(health.Status, "Expired", StringComparison.OrdinalIgnoreCase)
            ? "EXPIRED"
            : string.Equals(health.Status, "Expiring", StringComparison.OrdinalIgnoreCase)
                ? "EXPIRING"
                : string.Equals(health.Status, "Unknown", StringComparison.OrdinalIgnoreCase)
                    ? "WARNING"
                    : "OK";

        if (healthStatus == "EXPIRED")
        {
            diagnostics.AddRuntimeWarning($"CRL '{entry.Uri}' expired: {health.Message}");
        }
        else if (healthStatus == "WARNING" && !string.IsNullOrWhiteSpace(health.Message))
        {
            diagnostics.AddRuntimeWarning($"CRL '{entry.Uri}' health unknown: {health.Message}");
        }

        if (!string.Equals(signature.Status, "Valid", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(signature.Status, "Skipped", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.AddSignatureWarning($"Signature validation skipped for '{entry.Uri}': {signature.ErrorMessage}");
                return healthStatus == "OK" ? "WARNING" : healthStatus;
            }

            diagnostics.AddSignatureWarning($"Signature validation failed for '{entry.Uri}': {signature.ErrorMessage}");
            return "ERROR";
        }

        return healthStatus;
    }

    private static string? BuildErrorInfo(
        SignatureValidationResult signature,
        HealthEvaluationResult health,
        string status)
    {
        var parts = new List<string>();
        if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(health.Message))
        {
            parts.Add(health.Message!);
        }

        if (!string.Equals(signature.Status, "Valid", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(signature.ErrorMessage))
        {
            parts.Add(signature.ErrorMessage!);
        }

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }
}
