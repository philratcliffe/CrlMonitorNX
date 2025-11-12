using System.Globalization;
using System.Text;
using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal sealed class ConsoleReporter(ReportingStatus status, bool verbose = true) : IReporter
{
    private const int ConsoleWidth = 80;
    private const int UriColumnWidth = 45;
    private const int NextUpdateColumnWidth = 15;
    private const int DaysColumnWidth = 6;
    private const int StatusColumnWidth = 10;
    private const int MaxErrorsInSummary = 3;
    private static readonly CompositeFormat TableRowFormat = CompositeFormat.Parse($"{{0,-{UriColumnWidth}}}{{1,-{NextUpdateColumnWidth}}}{{2,-{DaysColumnWidth}}}{{3,-{StatusColumnWidth}}}");
    private static readonly CompositeFormat UriPadFormat = CompositeFormat.Parse($"{{0,-{UriColumnWidth}}}");
    private readonly ReportingStatus _status = status ?? throw new ArgumentNullException(nameof(status));
    private readonly bool _verbose = verbose;

    public Task ReportAsync(CrlCheckRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();

        TryClearConsole();

        if (this._verbose)
        {
            this.WriteFullReport(run);
        }
        else
        {
            this.WriteSummaryReport(run);
        }

        return Task.CompletedTask;
    }

    private void WriteFullReport(CrlCheckRun run)
    {
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
    }

    private void WriteSummaryReport(CrlCheckRun run)
    {
        WriteBanner(run.GeneratedAtUtc, run.Results.Count);
        WriteTableHeader();
        foreach (var result in run.Results)
        {
            WriteResultRow(result);
        }

        Console.WriteLine();
        WriteSimpleSummary(run.Results);
        WriteErrorSummary(run.Results);
        this.WriteReportPaths();
    }

    private static void WriteSimpleSummary(IReadOnlyList<CrlCheckResult> results)
    {
        var summary = CrlStatusSummary.FromResults(results);

#pragma warning disable CA1303
        Console.WriteLine();
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Total: {summary.Total}");
        Console.WriteLine($"  OK: {summary.Ok}");
        Console.WriteLine($"  Warning: {summary.Warning}");
        Console.WriteLine($"  Expiring: {summary.Expiring}");
        Console.WriteLine($"  Expired: {summary.Expired}");
        Console.WriteLine($"  Errors: {summary.Errors}");
#pragma warning restore CA1303
    }

    private static void WriteErrorSummary(IReadOnlyList<CrlCheckResult> results)
    {
        var errors = results.Where(r => r.Status == CrlStatus.Error).ToList();
        if (errors.Count == 0)
        {
            return;
        }

        Console.WriteLine();
#pragma warning disable CA1303
        Console.WriteLine($"Errors ({errors.Count}):");
#pragma warning restore CA1303

        var shown = Math.Min(errors.Count, MaxErrorsInSummary);
        for (var i = 0; i < shown; i++)
        {
            var error = errors[i];
            var fileName = ExtractFileName(error.Uri);
            var reason = string.IsNullOrWhiteSpace(error.ErrorInfo) ? "Unknown error" : error.ErrorInfo;
#pragma warning disable CA1303
            Console.WriteLine($"  - {fileName}: {reason}");
#pragma warning restore CA1303
        }

        if (errors.Count > MaxErrorsInSummary)
        {
            var remaining = errors.Count - MaxErrorsInSummary;
#pragma warning disable CA1303
            Console.WriteLine($"  \u2026 and {remaining} more {(remaining == 1 ? "error" : "errors")} (full list in CSV/HTML report)");
#pragma warning restore CA1303
        }
    }

    private void WriteReportPaths()
    {
        var hasCsv = this._status.CsvWritten && !string.IsNullOrWhiteSpace(this._status.CsvPath);
        var hasHtml = this._status.HtmlWritten && !string.IsNullOrWhiteSpace(this._status.HtmlReportPath);

        if (!hasCsv && !hasHtml)
        {
            return;
        }

        Console.WriteLine();
#pragma warning disable CA1303
        Console.WriteLine("Reports:");
#pragma warning restore CA1303

        if (hasCsv)
        {
#pragma warning disable CA1303
            Console.WriteLine($"  CSV: {this._status.CsvPath}");
#pragma warning restore CA1303
        }

        if (hasHtml)
        {
#pragma warning disable CA1303
            Console.WriteLine($"  HTML: {this._status.HtmlReportPath}");
#pragma warning restore CA1303
        }
    }

    private static string ExtractFileName(Uri uri)
    {
        var segments = uri.Segments;
        if (segments.Length > 0)
        {
            var lastSegment = segments[^1].TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(lastSegment))
            {
                return lastSegment;
            }
        }

        var str = uri.ToString();
        return Truncate(str, 50);
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
