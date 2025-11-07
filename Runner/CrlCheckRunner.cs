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
                var signatureValid = string.Equals(signature.Status, "Valid", StringComparison.OrdinalIgnoreCase);
                var healthOk = !string.Equals(health.Status, "Expired", StringComparison.OrdinalIgnoreCase);
                var succeeded = signatureValid && healthOk;
                if (!succeeded)
                {
                    diagnostics.AddSignatureWarning($"Signature validation failed for '{entry.Uri}': {signature.ErrorMessage}");
                }
                if (!healthOk)
                {
                    diagnostics.AddRuntimeWarning($"CRL '{entry.Uri}' health status: {health.Status} ({health.Message})");
                }

                var errorMessage = succeeded ? null : CombineErrors(signature.ErrorMessage, health.Status, health.Message);
                results.Add(new CrlCheckResult(entry.Uri, succeeded, stopwatch.Elapsed, parsed, signature.Status, signature.ErrorMessage, health.Status, errorMessage));
            }
            catch (LdapException ldapEx)
            {
                stopwatch.Stop();
                var friendly = ConvertLdapException(entry.Uri, ldapEx);
                diagnostics.AddRuntimeWarning($"Failed to process '{entry.Uri}': {friendly}");
                results.Add(new CrlCheckResult(entry.Uri, false, stopwatch.Elapsed, null, "Unknown", friendly, null, friendly));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var message = $"Failed to process '{entry.Uri}': {ex.Message}";
                diagnostics.AddRuntimeWarning(message);
                results.Add(new CrlCheckResult(entry.Uri, false, stopwatch.Elapsed, null, "Unknown", ex.Message, null, ex.Message));
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

    private static string? CombineErrors(string? signatureError, string? healthStatus, string? healthMessage)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(signatureError))
        {
            parts.Add(signatureError);
        }

        if (string.Equals(healthStatus, "Expired", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(healthMessage))
        {
            parts.Add(healthMessage);
        }

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }
}
