using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Diagnostics;
using CrlMonitor.Models;
using CrlMonitor.Reporting;
using CrlMonitor.Tests.TestUtilities;
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
        var reporter = new CsvReporter(path, new ReportingStatus());
        var previousFetch = DateTime.UtcNow.AddHours(-2);
        var generatedAt = DateTime.UtcNow;
        var (parsed, _, _, _) = CrlTestBuilder.BuildParsedCrl(false);
        var checkedAt = generatedAt.AddMinutes(-5);
        var run = new CrlCheckRun(
            new[]
            {
                new CrlCheckResult(
                    new System.Uri("http://example.com"),
                    CrlStatus.Warning,
                    System.TimeSpan.FromSeconds(1),
                    parsed,
                    "Signature validation disabled.",
                    previousFetch,
                    TimeSpan.FromMilliseconds(120),
                    4096,
                    checkedAt,
                    "Valid"),
                new CrlCheckResult(
                    new System.Uri("ldap://dc1.example.com/CN=Example,O=Example Corp"),
                    CrlStatus.Error,
                    System.TimeSpan.FromMilliseconds(5),
                    null,
                    "Could not connect",
                    null,
                    null,
                    null,
                    generatedAt,
                    null)
            },
            new RunDiagnostics(),
            generatedAt);

        await reporter.ReportAsync(run, CancellationToken.None);

        Assert.True(File.Exists(path));
        var content = await File.ReadAllTextAsync(path);
        var formattedPrev = TimeFormatter.FormatUtc(previousFetch);
        var formattedRun = TimeFormatter.FormatUtc(generatedAt);
        Assert.Contains("URI,Issuer_Name,Status,This_Update_UTC,Next_Update_UTC,CRL_Size_bytes,Download_Duration_ms,Signature_Valid,Revoked_Count,Checked_Time_UTC,Previous_Checked_Time_UTC,CRL_Type,Status_Details", content, StringComparison.Ordinal);
        Assert.Contains("Issuer_Name", content, StringComparison.Ordinal);
        Assert.Contains("CN=CA", content, StringComparison.Ordinal);
        Assert.Contains("Full", content, StringComparison.Ordinal);
        Assert.Contains("Signature validation disabled", content, StringComparison.Ordinal);
        Assert.Contains(formattedPrev, content, StringComparison.Ordinal);
        Assert.Contains(formattedRun, content, StringComparison.Ordinal);
        Assert.Contains("4096", content, StringComparison.Ordinal);
        Assert.Contains("# report_generated_utc", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures signature status column uses expected values.
    /// </summary>
    [Fact]
    public static async Task ReportAsyncNormalizesSignatureStatus()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "signatures.csv");
        var reporter = new CsvReporter(path, new ReportingStatus());
        var timestamp = DateTime.UtcNow;
        var run = new CrlCheckRun(
            new[]
            {
                new CrlCheckResult(new Uri("http://valid"), CrlStatus.Ok, TimeSpan.Zero, null, null, null, null, null, timestamp, "Valid"),
                new CrlCheckResult(new Uri("http://invalid"), CrlStatus.Ok, TimeSpan.Zero, null, null, null, null, null, timestamp, "Invalid"),
                new CrlCheckResult(new Uri("http://skipped"), CrlStatus.Ok, TimeSpan.Zero, null, null, null, null, null, timestamp, "Skipped")
            },
            new RunDiagnostics(),
            timestamp);

        await reporter.ReportAsync(run, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains(",VALID,", content, StringComparison.Ordinal);
        Assert.Contains(",INVALID,", content, StringComparison.Ordinal);
        Assert.Contains(",DISABLED,", content, StringComparison.Ordinal);
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
