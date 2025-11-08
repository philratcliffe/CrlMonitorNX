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
        var reporter = new ConsoleReporter();
        var diagnostics = new RunDiagnostics();
        diagnostics.AddRuntimeWarning("Disk full");
        var fetchedAt = DateTime.UtcNow.AddHours(-1);
        var generatedAt = DateTime.UtcNow;
        var run = new CrlCheckRun(
            new[]
            {
                new CrlCheckResult(new Uri("http://example.com"), "WARNING", TimeSpan.Zero, null, "Signature validation disabled.", fetchedAt)
            },
            diagnostics,
            generatedAt);

        using var writer = new StringWriter();
        Console.SetOut(writer);

        await reporter.ReportAsync(run, CancellationToken.None);

        var output = writer.ToString();
        var expectedGenerated = generatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var expectedPrevious = fetchedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        Assert.Contains("CRL Monitor Report", output, StringComparison.Ordinal);
        Assert.Contains(expectedGenerated, output, StringComparison.Ordinal);
        Assert.Contains("URI", output, StringComparison.Ordinal);
        Assert.Contains("Status", output, StringComparison.Ordinal);
        Assert.Contains("WARNING", output, StringComparison.Ordinal);
        Assert.Contains("Signature validation disabled", output, StringComparison.Ordinal);
        Assert.Contains(expectedPrevious, output, StringComparison.Ordinal);
        Assert.Contains("Summary:", output, StringComparison.Ordinal);
        Assert.Contains("Disk full", output, StringComparison.Ordinal);
    }
}
