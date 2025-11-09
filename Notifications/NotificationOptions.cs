using System;
using System.Collections.Generic;
using CrlMonitor.Models;

namespace CrlMonitor.Notifications;

internal enum ReportFrequency
{
    Daily,
    Weekly
}

internal sealed record SmtpOptions(
    string Host,
    int Port,
    string Username,
    string Password,
    string From,
    bool EnableStartTls);

internal sealed record ReportOptions(
    bool Enabled,
    ReportFrequency Frequency,
    IReadOnlyList<string> Recipients,
    string Subject,
    bool IncludeSummary,
    bool IncludeFullCsv,
    SmtpOptions Smtp);

internal sealed record AlertOptions(
    bool Enabled,
    IReadOnlyList<string> Recipients,
    IReadOnlyList<CrlStatus> Statuses,
    TimeSpan Cooldown,
    string SubjectPrefix,
    bool IncludeDetails,
    SmtpOptions Smtp);
