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
        Assert.Contains("CRL Errors", content, StringComparison.Ordinal);
        Assert.Contains("http://example.com", content, StringComparison.Ordinal);
        Assert.Contains("http://bad", content, StringComparison.Ordinal);
        Assert.Contains("ERROR", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures short HTTP/HTTPS URIs are wrapped in clickable anchor tags.
    /// </summary>
    [Fact]
    public static async Task WriteAsyncWrapsShortHttpUrisInAnchorTags()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "report.html");
        var now = DateTime.UtcNow;
        var shortUri = "http://example.com/crl.crl";
        var results = new[]
        {
            new CrlCheckResult(new Uri(shortUri), CrlStatus.Ok, TimeSpan.Zero, null, null, null, null, null, now, "Valid")
        };
        var run = new CrlCheckRun(results, new Diagnostics.RunDiagnostics(), now);

        await HtmlReportWriter.WriteAsync(path, run, CancellationToken.None).ConfigureAwait(true);

        var content = await File.ReadAllTextAsync(path).ConfigureAwait(true);
        Assert.Contains($"<a href=\"{shortUri}\">{shortUri}</a>", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures long HTTP/HTTPS URIs are wrapped in clickable anchor tags (both truncated and full versions).
    /// </summary>
    [Fact]
    public static async Task WriteAsyncWrapsLongHttpUrisInAnchorTags()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "report.html");
        var now = DateTime.UtcNow;
        var longUri = "http://example.com/very/long/path/to/certificate/revocation/list.crl";
        var results = new[]
        {
            new CrlCheckResult(new Uri(longUri), CrlStatus.Ok, TimeSpan.Zero, null, null, null, null, null, now, "Valid")
        };
        var run = new CrlCheckRun(results, new Diagnostics.RunDiagnostics(), now);

        await HtmlReportWriter.WriteAsync(path, run, CancellationToken.None).ConfigureAwait(true);

        var content = await File.ReadAllTextAsync(path).ConfigureAwait(true);
        // Both truncated and full versions should be wrapped in anchor tags
        var truncated = longUri[0..40] + "...";
        Assert.Contains($"<a href=\"{longUri}\">{truncated}</a>", content, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"{longUri}\">{longUri}</a>", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures non-HTTP URIs (ldap, file) are NOT wrapped in anchor tags for security.
    /// </summary>
    [Fact]
    public static async Task WriteAsyncDoesNotWrapNonHttpUrisInAnchorTags()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "report.html");
        var now = DateTime.UtcNow;
        var ldapUri = "ldap://dc.example.com/CN=Test";
        var fileUri = "file:///tmp/test.crl";
        var results = new[]
        {
            new CrlCheckResult(new Uri(ldapUri), CrlStatus.Ok, TimeSpan.Zero, null, null, null, null, null, now, "Valid"),
            new CrlCheckResult(new Uri(fileUri), CrlStatus.Ok, TimeSpan.Zero, null, null, null, null, null, now, "Valid")
        };
        var run = new CrlCheckRun(results, new Diagnostics.RunDiagnostics(), now);

        await HtmlReportWriter.WriteAsync(path, run, CancellationToken.None).ConfigureAwait(true);

        var content = await File.ReadAllTextAsync(path).ConfigureAwait(true);
        // Should NOT contain anchor tags for ldap or file URIs
        Assert.DoesNotContain($"<a href=\"{ldapUri}\">", content, StringComparison.Ordinal);
        Assert.DoesNotContain($"<a href=\"{fileUri}\">", content, StringComparison.Ordinal);
        // But URIs should still appear as text
        Assert.Contains(ldapUri, content, StringComparison.Ordinal);
        Assert.Contains(fileUri, content, StringComparison.Ordinal);
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
