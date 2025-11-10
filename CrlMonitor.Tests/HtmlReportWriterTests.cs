using CrlMonitor.Models;
using CrlMonitor.Reporting;

namespace CrlMonitor.Tests;

/// <summary>
/// Validates HTML report generation.
/// </summary>
public static class HtmlReportWriterTests
{
    /// <summary>
    /// Ensures summary and rows land in the generated file.
    /// </summary>
    [Fact]
    public static async Task WriteAsyncCreatesSummaryAndTable()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "report.html");
        var now = DateTime.UtcNow;
        var results = new[]
        {
            new CrlCheckResult(new Uri("http://example.com"), CrlStatus.Ok, TimeSpan.Zero, null, null, null, null, null, now, "Valid"),
            new CrlCheckResult(new Uri("http://bad"), CrlStatus.Error, TimeSpan.Zero, null, "Failed", null, null, null, now, "Invalid")
        };
        var run = new CrlCheckRun(results, new Diagnostics.RunDiagnostics(), now);

        await HtmlReportWriter.WriteAsync(path, run, CancellationToken.None).ConfigureAwait(true);

        Assert.True(File.Exists(path));
        var content = await File.ReadAllTextAsync(path).ConfigureAwait(true);
        Assert.Contains("CRLs Checked", content, StringComparison.Ordinal);
        Assert.Contains("CRLs Failed", content, StringComparison.Ordinal);
        Assert.Contains("http://example.com", content, StringComparison.Ordinal);
        Assert.Contains("http://bad", content, StringComparison.Ordinal);
        Assert.Contains("ERROR", content, StringComparison.Ordinal);
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString())).FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(this.Path, true);
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
