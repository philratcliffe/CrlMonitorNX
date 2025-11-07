using System;
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
        var run = new CrlCheckRun(Array.Empty<CrlCheckResult>(), diagnostics);

        using var writer = new StringWriter();
        Console.SetOut(writer);

        await reporter.ReportAsync(run, CancellationToken.None);

        var output = writer.ToString();
        Assert.Contains("Disk full", output, StringComparison.Ordinal);
    }
}
