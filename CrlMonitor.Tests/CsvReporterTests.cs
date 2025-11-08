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
/// Validates CSV reporter on simple input.
/// </summary>
public static class CsvReporterTests
{
    /// <summary>
    /// Ensures CSV output is generated.
    /// </summary>
    [Fact]
    public static async Task ReportAsyncCreatesFile()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "run.csv");
        var reporter = new CsvReporter(path, appendTimestamp: false);
        var previousFetch = DateTime.UtcNow.AddHours(-2);
        var generatedAt = DateTime.UtcNow;
        var run = new CrlCheckRun(
            new[]
            {
                new CrlCheckResult(new System.Uri("http://example.com"), "WARNING", System.TimeSpan.FromSeconds(1), null, "Signature validation disabled.", previousFetch),
                new CrlCheckResult(new System.Uri("ldap://dc1.example.com/CN=Example,O=Example Corp"), "ERROR", System.TimeSpan.FromMilliseconds(5), null, "Could not connect", null)
            },
            new RunDiagnostics(),
            generatedAt);

        await reporter.ReportAsync(run, CancellationToken.None);

        Assert.True(File.Exists(path));
        var content = await File.ReadAllTextAsync(path);
        var formattedPrev = previousFetch.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var formattedRun = generatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        Assert.Contains("previous_fetch_utc", content, StringComparison.Ordinal);
        Assert.Contains("\"ldap://dc1.example.com/CN=Example,O=Example Corp\"", content, StringComparison.Ordinal);
        Assert.Contains(formattedPrev, content, StringComparison.Ordinal);
        Assert.Contains(formattedRun, content, StringComparison.Ordinal);
        Assert.Contains("WARNING", content, StringComparison.Ordinal);
        Assert.Contains("Signature validation disabled", content, StringComparison.Ordinal);
        Assert.Contains("# report_generated_utc", content, StringComparison.Ordinal);
    }

    private sealed class TempFolder : System.IDisposable
    {
        public string Path { get; } = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString())).FullName;

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Path, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
