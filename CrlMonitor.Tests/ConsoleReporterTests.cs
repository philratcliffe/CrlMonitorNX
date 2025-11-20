using CrlMonitor.Diagnostics;
using CrlMonitor.Licensing;
using CrlMonitor.Models;
using CrlMonitor.Reporting;
using Serilog;

namespace CrlMonitor.Tests;

/// <summary>
/// Validates console reporter output.
/// </summary>
public static class ConsoleReporterTests
{
    /// <summary>
    /// Ensures warnings surface in console output (verbose mode default).
    /// </summary>
    [Fact]
    public static async Task ReportAsyncWritesWarnings()
    {
        var status = new ReportingStatus();
        status.RecordCsv("report.csv");
        status.RecordHtml("reports/latest.html");
        status.RecordEmailSent();
        var reporter = new ConsoleReporter(status, verbose: true);
        var diagnostics = new RunDiagnostics();
        diagnostics.AddRuntimeWarning("Disk full");
        var fetchedAt = DateTime.UtcNow.AddHours(-1);
        var generatedAt = DateTime.UtcNow;
        var run = new CrlCheckRun(
            new[]
            {
                new CrlCheckResult(
                    new Uri("http://example.com"),
                    CrlStatus.Warning,
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

        // Configure Serilog to write to temp file to avoid console interference
        var logFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.log");
        var previousLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(logFile, formatProvider: System.Globalization.CultureInfo.InvariantCulture)
            .CreateLogger();

        // Initialize licensing so ConsoleReporter can display license info
        try
        {
            await LicenseBootstrapper.EnsureLicensedAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // Test may run without valid license file
        catch
#pragma warning restore CA1031
        {
            // License validation may fail in test environment - that's ok
        }

        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            await reporter.ReportAsync(run, CancellationToken.None).ConfigureAwait(true);

            var output = writer.ToString();
            var expectedPrevious = TimeFormatter.FormatUtc(fetchedAt);
            var expectedHtmlPath = OperatingSystem.IsWindows() ? "HTML: reports\\latest.html" : "HTML: reports/latest.html";
            Assert.Contains("Red Kestrel CrlMonitor", output, StringComparison.Ordinal);
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
            Assert.Contains(expectedHtmlPath, output, StringComparison.Ordinal);
            Assert.Contains("Report written to:", output, StringComparison.Ordinal);
            Assert.Contains("report.csv", output, StringComparison.Ordinal);
            Assert.Contains("Report email sent successfully.", output, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            await Log.CloseAndFlushAsync();
            Log.Logger = previousLogger;
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }
        }
    }

    /// <summary>
    /// Ensures simplified console output when verbose=false.
    /// </summary>
    [Fact]
    public static async Task SimplifiedModeShowsSummaryOnly()
    {
        var status = new ReportingStatus();
        status.RecordCsv("report.csv");
        var reporter = new ConsoleReporter(status, verbose: false);
        var diagnostics = new RunDiagnostics();
        diagnostics.AddRuntimeWarning("File not found");
        var run = new CrlCheckRun(
            new[]
            {
                new CrlCheckResult(new Uri("http://example.com/ok.crl"), CrlStatus.Ok, TimeSpan.Zero, null, null, null, TimeSpan.Zero, 0, DateTime.UtcNow, null),
                new CrlCheckResult(new Uri("http://example.com/error.crl"), CrlStatus.Error, TimeSpan.Zero, null, "Connection failed", null, TimeSpan.Zero, 0, DateTime.UtcNow, null)
            },
            diagnostics,
            DateTime.UtcNow);

        // Configure Serilog to write to temp file to avoid console interference
        var logFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.log");
        var previousLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(logFile, formatProvider: System.Globalization.CultureInfo.InvariantCulture)
            .CreateLogger();

        // Initialize licensing so ConsoleReporter can display license info
        try
        {
            await LicenseBootstrapper.EnsureLicensedAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // Test may run without valid license file
        catch
#pragma warning restore CA1031
        {
            // License validation may fail in test environment - that's ok
        }

        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            await reporter.ReportAsync(run, CancellationToken.None).ConfigureAwait(true);

            var output = writer.ToString();
            Assert.Contains("Red Kestrel CrlMonitor", output, StringComparison.Ordinal);
            Assert.Contains("URI", output, StringComparison.Ordinal);
            Assert.Contains("Status", output, StringComparison.Ordinal);
            Assert.Contains("Summary:", output, StringComparison.Ordinal);
            Assert.Contains("Total:", output, StringComparison.Ordinal);
            Assert.Contains("2", output, StringComparison.Ordinal); // Total count
            Assert.Contains("OK:", output, StringComparison.Ordinal);
            Assert.Contains("1", output, StringComparison.Ordinal); // OK and Error counts
            Assert.Contains("Errors:", output, StringComparison.Ordinal);
            Assert.Contains("error.crl", output, StringComparison.Ordinal);
            Assert.Contains("Connection failed", output, StringComparison.Ordinal);
            Assert.Contains("CSV: report.csv", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Result details:", output, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            await Log.CloseAndFlushAsync();
            Log.Logger = previousLogger;
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }
        }
    }

    /// <summary>
    /// Ensures error truncation shows top 3 and indicates remaining.
    /// </summary>
    [Fact]
    public static async Task SimplifiedModeTruncatesErrorsAt3()
    {
        var status = new ReportingStatus();
        status.RecordCsv("report.csv");
        var reporter = new ConsoleReporter(status, verbose: false);
        var diagnostics = new RunDiagnostics();
        var results = new List<CrlCheckResult>
        {
            new(new Uri("http://example.com/error1.crl"), CrlStatus.Error, TimeSpan.Zero, null, "Error 1", null, TimeSpan.Zero, 0, DateTime.UtcNow, null),
            new(new Uri("http://example.com/error2.crl"), CrlStatus.Error, TimeSpan.Zero, null, "Error 2", null, TimeSpan.Zero, 0, DateTime.UtcNow, null),
            new(new Uri("http://example.com/error3.crl"), CrlStatus.Error, TimeSpan.Zero, null, "Error 3", null, TimeSpan.Zero, 0, DateTime.UtcNow, null),
            new(new Uri("http://example.com/error4.crl"), CrlStatus.Error, TimeSpan.Zero, null, "Error 4", null, TimeSpan.Zero, 0, DateTime.UtcNow, null),
            new(new Uri("http://example.com/error5.crl"), CrlStatus.Error, TimeSpan.Zero, null, "Error 5", null, TimeSpan.Zero, 0, DateTime.UtcNow, null)
        };
        var run = new CrlCheckRun(results, diagnostics, DateTime.UtcNow);

        // Configure Serilog to write to temp file to avoid console interference
        var logFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.log");
        var previousLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(logFile, formatProvider: System.Globalization.CultureInfo.InvariantCulture)
            .CreateLogger();

        // Initialize licensing so ConsoleReporter can display license info
        try
        {
            await LicenseBootstrapper.EnsureLicensedAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // Test may run without valid license file
        catch
#pragma warning restore CA1031
        {
            // License validation may fail in test environment - that's ok
        }

        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            await reporter.ReportAsync(run, CancellationToken.None).ConfigureAwait(true);

            var output = writer.ToString();

            // Strip ANSI color codes for assertions (some environments enable colors even with StringWriter)
            var ansiPattern = @"\x1b\[[0-9;]*m";
            var cleanOutput = System.Text.RegularExpressions.Regex.Replace(output, ansiPattern, string.Empty);

            Assert.Contains("  - error1.crl: Error 1", cleanOutput, StringComparison.Ordinal);
            Assert.Contains("  - error2.crl: Error 2", cleanOutput, StringComparison.Ordinal);
            Assert.Contains("  - error3.crl: Error 3", cleanOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("  - error4.crl: Error 4", cleanOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("  - error5.crl: Error 5", cleanOutput, StringComparison.Ordinal);
            Assert.Contains("and 2 more", cleanOutput, StringComparison.Ordinal);
            Assert.Contains("full list in CSV/HTML report", cleanOutput, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            await Log.CloseAndFlushAsync();
            Log.Logger = previousLogger;
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }
        }
    }

    /// <summary>
    /// Ensures only written reports are shown in simplified mode.
    /// </summary>
    [Fact]
    public static async Task SimplifiedModeOnlyShowsWrittenReports()
    {
        var status = new ReportingStatus();
        var reporter = new ConsoleReporter(status, verbose: false);
        var run = new CrlCheckRun(
            new[] { new CrlCheckResult(new Uri("http://example.com/ok.crl"), CrlStatus.Ok, TimeSpan.Zero, null, null, null, TimeSpan.Zero, 0, DateTime.UtcNow, null) },
            new RunDiagnostics(),
            DateTime.UtcNow);

        // Configure Serilog to write to temp file to avoid console interference
        var logFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.log");
        var previousLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(logFile, formatProvider: System.Globalization.CultureInfo.InvariantCulture)
            .CreateLogger();

        // Initialize licensing so ConsoleReporter can display license info
        try
        {
            await LicenseBootstrapper.EnsureLicensedAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // Test may run without valid license file
        catch
#pragma warning restore CA1031
        {
            // License validation may fail in test environment - that's ok
        }

        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            await reporter.ReportAsync(run, CancellationToken.None).ConfigureAwait(true);

            var output = writer.ToString();
            Assert.DoesNotContain("CSV:", output, StringComparison.Ordinal);
            Assert.DoesNotContain("HTML:", output, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            await Log.CloseAndFlushAsync();
            Log.Logger = previousLogger;
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }
        }
    }
}
