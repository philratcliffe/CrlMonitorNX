using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Diagnostics;
using CrlMonitor.Models;
using CrlMonitor.Notifications;
using CrlMonitor.Reporting;
using CrlMonitor.State;
using Xunit;

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
        var reporter = new EmailReportReporter(options, client, state, reportingStatus);
        var run = BuildRun();

        await reporter.ReportAsync(run, CancellationToken.None);

        Assert.True(client.WasSent);
        Assert.Equal("Test Report", client.LastMessage!.Subject);
        Assert.Single(client.LastMessage.Recipients);
        Assert.Contains("CRLs Checked:", client.LastMessage.Body, StringComparison.Ordinal);
        Assert.Contains("CRLs Expiring:", client.LastMessage.Body, StringComparison.Ordinal);
        Assert.NotEmpty(client.LastMessage.Attachments);
        Assert.NotNull(await state.GetLastReportSentAsync(CancellationToken.None));
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
        var reporter = new EmailReportReporter(options, client, state, reportingStatus);
        var run = BuildRun();

        await reporter.ReportAsync(run, CancellationToken.None);

        Assert.True(client.WasSent);
        Assert.True(reportingStatus.EmailReportSent);
    }

    private static CrlCheckRun BuildRun()
    {
        var now = DateTime.UtcNow;
        var results = new List<CrlCheckResult>
        {
            new(new Uri("http://valid"), "OK", TimeSpan.FromMilliseconds(10), null, null, null, null, null, now, "Valid"),
            new(new Uri("http://expired"), "EXPIRED", TimeSpan.FromMilliseconds(15), null, "Expired", null, null, null, now, "Invalid")
        };
        return new CrlCheckRun(results, new RunDiagnostics(), now);
    }

    private sealed class InMemoryStateStore : IStateStore
    {
        public DateTime? LastReportSentUtc { get; set; }

        public Task<DateTime?> GetLastFetchAsync(Uri uri, CancellationToken cancellationToken) =>
            Task.FromResult<DateTime?>(null);

        public Task SaveLastFetchAsync(Uri uri, DateTime fetchedAtUtc, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<DateTime?> GetLastReportSentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(LastReportSentUtc);

        public Task SaveLastReportSentAsync(DateTime sentAtUtc, CancellationToken cancellationToken)
        {
            LastReportSentUtc = sentAtUtc;
            return Task.CompletedTask;
        }

        public Task<DateTime?> GetAlertCooldownAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult<DateTime?>(null);

        public Task SaveAlertCooldownAsync(string key, DateTime triggeredAtUtc, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RecordingEmailClient : IEmailClient
    {
        public bool WasSent => LastMessage != null;
        public EmailMessage? LastMessage { get; private set; }

        public Task SendAsync(EmailMessage message, SmtpOptions options, CancellationToken cancellationToken)
        {
            LastMessage = message;
            return Task.CompletedTask;
        }
    }
}
