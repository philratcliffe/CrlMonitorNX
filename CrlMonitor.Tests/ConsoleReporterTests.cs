using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Diagnostics;
using CrlMonitor.Models;
using CrlMonitor.Reporting;
using Xunit;

namespace CrlMonitor.Tests;

/// <summary>
/// Validates console reporter output.
/// </summary>
public static class ConsoleReporterTests
{
    /// <summary>
    /// Ensures warnings surface in console output.
    /// </summary>
    [Fact]
    public static async Task ReportAsyncWritesWarnings()
    {
        var status = new ReportingStatus();
        status.RecordCsv("report.csv");
        status.RecordHtml("reports/latest.html");
        status.RecordEmailSent();
        var reporter = new ConsoleReporter(status);
        var diagnostics = new RunDiagnostics();
        diagnostics.AddRuntimeWarning("Disk full");
        var fetchedAt = DateTime.UtcNow.AddHours(-1);
        var generatedAt = DateTime.UtcNow;
        var run = new CrlCheckRun(
            new[]
            {
                new CrlCheckResult(
                    new Uri("http://example.com"),
                    "WARNING",
                    TimeSpan.Zero,
                    null,
                    "Signature validation disabled.",
                    fetchedAt,
                    TimeSpan.FromMilliseconds(50),
                    2048,
                    generatedAt,
                    "Skipped")
            },
            diagnostics,
            generatedAt);

        using var writer = new StringWriter();
        Console.SetOut(writer);

        await reporter.ReportAsync(run, CancellationToken.None);

        var output = writer.ToString();
        var expectedGenerated = TimeFormatter.FormatUtc(generatedAt);
        var expectedPrevious = TimeFormatter.FormatUtc(fetchedAt);
        Assert.Contains("CRL Monitor Report", output, StringComparison.Ordinal);
        Assert.Contains(expectedGenerated, output, StringComparison.Ordinal);
        Assert.Contains("URI", output, StringComparison.Ordinal);
        Assert.Contains("Status", output, StringComparison.Ordinal);
        Assert.Contains("WARNING", output, StringComparison.Ordinal);
        Assert.Contains("Result details:", output, StringComparison.Ordinal);
        Assert.Contains("Signature validation disabled", output, StringComparison.Ordinal);
        Assert.Contains("Previous:", output, StringComparison.Ordinal);
        Assert.Contains(expectedPrevious, output, StringComparison.Ordinal);
        Assert.Contains("Summary:", output, StringComparison.Ordinal);
        Assert.Contains("Disk full", output, StringComparison.Ordinal);
        Assert.Contains("CSV: report.csv", output, StringComparison.Ordinal);
        Assert.Contains("HTML: reports/latest.html", output, StringComparison.Ordinal);
        Assert.Contains("Report written to:", output, StringComparison.Ordinal);
        Assert.Contains("report.csv", output, StringComparison.Ordinal);
        Assert.Contains("Report email sent successfully.", output, StringComparison.Ordinal);
    }
}
