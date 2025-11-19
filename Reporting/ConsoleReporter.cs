using System.Globalization;
using System.Text;
using CrlMonitor.Licensing;
using CrlMonitor.Models;

#pragma warning disable CA1303 // Console reporter emits English-only diagnostic output; no localization planned

namespace CrlMonitor.Reporting;

internal sealed class ConsoleReporter(ReportingStatus status, bool verbose = true) : IReporter
{
    private static class Ansi
    {
        public const string Green = "\u001b[32m";
        public const string Yellow = "\u001b[33m";
        public const string BrightYellow = "\u001b[93m";
        public const string Red = "\u001b[31m";
        public const string Magenta = "\u001b[35m";
        public const string Grey = "\x1b[90m";
        public const string Cyan = "\u001b[36m";
        public const string White = "\u001b[97m";
        public const string Reset = "\u001b[0m";
    }
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
        AnnounceDebugBuild();

        if (this._verbose)
        {
            this.WriteFullReport(run);
        }
        else
        {
            this.WriteSummaryReport(run);
        }

        Console.WriteLine();
        return Task.CompletedTask;
    }

    private void WriteFullReport(CrlCheckRun run)
    {
        WriteBanner();
        WriteTableHeader();
        foreach (var result in run.Results)
        {
            WriteResultRow(result);
        }

        Console.WriteLine();
        this.WriteSummary(run.Results);
        if (LicenseBootstrapper.ValidatedLicense?.Type == Standard.Licensing.LicenseType.Trial)
        {
            WriteTrialUpgradeMessage();
        }

        WriteResultNotes(run.Results);
        WriteDiagnostics(run);
    }

    private void WriteSummaryReport(CrlCheckRun run)
    {
        WriteBanner();
        WriteTableHeader();
        foreach (var result in run.Results)
        {
            WriteResultRow(result);
        }

        Console.WriteLine();
        WriteSimpleSummary(run.Results);
        WriteErrorSummary(run.Results);
        this.WriteReportPaths();
        if (LicenseBootstrapper.ValidatedLicense?.Type == Standard.Licensing.LicenseType.Trial)
        {
            WriteTrialUpgradeMessage();
        }
    }

    private static void WriteSimpleSummary(IReadOnlyList<CrlCheckResult> results)
    {
        var summary = CrlStatusSummary.FromResults(results);
        var colorEnabled = IsColorEnabled();

        Console.WriteLine();
        Console.WriteLine("Summary:");
        if (colorEnabled)
        {
            Console.WriteLine($"  {"Total:",-10} {Ansi.White}{summary.Total}{Ansi.Reset}");
            Console.WriteLine($"  {"OK:",-10} {Ansi.White}{summary.Ok}{Ansi.Reset}");
            Console.WriteLine($"  {"Warning:",-10} {Ansi.White}{summary.Warning}{Ansi.Reset}");
            Console.WriteLine($"  {"Expiring:",-10} {Ansi.White}{summary.Expiring}{Ansi.Reset}");
            Console.WriteLine($"  {"Expired:",-10} {Ansi.White}{summary.Expired}{Ansi.Reset}");
            Console.WriteLine($"  {"Errors:",-10} {Ansi.White}{summary.Errors}{Ansi.Reset}");
        }
        else
        {
            Console.WriteLine($"  {"Total:",-10} {summary.Total}");
            Console.WriteLine($"  {"OK:",-10} {summary.Ok}");
            Console.WriteLine($"  {"Warning:",-10} {summary.Warning}");
            Console.WriteLine($"  {"Expiring:",-10} {summary.Expiring}");
            Console.WriteLine($"  {"Expired:",-10} {summary.Expired}");
            Console.WriteLine($"  {"Errors:",-10} {summary.Errors}");
        }
    }

    private static void WriteErrorSummary(IReadOnlyList<CrlCheckResult> results)
    {
        var errors = results.Where(r => r.Status == CrlStatus.Error).ToList();
        if (errors.Count == 0)
        {
            return;
        }

        var colorEnabled = IsColorEnabled();
        Console.WriteLine();
        if (colorEnabled)
        {
            Console.WriteLine($"{Ansi.Red}Errors ({errors.Count}):{Ansi.Reset}");
        }
        else
        {
            Console.WriteLine($"Errors ({errors.Count}):");
        }

        var shown = Math.Min(errors.Count, MaxErrorsInSummary);
        for (var i = 0; i < shown; i++)
        {
            var error = errors[i];
            var fileName = ExtractFileName(error.Uri);
            var reason = string.IsNullOrWhiteSpace(error.ErrorInfo) ? "Unknown error" : error.ErrorInfo;
            if (colorEnabled)
            {
                Console.WriteLine($"  {Ansi.White}- {fileName}: {reason}{Ansi.Reset}");
            }
            else
            {
                Console.WriteLine($"  - {fileName}: {reason}");
            }
        }

        if (errors.Count > MaxErrorsInSummary)
        {
            var remaining = errors.Count - MaxErrorsInSummary;
            Console.WriteLine($"  \u2026 and {remaining} more {(remaining == 1 ? "error" : "errors")} (full list in CSV/HTML report)");
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

        var colorEnabled = IsColorEnabled();
        Console.WriteLine();
        Console.WriteLine("Reports:");

        if (hasCsv)
        {
            if (colorEnabled)
            {
                Console.WriteLine($"  {Ansi.White}CSV: {this._status.CsvPath}{Ansi.Reset}");
            }
            else
            {
                Console.WriteLine($"  CSV: {this._status.CsvPath}");
            }
        }

        if (hasHtml)
        {
            if (colorEnabled)
            {
                Console.WriteLine($"  {Ansi.White}HTML: {this._status.HtmlReportPath}{Ansi.Reset}");
            }
            else
            {
                Console.WriteLine($"  HTML: {this._status.HtmlReportPath}");
            }
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

    private static void WriteBanner()
    {
        var line = new string('=', ConsoleWidth);
        var assemblyVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var semver = assemblyVersion != null ? $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}" : "unknown";
        var title = $"Red Kestrel CrlMonitor v{semver}";
        var colorEnabled = IsColorEnabled();

        if (colorEnabled)
        {
            Console.WriteLine($"{Ansi.Grey}{line}{Ansi.Reset}");
            Console.WriteLine($"                     {Ansi.Cyan}{title}{Ansi.Reset}");
            Console.WriteLine($"{Ansi.Grey}{line}{Ansi.Reset}");
        }
        else
        {
            Console.WriteLine(line);
            Console.WriteLine(title.PadLeft((ConsoleWidth + title.Length) / 2));
            Console.WriteLine(line);
        }

        Console.WriteLine();

        var license = LicenseBootstrapper.ValidatedLicense;
        if (license != null)
        {
            if (colorEnabled)
            {
                Console.WriteLine($"License Type:   {Ansi.White}{license.Type}{Ansi.Reset}");
            }
            else
            {
                Console.WriteLine($"License Type:   {license.Type}");
            }

            if (license.Type == Standard.Licensing.LicenseType.Trial)
            {
                var trialStatus = LicenseBootstrapper.TrialStatus;
                if (trialStatus != null)
                {
                    if (colorEnabled)
                    {
                        Console.WriteLine($"Days Remaining: {Ansi.White}{trialStatus.DaysRemaining}{Ansi.Reset}");
                    }
                    else
                    {
                        Console.WriteLine($"Days Remaining: {trialStatus.DaysRemaining}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"Valid Until:    {license.Expiration:d MMM yyyy}");
            }
        }

        Console.WriteLine();
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

        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length));
    }

    private static void WriteResultRow(CrlCheckResult result)
    {
        var uri = Truncate(result.Uri.ToString(), UriColumnWidth);
        var nextUpdate = result.ParsedCrl?.NextUpdate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        var days = CalculateDaysRemaining(result.ParsedCrl?.NextUpdate);
        var statusDisplay = result.Status.ToDisplayString();
        var line = string.Format(
            CultureInfo.InvariantCulture,
            TableRowFormat,
            uri,
            nextUpdate,
            days,
            statusDisplay);

        if (IsColorEnabled())
        {
            var colorCode = GetStatusColorCode(statusDisplay);
            Console.WriteLine($"{colorCode}{line}{Ansi.Reset}");
        }
        else
        {
            Console.WriteLine(line);
        }
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
        Console.WriteLine(this._status.EmailReportSent ? "Report email sent successfully." : "Report email not sent.");
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

        Console.WriteLine();
        Console.WriteLine("Result details:");
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
                Console.WriteLine(title + ":");
                hasEntries = true;
            }

            WriteWrappedLine(" - ", trimmed);
        }
    }

    private static string GetStatusColorCode(string status)
    {
        return status switch {
            "OK" => Ansi.Green,
            "WARNING" => Ansi.Yellow,
            "EXPIRING" => Ansi.BrightYellow,
            "EXPIRED" => Ansi.Red,
            "ERROR" => Ansi.Red,
            _ => string.Empty
        };
    }

    private static string Truncate(string value, int width)
    {
        return string.IsNullOrWhiteSpace(value) || value.Length <= width ? value : width <= 3 ? value[..width] : value[..(width - 3)] + "...";
    }

    /// <summary>
    /// Displays upgrade message for trial users with their request code.
    /// </summary>
    private static void WriteTrialUpgradeMessage()
    {
        var requestCode = LicenseBootstrapper.CreateRequestCode();
        var colorEnabled = IsColorEnabled();

        Console.WriteLine();
        if (colorEnabled)
        {
            Console.WriteLine($"{Ansi.Grey}You are using a trial license. To upgrade, please email{Ansi.Reset}");
            Console.WriteLine($"{Ansi.Grey}sales@redkestrel.co.uk with your request code: {Ansi.White}{requestCode}{Ansi.Reset}");
        }
        else
        {
            Console.WriteLine("You are using a trial license. To upgrade, please email");
            Console.WriteLine($"sales@redkestrel.co.uk with your request code: {requestCode}");
        }
    }

    private static bool IsColorEnabled()
    {
        return !Console.IsOutputRedirected && Console.Out is not StringWriter;
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

    [System.Diagnostics.Conditional("DEBUG")]
    private static void AnnounceDebugBuild()
    {
        var originalColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("DEBUG VERSION");
            Console.WriteLine();
        }
        finally
        {
            Console.ForegroundColor = originalColor;
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
