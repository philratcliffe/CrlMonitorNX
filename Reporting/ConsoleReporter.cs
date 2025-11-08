using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal sealed class ConsoleReporter : IReporter
{
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private const int ConsoleWidth = 80;
    private const int UriColumnWidth = 45;

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
        WriteResultNotes(run.Results);
        WriteDiagnostics(run);
        return Task.CompletedTask;
    }

    private static void WriteBanner(DateTime generatedAtUtc, int count)
    {
        var line = new string('=', ConsoleWidth);
        var timestamp = generatedAtUtc.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
#pragma warning disable CA1303
        Console.WriteLine(line);
        Console.WriteLine("CRL Monitor Report");
        Console.WriteLine(line);
        Console.WriteLine($"Generated (UTC): {timestamp}");
        Console.WriteLine();
#pragma warning restore CA1303
    }

    private static void WriteTableHeader()
    {
        var header = string.Format(
            CultureInfo.InvariantCulture,
            "{0,-45}{1,-15}{2,-6}{3,-10}",
            "URI",
            "Next Update",
            "Days",
            "Status");

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
        var line = string.Format(
            CultureInfo.InvariantCulture,
            "{0,-45}{1,-15}{2,-6}{3,-10}",
            uri,
            nextUpdate,
            days,
            result.Status);

        var original = Console.ForegroundColor;
        Console.ForegroundColor = GetStatusColor(result.Status);
#pragma warning disable CA1303
        Console.WriteLine(line);
#pragma warning restore CA1303
        Console.ForegroundColor = original;
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
        Console.WriteLine($"  Warning:  {warning}");
        Console.WriteLine($"  Expiring: {expiring}");
        Console.WriteLine($"  Expired:  {expired}");
        Console.WriteLine($"  Errors:   {errors}");
#pragma warning restore CA1303
    }

    private static void WriteResultNotes(IReadOnlyList<CrlCheckResult> results)
    {
        var notes = results
            .Where(r => r.PreviousFetchUtc.HasValue || !string.IsNullOrWhiteSpace(r.ErrorInfo))
            .ToList();
        if (notes.Count == 0)
        {
            return;
        }

#pragma warning disable CA1303
        Console.WriteLine();
        Console.WriteLine("Result details:");
#pragma warning restore CA1303
        foreach (var entry in notes)
        {
            var uri = Truncate(entry.Uri.ToString(), UriColumnWidth);
            var parts = new List<string>();
            if (entry.PreviousFetchUtc.HasValue)
            {
                parts.Add($"Previous: {entry.PreviousFetchUtc.Value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture)}");
            }

            if (!string.IsNullOrWhiteSpace(entry.ErrorInfo))
            {
                parts.Add(entry.ErrorInfo!);
            }

            WriteWrappedLine($"  {uri,-45} ", string.Join(" | ", parts));
        }
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
            var trimmed = warning?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            if (!hasEntries)
            {
                Console.WriteLine();
#pragma warning disable CA1303
                Console.WriteLine(title + ":");
#pragma warning restore CA1303
                hasEntries = true;
            }

            WriteWrappedLine(" - ", trimmed);
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

    private static void WriteWrappedLine(string prefix, string content)
    {
        var text = content ?? string.Empty;
        var effectivePrefix = prefix.Length > ConsoleWidth ? prefix[..ConsoleWidth] : prefix;
        var indent = new string(' ', Math.Min(prefix.Length, ConsoleWidth));

        if (string.IsNullOrEmpty(text))
        {
            Console.WriteLine(effectivePrefix);
            return;
        }

        while (true)
        {
            var available = ConsoleWidth - effectivePrefix.Length;
            if (available <= 0)
            {
                Console.WriteLine(effectivePrefix);
                effectivePrefix = indent;
                continue;
            }

            if (text.Length <= available)
            {
                Console.WriteLine(effectivePrefix + text);
                break;
            }

            var chunk = text[..available];
            var splitIndex = chunk.LastIndexOf(' ');
            if (splitIndex <= 0)
            {
                splitIndex = available;
            }

            Console.WriteLine(effectivePrefix + text[..splitIndex]);
            text = text[splitIndex..].TrimStart();
            effectivePrefix = indent;
        }
    }
}
