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
    private readonly HashSet<string> _statusFilters;

    public AlertReporter(AlertOptions options, IEmailClient emailClient, IStateStore stateStore)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _emailClient = emailClient ?? throw new ArgumentNullException(nameof(emailClient));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _statusFilters = new HashSet<string>(options.Statuses, StringComparer.OrdinalIgnoreCase);
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
        var body = BuildBody(triggered, _options.IncludeDetails);
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

    private static string BuildBody(IReadOnlyCollection<AlertInstance> alerts, bool includeDetails)
    {
        var builder = new StringBuilder();
        builder.AppendLine(FormattableString.Invariant($"Alerts generated at {DateTime.UtcNow:u}"));
        foreach (var group in alerts.GroupBy(a => a.Condition, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(FormattableString.Invariant($"{group.Key.ToUpperInvariant()}:"));
            foreach (var alert in group)
            {
                builder.AppendLine(FormattableString.Invariant($"- {alert.Result.Uri}"));
                if (includeDetails)
                {
                    builder.AppendLine(FormattableString.Invariant($"  Status: {alert.Result.Status}"));
                    builder.AppendLine(FormattableString.Invariant($"  Checked: {alert.Result.CheckedAtUtc:u}"));
                    if (!string.IsNullOrWhiteSpace(alert.Result.ErrorInfo))
                    {
                        builder.AppendLine(FormattableString.Invariant($"  Details: {alert.Result.ErrorInfo}"));
                    }
                }
            }
        }

        return builder.ToString();
    }

    private static string BuildStateKey(string condition, Uri uri)
    {
        return FormattableString.Invariant($"{condition}|{uri}");
    }

    private readonly record struct AlertInstance(string Condition, string StateKey, CrlCheckResult Result);
}
