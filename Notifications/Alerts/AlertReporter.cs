using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Models;
using CrlMonitor.Reporting;
using CrlMonitor.State;

namespace CrlMonitor.Notifications;

internal sealed class AlertReporter : IReporter
{
    private readonly AlertOptions _options;
    private readonly IEmailClient _emailClient;
    private readonly IStateStore _stateStore;
    private readonly HashSet<CrlStatus> _statusFilters;
    private readonly string? _htmlReportUrl;

    public AlertReporter(AlertOptions options, IEmailClient emailClient, IStateStore stateStore, string? htmlReportUrl)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _emailClient = emailClient ?? throw new ArgumentNullException(nameof(emailClient));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _statusFilters = new HashSet<CrlStatus>(options.Statuses);
        _htmlReportUrl = htmlReportUrl;
    }

    public async Task ReportAsync(CrlCheckRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!_options.Enabled)
        {
            return;
        }

        var triggered = new List<AlertInstance>();
        foreach (var result in run.Results)
        {
            if (!_statusFilters.Contains(result.Status))
            {
                continue;
            }

            var key = BuildStateKey(result.Status, result.Uri);
            var lastTriggered = await _stateStore.GetAlertCooldownAsync(key, cancellationToken).ConfigureAwait(false);
            if (lastTriggered.HasValue && run.GeneratedAtUtc - lastTriggered.Value < _options.Cooldown)
            {
                continue;
            }

            triggered.Add(new AlertInstance(result.Status, key, result));
        }

        if (triggered.Count == 0)
        {
            return;
        }

        var subject = BuildSubject(_options.SubjectPrefix, triggered.Count);
        var body = BuildBody(triggered, _options.IncludeDetails, _htmlReportUrl);
        var message = new EmailMessage(_options.Recipients, subject, body, Array.Empty<EmailAttachment>());
        await _emailClient.SendAsync(message, _options.Smtp, cancellationToken).ConfigureAwait(false);

        foreach (var alert in triggered)
        {
            await _stateStore.SaveAlertCooldownAsync(alert.StateKey, run.GeneratedAtUtc, cancellationToken).ConfigureAwait(false);
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
        builder.AppendLine(FormattableString.Invariant($"{alerts.Count} issue(s) detected during the latest CRL check:"));
        builder.AppendLine();
        for (var index = 0; index < alerts.Count; index++)
        {
            var alert = alerts[index];
            builder.AppendLine("--------------------------------------------------");
            builder.AppendLine(FormattableString.Invariant($"#{index + 1} {GetIssueTitle(alert)}"));
            builder.AppendLine("--------------------------------------------------");
            builder.AppendLine(FormattableString.Invariant($"URL: {alert.Result.Uri}"));
            builder.AppendLine(FormattableString.Invariant($"Status: {alert.Result.Status.ToDisplayString()}"));
            builder.AppendLine(FormattableString.Invariant($"Checked: {TimeFormatter.FormatUtc(alert.Result.CheckedAtUtc)}"));
            if (includeDetails && !string.IsNullOrWhiteSpace(alert.Result.ErrorInfo))
            {
                builder.AppendLine(FormattableString.Invariant($"Details: {alert.Result.ErrorInfo}"));
            }

            var cause = includeDetails ? GetPossibleCause(alert) : null;
            if (!string.IsNullOrWhiteSpace(cause))
            {
                builder.AppendLine(FormattableString.Invariant($"Possible cause: {cause}"));
            }

            builder.AppendLine();
        }

        builder.AppendLine("--------------------------------------------------");
        builder.AppendLine("End of report");
        builder.AppendLine("--------------------------------------------------");
        if (!string.IsNullOrWhiteSpace(htmlReportUrl))
        {
            builder.AppendLine(FormattableString.Invariant($"View full report: {htmlReportUrl}"));
        }
        return builder.ToString();
    }

    private static string GetIssueTitle(AlertInstance alert)
    {
        var error = alert.Result.ErrorInfo ?? string.Empty;
        if (error.Contains("signature", StringComparison.OrdinalIgnoreCase))
        {
            return "CRL Verification Failed";
        }

        if (alert.Result.Uri.Scheme.Equals("ldap", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("LDAP", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("connect", StringComparison.OrdinalIgnoreCase))
        {
            return "LDAP Connection Failed";
        }

        if (error.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "CRL Fetch Timed Out";
        }

        return "CRL Alert";
    }

    private static string? GetPossibleCause(AlertInstance alert)
    {
        var error = alert.Result.ErrorInfo ?? string.Empty;
        if (error.Contains("signature", StringComparison.OrdinalIgnoreCase))
        {
            return "Mismatched issuer certificate or updated CRL signing key.";
        }

        if (alert.Result.Uri.Scheme.Equals("ldap", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("LDAP", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("connect", StringComparison.OrdinalIgnoreCase))
        {
            return "Host unreachable, incorrect URI, or network/firewall issue.";
        }

        if (error.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "Endpoint slow to respond or network latency.";
        }

        return null;
    }

    private static string BuildStateKey(CrlStatus condition, Uri uri)
    {
        return FormattableString.Invariant($"{condition.ToDisplayString()}|{uri}");
    }

    private readonly record struct AlertInstance(CrlStatus Condition, string StateKey, CrlCheckResult Result);
}
