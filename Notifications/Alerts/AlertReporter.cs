using System.Text;
using CrlMonitor.Models;
using CrlMonitor.Notifications.Email;
using CrlMonitor.Reporting;
using CrlMonitor.State;

namespace CrlMonitor.Notifications.Alerts;

internal sealed class AlertReporter(AlertOptions options, IEmailClient emailClient, IStateStore stateStore, string? htmlReportUrl) : IReporter
{
    private readonly AlertOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IEmailClient _emailClient = emailClient ?? throw new ArgumentNullException(nameof(emailClient));
    private readonly IStateStore _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly HashSet<CrlStatus> _statusFilters = new(options.Statuses);
    private readonly string? _htmlReportUrl = htmlReportUrl;

    public async Task ReportAsync(CrlCheckRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!this._options.Enabled)
        {
            return;
        }

        var triggered = new List<AlertInstance>();
        foreach (var result in run.Results)
        {
            if (!this._statusFilters.Contains(result.Status))
            {
                continue;
            }

            var key = BuildStateKey(result.Status, result.Uri);
            var lastTriggered = await this._stateStore.GetAlertCooldownAsync(key, cancellationToken).ConfigureAwait(false);
            if (lastTriggered.HasValue && run.GeneratedAtUtc - lastTriggered.Value < this._options.Cooldown)
            {
                continue;
            }

            triggered.Add(new AlertInstance(result.Status, key, result));
        }

        if (triggered.Count == 0)
        {
            return;
        }

        var subject = BuildSubject(this._options.SubjectPrefix, triggered.Count);
        var body = BuildBody(triggered, this._options.IncludeDetails, this._htmlReportUrl);
        var message = new EmailMessage(this._options.Recipients, subject, body, []);
        await this._emailClient.SendAsync(message, this._options.Smtp, cancellationToken).ConfigureAwait(false);

        foreach (var alert in triggered)
        {
            await this._stateStore.SaveAlertCooldownAsync(alert.StateKey, run.GeneratedAtUtc, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildSubject(string prefix, int count)
    {
        var issues = count == 1 ? "issue" : "issues";
        return FormattableString.Invariant($"{prefix} {count} {issues} detected");
    }

    private static string BuildBody(IReadOnlyList<AlertInstance> alerts, bool includeDetails, string? htmlReportUrl)
    {
        var builder = new StringBuilder();
        _ = builder.AppendLine(FormattableString.Invariant($"{alerts.Count} issue(s) detected during the latest CRL check:"));
        _ = builder.AppendLine();
        for (var index = 0; index < alerts.Count; index++)
        {
            var alert = alerts[index];
            _ = builder.AppendLine("--------------------------------------------------");
            _ = builder.AppendLine(FormattableString.Invariant($"#{index + 1} {GetIssueTitle(alert)}"));
            _ = builder.AppendLine("--------------------------------------------------");
            _ = builder.AppendLine(FormattableString.Invariant($"URL: {alert.Result.Uri}"));
            _ = builder.AppendLine(FormattableString.Invariant($"Status: {alert.Result.Status.ToDisplayString()}"));
            _ = builder.AppendLine(FormattableString.Invariant($"Checked: {TimeFormatter.FormatUtc(alert.Result.CheckedAtUtc)}"));
            if (includeDetails && !string.IsNullOrWhiteSpace(alert.Result.ErrorInfo))
            {
                _ = builder.AppendLine(FormattableString.Invariant($"Details: {alert.Result.ErrorInfo}"));
            }

            var cause = includeDetails ? GetPossibleCause(alert) : null;
            if (!string.IsNullOrWhiteSpace(cause))
            {
                _ = builder.AppendLine(FormattableString.Invariant($"Possible cause: {cause}"));
            }

            _ = builder.AppendLine();
        }

        _ = builder.AppendLine("--------------------------------------------------");
        _ = builder.AppendLine("End of report");
        _ = builder.AppendLine("--------------------------------------------------");
        if (!string.IsNullOrWhiteSpace(htmlReportUrl))
        {
            _ = builder.AppendLine(FormattableString.Invariant($"View full report: {htmlReportUrl}"));
        }
        return builder.ToString();
    }

    private static string GetIssueTitle(AlertInstance alert)
    {
        var error = alert.Result.ErrorInfo ?? string.Empty;
        return error.Contains("signature", StringComparison.OrdinalIgnoreCase)
            ? "CRL Verification Failed"
            : alert.Result.Uri.Scheme.Equals("ldap", StringComparison.OrdinalIgnoreCase) ||
              error.Contains("LDAP", StringComparison.OrdinalIgnoreCase) ||
              error.Contains("connect", StringComparison.OrdinalIgnoreCase)
                ? "LDAP Connection Failed"
                : error.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                    ? "CRL Fetch Timed Out"
                    : "CRL Alert";
    }

    private static string? GetPossibleCause(AlertInstance alert)
    {
        var error = alert.Result.ErrorInfo ?? string.Empty;
        return error.Contains("signature", StringComparison.OrdinalIgnoreCase)
            ? "Mismatched issuer certificate or updated CRL signing key."
            : alert.Result.Uri.Scheme.Equals("ldap", StringComparison.OrdinalIgnoreCase) ||
              error.Contains("LDAP", StringComparison.OrdinalIgnoreCase) ||
              error.Contains("connect", StringComparison.OrdinalIgnoreCase)
                ? "Host unreachable, incorrect URI, or network/firewall issue."
                : error.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                    ? "Endpoint slow to respond or network latency."
                    : null;
    }

    private static string BuildStateKey(CrlStatus condition, Uri uri)
    {
        return FormattableString.Invariant($"{condition.ToDisplayString()}|{uri}");
    }

    private readonly record struct AlertInstance(CrlStatus Condition, string StateKey, CrlCheckResult Result);
}
