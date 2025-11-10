using CrlMonitor.Models;
using CrlMonitor.Notifications;
using CrlMonitor.Notifications.Alerts;
using CrlMonitor.Notifications.Email;
using CrlMonitor.State;

namespace CrlMonitor.Tests;

/// <summary>
/// Validates alert dispatch logic.
/// </summary>
public static class AlertReporterTests
{
    /// <summary>
    /// Ensures alerts fire when matching statuses are selected.
    /// </summary>
    [Fact]
    public static async Task SendsAlertForSelectedStatuses()
    {
        var options = BuildOptions(CrlStatus.Error, CrlStatus.Expired, CrlStatus.Expiring);
        var state = new RecordingAlertStateStore();
        var client = new RecordingEmailClient();
        var reporter = new AlertReporter(options, client, state, "https://example.com/crl/report.html");
        var run = BuildRun();

        await reporter.ReportAsync(run, CancellationToken.None).ConfigureAwait(true);

        Assert.True(client.WasSent);
        Assert.Contains("[CRL Alert]", client.LastMessage!.Subject, StringComparison.Ordinal);
        Assert.Contains("http://expired", client.LastMessage.Body, StringComparison.Ordinal);
        Assert.Contains("http://expiring", client.LastMessage.Body, StringComparison.Ordinal);
        Assert.Contains("http://failed", client.LastMessage.Body, StringComparison.Ordinal);
        Assert.Contains("View full report: https://example.com/crl/report.html", client.LastMessage.Body, StringComparison.Ordinal);
        Assert.NotEmpty(state.SavedKeys);
    }

    /// <summary>
    /// Ensures cooldown suppression works for repeated statuses.
    /// </summary>
    [Fact]
    public static async Task SuppressesAlertWhenCooldownActive()
    {
        var options = BuildOptions(CrlStatus.Expired);
        var state = new RecordingAlertStateStore {
            Cooldowns = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
            {
                { "EXPIRED|http://expired/", DateTime.UtcNow }
            }
        };
        var client = new RecordingEmailClient();
        var reporter = new AlertReporter(options, client, state, null);

        await reporter.ReportAsync(BuildRun(), CancellationToken.None).ConfigureAwait(true);

        Assert.False(client.WasSent);
    }

    private static AlertOptions BuildOptions(params CrlStatus[] statuses)
    {
        return new AlertOptions(
            Enabled: true,
            Recipients: new List<string> { "oncall@example.com" },
            Statuses: statuses,
            Cooldown: TimeSpan.FromHours(6),
            SubjectPrefix: "[CRL Alert]",
            IncludeDetails: true,
            Smtp: new SmtpOptions("smtp.example.com", 25, "svc", "pw", "sender@example.com", true));
    }

    private static CrlCheckRun BuildRun()
    {
        var now = DateTime.UtcNow;
        var results = new List<CrlCheckResult>
        {
            new(new Uri("http://expired"), CrlStatus.Expired, TimeSpan.FromMilliseconds(10), null, "Expired", null, null, null, now, "Valid"),
            new(new Uri("http://expiring"), CrlStatus.Expiring, TimeSpan.FromMilliseconds(12), null, "Expiring soon", null, null, null, now, "Valid"),
            new(new Uri("http://failed"), CrlStatus.Error, TimeSpan.FromMilliseconds(10), null, "Failed fetch", null, null, null, now, null)
        };
        return new CrlCheckRun(results, new Diagnostics.RunDiagnostics(), now);
    }

    private sealed class RecordingAlertStateStore : IStateStore
    {
        public HashSet<string> SavedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DateTime> Cooldowns { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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
            return Task.FromResult<DateTime?>(null);
        }

        public Task SaveLastReportSentAsync(DateTime sentAtUtc, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<DateTime?> GetAlertCooldownAsync(string key, CancellationToken cancellationToken)
        {
            return this.Cooldowns.TryGetValue(key, out var timestamp)
                ? Task.FromResult<DateTime?>(timestamp)
                : Task.FromResult<DateTime?>(null);
        }

        public Task SaveAlertCooldownAsync(string key, DateTime triggeredAtUtc, CancellationToken cancellationToken)
        {
            this.Cooldowns[key] = triggeredAtUtc;
            _ = this.SavedKeys.Add(key);
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
