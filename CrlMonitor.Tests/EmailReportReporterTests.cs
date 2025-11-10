using CrlMonitor.Diagnostics;
using CrlMonitor.Models;
using CrlMonitor.Notifications;
using CrlMonitor.Reporting;
using CrlMonitor.Notifications.Email;
using CrlMonitor.Notifications.Reports;
using CrlMonitor.State;

namespace CrlMonitor.Tests;

/// <summary>
/// Exercises the email report reporter.
/// </summary>
public static class EmailReportReporterTests
{
    /// <summary>
    /// Ensures an email is dispatched when the reporting interval has elapsed.
    /// </summary>
    [Fact]
    public static async Task SendsEmailWhenIntervalElapsed()
    {
        var options = new ReportOptions(
            true,
            ReportFrequency.Daily,
            new List<string> { "ops@example.com" },
            "Test Report",
            IncludeSummary: true,
            IncludeFullCsv: true,
            new SmtpOptions("smtp.example.com", 25, "svc", "pw", "sender@example.com", true));
        var state = new InMemoryStateStore();
        var client = new RecordingEmailClient();
        var reportingStatus = new ReportingStatus();
        var reporter = new EmailReportReporter(options, client, state, reportingStatus, "http://example.com/report.html");
        var run = BuildRun();

        await reporter.ReportAsync(run, CancellationToken.None).ConfigureAwait(true);

        Assert.True(client.WasSent);
        Assert.Equal("Test Report", client.LastMessage!.Subject);
        _ = Assert.Single(client.LastMessage.Recipients);
        Assert.Contains("CRLs Checked:", client.LastMessage.Body, StringComparison.Ordinal);
        Assert.Contains("CRLs Expiring:", client.LastMessage.Body, StringComparison.Ordinal);
        Assert.Contains("View full HTML report: http://example.com/report.html", client.LastMessage.Body, StringComparison.Ordinal);
        Assert.NotEmpty(client.LastMessage.Attachments);
        _ = Assert.NotNull(await state.GetLastReportSentAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.True(reportingStatus.EmailReportSent);
    }

    /// <summary>
    /// Ensures reporter still sends even if a report was recently sent.
    /// </summary>
    [Fact]
    public static async Task SendsWhenRecentlySent()
    {
        var options = new ReportOptions(
            true,
            ReportFrequency.Daily,
            new List<string> { "ops@example.com" },
            "Test Report",
            IncludeSummary: true,
            IncludeFullCsv: true,
            new SmtpOptions("smtp.example.com", 25, "svc", "pw", "sender@example.com", true));
        var state = new InMemoryStateStore { LastReportSentUtc = DateTime.UtcNow.AddHours(-2) };
        var client = new RecordingEmailClient();
        var reportingStatus = new ReportingStatus();
        var reporter = new EmailReportReporter(options, client, state, reportingStatus, "http://example.com/report.html");
        var run = BuildRun();

        await reporter.ReportAsync(run, CancellationToken.None).ConfigureAwait(true);

        Assert.True(client.WasSent);
        Assert.True(reportingStatus.EmailReportSent);
    }

    private static CrlCheckRun BuildRun()
    {
        var now = DateTime.UtcNow;
        var results = new List<CrlCheckResult>
        {
            new(new Uri("http://valid"), CrlStatus.Ok, TimeSpan.FromMilliseconds(10), null, null, null, null, null, now, "Valid"),
            new(new Uri("http://expired"), CrlStatus.Expired, TimeSpan.FromMilliseconds(15), null, "Expired", null, null, null, now, "Invalid")
        };
        return new CrlCheckRun(results, new RunDiagnostics(), now);
    }

    private sealed class InMemoryStateStore : IStateStore
    {
        public DateTime? LastReportSentUtc { get; set; }

        public Task<DateTime?> GetLastFetchAsync(Uri uri, CancellationToken cancellationToken)
        {
            return Task.FromResult<DateTime?>(null);
        }

        public Task SaveLastFetchAsync(Uri uri, DateTime fetchedAtUtc, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<DateTime?> GetLastReportSentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(this.LastReportSentUtc);
        }

        public Task SaveLastReportSentAsync(DateTime sentAtUtc, CancellationToken cancellationToken)
        {
            this.LastReportSentUtc = sentAtUtc;
            return Task.CompletedTask;
        }

        public Task<DateTime?> GetAlertCooldownAsync(string key, CancellationToken cancellationToken)
        {
            return Task.FromResult<DateTime?>(null);
        }

        public Task SaveAlertCooldownAsync(string key, DateTime triggeredAtUtc, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEmailClient : IEmailClient
    {
        public bool WasSent => this.LastMessage != null;
        public EmailMessage? LastMessage { get; private set; }

        public Task SendAsync(EmailMessage message, SmtpOptions options, CancellationToken cancellationToken)
        {
            this.LastMessage = message;
            return Task.CompletedTask;
        }
    }
}
