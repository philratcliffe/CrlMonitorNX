using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal sealed class ConsoleReporter : IReporter
{
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private const int UriColumnWidth = 45;
    private const int NextUpdateWidth = 15;
    private const int DaysWidth = 6;
    private const int StatusWidth = 10;

    public Task ReportAsync(CrlCheckRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();

        TryClearConsole();
        WriteBanner(run.GeneratedAtUtc, run.Results.Count);
        WriteTableHeader();
        foreach (var result in run.Results)
        {
            WriteResultRow(result);
        }

        Console.WriteLine();
        WriteSummary(run.Results);
        WriteDiagnostics(run);
        return Task.CompletedTask;
    }

    private static void WriteBanner(DateTime generatedAtUtc, int count)
    {
        var line = new string('=', UriColumnWidth + NextUpdateWidth + DaysWidth + StatusWidth + 10);
        var timestamp = generatedAtUtc.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
#pragma warning disable CA1303
        Console.WriteLine(line);
        Console.WriteLine("CRL Monitor Report");
        Console.WriteLine(line);
        Console.WriteLine($"Generated (UTC): {timestamp}");
        Console.WriteLine($"Entries: {count}");
        Console.WriteLine();
#pragma warning restore CA1303
    }

    private static void WriteTableHeader()
    {
        var header = string.Format(
            CultureInfo.InvariantCulture,
            "{0,-45}{1,-15}{2,-6}{3,-10}{4}",
            "URI",
            "Next Update",
            "Days",
            "Status",
            "Details");

#pragma warning disable CA1303
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length));
#pragma warning restore CA1303
    }

    private static void WriteResultRow(CrlCheckResult result)
    {
        var uri = Truncate(result.Uri.ToString(), UriColumnWidth);
        var nextUpdate = result.ParsedCrl?.NextUpdate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        var days = CalculateDaysRemaining(result.ParsedCrl?.NextUpdate);
        var details = BuildDetails(result);
        var line = string.Format(
            CultureInfo.InvariantCulture,
            "{0,-45}{1,-15}{2,-6}{3,-10}{4}",
            uri,
            nextUpdate,
            days,
            result.Status,
            details);

        var original = Console.ForegroundColor;
        Console.ForegroundColor = GetStatusColor(result.Status);
#pragma warning disable CA1303
        Console.WriteLine(line);
#pragma warning restore CA1303
        Console.ForegroundColor = original;
    }

    private static string BuildDetails(CrlCheckResult result)
    {
        var builder = new StringBuilder();
        if (result.PreviousFetchUtc.HasValue)
        {
            builder.Append("prev: ")
                .Append(result.PreviousFetchUtc.Value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorInfo))
        {
            if (builder.Length > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(result.ErrorInfo);
        }

        return builder.ToString();
    }

    private static string CalculateDaysRemaining(DateTime? nextUpdate)
    {
        if (!nextUpdate.HasValue)
        {
            return string.Empty;
        }

        var days = (int)Math.Round((nextUpdate.Value - DateTime.UtcNow).TotalDays, MidpointRounding.AwayFromZero);
        return days.ToString(CultureInfo.InvariantCulture);
    }

    private static void WriteSummary(IReadOnlyList<CrlCheckResult> results)
    {
        var total = results.Count;
        var ok = results.Count(r => string.Equals(r.Status, "OK", StringComparison.OrdinalIgnoreCase));
        var warning = results.Count(r => string.Equals(r.Status, "WARNING", StringComparison.OrdinalIgnoreCase));
        var expiring = results.Count(r => string.Equals(r.Status, "EXPIRING", StringComparison.OrdinalIgnoreCase));
        var expired = results.Count(r => string.Equals(r.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase));
        var errors = results.Count(r => string.Equals(r.Status, "ERROR", StringComparison.OrdinalIgnoreCase));

#pragma warning disable CA1303
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Total:    {total}");
        Console.WriteLine($"  OK:       {ok}");
        Console.WriteLine($"  Warn:     {warning + expiring}");
        Console.WriteLine($"  Expired:  {expired}");
        Console.WriteLine($"  Errors:   {errors}");
#pragma warning restore CA1303
    }

    private static void WriteDiagnostics(CrlCheckRun run)
    {
        WriteWarningBlock("State warnings", run.Diagnostics.StateWarnings);
        WriteWarningBlock("Signature warnings", run.Diagnostics.SignatureWarnings);
        WriteWarningBlock("Config warnings", run.Diagnostics.ConfigurationWarnings);
        WriteWarningBlock("Runtime warnings", run.Diagnostics.RuntimeWarnings);
    }

    private static void WriteWarningBlock(string title, IEnumerable<string> warnings)
    {
        var hasEntries = false;
        foreach (var warning in warnings)
        {
            if (!hasEntries)
            {
                Console.WriteLine();
#pragma warning disable CA1303
                Console.WriteLine(title + ":");
#pragma warning restore CA1303
                hasEntries = true;
            }

#pragma warning disable CA1303
            Console.WriteLine($" - {warning}");
#pragma warning restore CA1303
        }
    }

    private static ConsoleColor GetStatusColor(string status)
    {
        return status.ToUpperInvariant() switch
        {
            "OK" => ConsoleColor.Green,
            "WARNING" => ConsoleColor.Yellow,
            "EXPIRING" => ConsoleColor.Yellow,
            "EXPIRED" => ConsoleColor.Red,
            "ERROR" => ConsoleColor.Red,
            _ => ConsoleColor.Gray
        };
    }

    private static string Truncate(string value, int width)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= width)
        {
            return value;
        }

        if (width <= 3)
        {
            return value[..width];
        }

        return value[..(width - 3)] + "...";
    }

    private static void TryClearConsole()
    {
        if (Console.IsOutputRedirected || Console.Out is StringWriter)
        {
            return;
        }

        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
