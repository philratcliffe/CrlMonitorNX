using System.Globalization;
using System.Text;
using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal sealed class ConsoleReporter(ReportingStatus status) : IReporter
{
    private const int ConsoleWidth = 80;
    private const int UriColumnWidth = 45;
    private const int NextUpdateColumnWidth = 15;
    private const int DaysColumnWidth = 6;
    private const int StatusColumnWidth = 10;
    private static readonly CompositeFormat TableRowFormat = CompositeFormat.Parse($"{{0,-{UriColumnWidth}}}{{1,-{NextUpdateColumnWidth}}}{{2,-{DaysColumnWidth}}}{{3,-{StatusColumnWidth}}}");
    private static readonly CompositeFormat UriPadFormat = CompositeFormat.Parse($"{{0,-{UriColumnWidth}}}");
    private readonly ReportingStatus _status = status ?? throw new ArgumentNullException(nameof(status));

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
        this.WriteSummary(run.Results);
        WriteResultNotes(run.Results);
        WriteDiagnostics(run);
        return Task.CompletedTask;
    }

    private static void WriteBanner(DateTime generatedAtUtc, int count)
    {
        var line = new string('=', ConsoleWidth);
        var timestamp = TimeFormatter.FormatUtc(generatedAtUtc);
#pragma warning disable CA1303
        Console.WriteLine(line);
        Console.WriteLine("CRL Monitor Report");
        Console.WriteLine(line);
        Console.WriteLine($"Generated (UTC): {timestamp}");
        Console.WriteLine($"CRLs Checked: {count}");
        Console.WriteLine();
#pragma warning restore CA1303
    }

    private static void WriteTableHeader()
    {
        var header = string.Format(
            CultureInfo.InvariantCulture,
            TableRowFormat,
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
            TableRowFormat,
            uri,
            nextUpdate,
            days,
            result.Status.ToDisplayString());

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

    private void WriteSummary(IReadOnlyList<CrlCheckResult> results)
    {
        var summary = CrlStatusSummary.FromResults(results);

#pragma warning disable CA1303
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Total:    {summary.Total}");
        Console.WriteLine($"  OK:       {summary.Ok}");
        Console.WriteLine($"  Warning:  {summary.Warning}");
        Console.WriteLine($"  Expiring: {summary.Expiring}");
        Console.WriteLine($"  Expired:  {summary.Expired}");
        Console.WriteLine($"  Errors:   {summary.Errors}");

        Console.WriteLine();
        Console.WriteLine("Report written to:");
        if (this._status.CsvWritten && !string.IsNullOrWhiteSpace(this._status.CsvPath))
        {
            Console.WriteLine($"  CSV: {this._status.CsvPath}");
        }
        else
        {
            Console.WriteLine("  CSV: (not generated)");
        }

        if (this._status.HtmlWritten && !string.IsNullOrWhiteSpace(this._status.HtmlReportPath))
        {
            Console.WriteLine($"  HTML: {this._status.HtmlReportPath}");
        }
        else
        {
            Console.WriteLine("  HTML: (not generated)");
        }
#pragma warning restore CA1303
#pragma warning disable CA1303
        Console.WriteLine(this._status.EmailReportSent ? "Report email sent successfully." : "Report email not sent.");
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
                parts.Add($"Previous: {TimeFormatter.FormatUtc(entry.PreviousFetchUtc.Value)}");
            }

            if (!string.IsNullOrWhiteSpace(entry.ErrorInfo))
            {
                parts.Add(entry.ErrorInfo!);
            }

            if (parts.Count == 0)
            {
                continue;
            }

            var paddedUri = string.Format(CultureInfo.InvariantCulture, UriPadFormat, uri);
            WriteWrappedLine($"  {paddedUri} ", string.Join(" | ", parts));
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

    private static ConsoleColor GetStatusColor(CrlStatus status)
    {
        return status switch {
            CrlStatus.Ok => ConsoleColor.Green,
            CrlStatus.Warning => ConsoleColor.Yellow,
            CrlStatus.Expiring => ConsoleColor.Yellow,
            CrlStatus.Expired => ConsoleColor.Red,
            CrlStatus.Error => ConsoleColor.Red,
            _ => ConsoleColor.Gray
        };
    }

    private static string Truncate(string value, int width)
    {
        return string.IsNullOrWhiteSpace(value) || value.Length <= width ? value : width <= 3 ? value[..width] : value[..(width - 3)] + "...";
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
