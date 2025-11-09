using System;
using System.Collections.Generic;
using CrlMonitor.Notifications;

namespace CrlMonitor;

internal sealed record RunOptions(
    bool ConsoleReports,
    bool CsvReports,
    string CsvOutputPath,
    bool CsvAppendTimestamp,
    string? HtmlReportPath,
    TimeSpan FetchTimeout,
    int MaxParallelFetches,
    string StateFilePath,
    IReadOnlyList<CrlConfigEntry> Crls,
    ReportOptions? Reports,
    AlertOptions? Alerts);
