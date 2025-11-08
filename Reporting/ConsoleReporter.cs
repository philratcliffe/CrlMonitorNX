using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal sealed class ConsoleReporter : IReporter
{
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public Task ReportAsync(CrlCheckRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();
        WriteHeader(run.GeneratedAtUtc);
        foreach (var result in run.Results)
        {
            WriteResult(result);
        }

        WriteDiagnostics(run);
        return Task.CompletedTask;
    }

    private static void WriteHeader(DateTime generatedAtUtc)
    {
#pragma warning disable CA1303
        var formattedTimestamp = generatedAtUtc.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
        Console.WriteLine("╔══════════════════════════════════════════════════════╗");
        Console.WriteLine("║                     CRL RESULTS                      ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════╝");
        Console.WriteLine($"Report generated (UTC): {formattedTimestamp}");
#pragma warning restore CA1303
    }

    private static void WriteResult(CrlCheckResult result)
    {
        var builder = new StringBuilder();
        builder.Append(result.Status).Append(' ').Append(result.Uri);
        if (!string.IsNullOrWhiteSpace(result.ErrorInfo))
        {
            builder.Append(" :: ").Append(result.ErrorInfo);
        }

        if (result.PreviousFetchUtc.HasValue)
        {
            builder.Append(" | previous fetch: ")
                .Append(result.PreviousFetchUtc.Value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
        }

#pragma warning disable CA1303
        Console.WriteLine(builder.ToString());
#pragma warning restore CA1303
    }

    private static void WriteDiagnostics(CrlCheckRun run)
    {
        WriteWarningBlock("State warnings", run.Diagnostics.StateWarnings);
        WriteWarningBlock("Signature warnings", run.Diagnostics.SignatureWarnings);
        WriteWarningBlock("Config warnings", run.Diagnostics.ConfigurationWarnings);
        WriteWarningBlock("Runtime warnings", run.Diagnostics.RuntimeWarnings);
    }

    private static void WriteWarningBlock(string title, System.Collections.Generic.IEnumerable<string> warnings)
    {
        var hasEntries = false;
        foreach (var warning in warnings)
        {
            if (!hasEntries)
            {
#pragma warning disable CA1303
                Console.WriteLine();
                Console.WriteLine(title + ":");
#pragma warning restore CA1303
                hasEntries = true;
            }

#pragma warning disable CA1303
            Console.WriteLine($" - {warning}");
#pragma warning restore CA1303
        }
    }
}
