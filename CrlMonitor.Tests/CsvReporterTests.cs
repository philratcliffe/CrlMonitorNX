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
        var run = new CrlCheckRun(
            new[]
            {
                new CrlCheckResult(new System.Uri("http://example.com"), "WARNING", System.TimeSpan.FromSeconds(1), null, "Signature validation disabled."),
                new CrlCheckResult(new System.Uri("ldap://dc1.example.com/CN=Example,O=Example Corp"), "ERROR", System.TimeSpan.FromMilliseconds(5), null, "Could not connect")
            },
            new RunDiagnostics());

        await reporter.ReportAsync(run, CancellationToken.None);

        Assert.True(File.Exists(path));
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("status_info", content, StringComparison.Ordinal);
        Assert.Contains("\"ldap://dc1.example.com/CN=Example,O=Example Corp\"", content, StringComparison.Ordinal);
        Assert.Contains("WARNING", content, StringComparison.Ordinal);
        Assert.Contains("Signature validation disabled", content, StringComparison.Ordinal);
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
