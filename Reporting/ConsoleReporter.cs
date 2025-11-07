using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal sealed class ConsoleReporter : IReporter
{
    public Task ReportAsync(CrlCheckRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();
        WriteHeader();
        foreach (var result in run.Results)
        {
            WriteResult(result);
        }

        WriteDiagnostics(run);
        return Task.CompletedTask;
    }

    private static void WriteHeader()
    {
#pragma warning disable CA1303
        Console.WriteLine("╔══════════════════════════════════════════════════════╗");
        Console.WriteLine("║                     CRL RESULTS                      ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════╝");
#pragma warning restore CA1303
    }

    private static void WriteResult(CrlCheckResult result)
    {
        var status = result.Succeeded ? "OK" : "ERROR";
        var builder = new StringBuilder();
        builder.Append(status).Append(' ').Append(result.Uri);
        builder.Append(" [signature: ").Append(result.SignatureStatus ?? "Unknown").Append(']');
        builder.Append(" [health: ").Append(result.HealthStatus ?? "Unknown").Append(']');
        if (!string.IsNullOrWhiteSpace(result.SignatureError))
        {
            builder.Append(" :: ").Append(result.SignatureError);
        }

        if (!result.Succeeded && !string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append(" :: ").Append(result.Error);
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
