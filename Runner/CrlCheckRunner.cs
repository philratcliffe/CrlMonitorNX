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

namespace CrlMonitor.Runner;

internal sealed class CrlCheckRunner
{
    private readonly IFetcherResolver _fetcherResolver;
    private readonly ICrlParser _parser;
    private readonly ICrlSignatureValidator _signatureValidator;

    public CrlCheckRunner(IFetcherResolver fetcherResolver, ICrlParser parser, ICrlSignatureValidator signatureValidator)
    {
        _fetcherResolver = fetcherResolver ?? throw new ArgumentNullException(nameof(fetcherResolver));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _signatureValidator = signatureValidator ?? throw new ArgumentNullException(nameof(signatureValidator));
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
                stopwatch.Stop();
                var succeeded = string.Equals(signature.Status, "Valid", StringComparison.OrdinalIgnoreCase);
                if (!succeeded)
                {
                    diagnostics.AddSignatureWarning($"Signature validation failed for '{entry.Uri}': {signature.ErrorMessage}");
                }
                results.Add(new CrlCheckResult(entry.Uri, succeeded, stopwatch.Elapsed, parsed, signature.Status, signature.ErrorMessage, succeeded ? null : signature.ErrorMessage));
            }
            catch (LdapException ldapEx)
            {
                stopwatch.Stop();
                var friendly = ConvertLdapException(entry.Uri, ldapEx);
                diagnostics.AddRuntimeWarning($"Failed to process '{entry.Uri}': {friendly}");
                results.Add(new CrlCheckResult(entry.Uri, false, stopwatch.Elapsed, null, "Unknown", friendly, friendly));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var message = $"Failed to process '{entry.Uri}': {ex.Message}";
                diagnostics.AddRuntimeWarning(message);
                results.Add(new CrlCheckResult(entry.Uri, false, stopwatch.Elapsed, null, "Unknown", ex.Message, ex.Message));
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
}
