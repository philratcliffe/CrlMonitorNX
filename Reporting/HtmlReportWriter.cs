using System.Globalization;
using System.Text;
using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal static class HtmlReportWriter
{
    public static async Task WriteAsync(string path, CrlCheckRun run, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(run);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        var html = BuildHtml(run);
        await File.WriteAllTextAsync(path, html, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildHtml(CrlCheckRun run)
    {
        var summary = BuildSummary(run.Results);
        var builder = new StringBuilder();
        _ = builder.AppendLine("<!DOCTYPE html>");
        _ = builder.AppendLine("<html lang=\"en\"><head>");
        _ = builder.AppendLine("<meta charset=\"utf-8\" />");
        _ = builder.AppendLine("<title>CRL Health Report</title>");
        _ = builder.AppendLine("<style>");
        _ = builder.AppendLine("body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f5f7fb;color:#1f2937;margin:0;padding:0;}");
        _ = builder.AppendLine(".container{max-width:1200px;margin:0 auto;padding:32px;}");
        _ = builder.AppendLine(".card{background:#fff;border-radius:16px;box-shadow:0 10px 30px rgba(15,23,42,.1);padding:32px;margin-bottom:32px;}");
        _ = builder.AppendLine(".summary-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:16px;}");
        _ = builder.AppendLine(".summary-card{padding:16px;border-radius:12px;background:#f9fafb;border:1px solid #e5e7eb;}");
        _ = builder.AppendLine(".summary-label{font-size:14px;color:#6b7280;text-transform:uppercase;letter-spacing:.05em;}");
        _ = builder.AppendLine(".summary-value{font-size:28px;font-weight:600;color:#111827;margin-top:4px;}");
        _ = builder.AppendLine(".table-wrapper{overflow-x:auto;}");
        _ = builder.AppendLine("table{width:100%;border-collapse:collapse;margin-top:16px;font-size:14px;}");
        _ = builder.AppendLine("th{background:#111827;color:#f9fafb;text-align:left;padding:12px;border-bottom:2px solid #0f172a;}");
        _ = builder.AppendLine("td{padding:12px;border-bottom:1px solid #e5e7eb;line-height:1.4;}");
        _ = builder.AppendLine("tr:nth-child(even){background:#f9fafb;}");
        _ = builder.AppendLine(".status-OK{color:#16a34a;font-weight:600;}");
        _ = builder.AppendLine(".status-WARNING,.status-EXPIRING{color:#f97316;font-weight:600;}");
        _ = builder.AppendLine(".status-EXPIRED,.status-ERROR{color:#dc2626;font-weight:600;}");
        _ = builder.AppendLine("tr.row-ERROR{background:#fee2e2;}");
        _ = builder.AppendLine("tr.row-EXPIRED{background:#fee2e2;}");
        _ = builder.AppendLine(".uri-toggle{color:#2563eb;text-decoration:none;font-size:12px;margin-left:4px;}");
        _ = builder.AppendLine(".uri-toggle:hover{text-decoration:underline;}");
        _ = builder.AppendLine(".uri-full{white-space:nowrap;margin-left:4px;}");
        _ = builder.AppendLine(".issuer{white-space:normal;word-break:keep-all;}");
        _ = builder.AppendLine("</style>");
        _ = builder.AppendLine("<script>");
        _ = builder.AppendLine("function toggleUri(id){var full=document.getElementById(id+'-full');var short=document.getElementById(id+'-short');var link=document.getElementById(id+'-link');if(full.style.display==='none'){full.style.display='inline';short.style.display='none';link.textContent='(hide)';}else{full.style.display='none';short.style.display='inline';link.textContent='(show)';}}");
        _ = builder.AppendLine("</script>");
        _ = builder.AppendLine("</head><body>");
        _ = builder.AppendLine("<div class=\"container\">");
        _ = builder.AppendLine("<div class=\"card\">");
        _ = builder.AppendLine(FormattableString.Invariant($"<h1>CRL Health Report</h1><p>Generated at {TimeFormatter.FormatUtc(run.GeneratedAtUtc)}</p>"));
        _ = builder.AppendLine("<div class=\"summary-grid\">");
        AppendSummaryCard(builder, "CRLs Checked", summary.Total, null);
        AppendSummaryCard(builder, "CRLs OK", summary.Ok, null);
        AppendSummaryCard(builder, "CRLs Warning", summary.Warning, null);
        AppendSummaryCard(builder, "CRLs Expiring", summary.Expiring, null);
        AppendSummaryCard(builder, "CRLs Expired", summary.Expired, summary.Expired > 0 ? "#dc2626" : null);
        AppendSummaryCard(builder, "CRLs Failed", summary.Errors, summary.Errors > 0 ? "#dc2626" : null);
        _ = builder.AppendLine("</div></div>");
        _ = builder.AppendLine("<div class=\"card table-wrapper\">");
        _ = builder.AppendLine("<table><thead><tr>");
        _ = builder.AppendLine("<th>URI</th><th>Issuer</th><th>Status</th><th>This Update (UTC)</th><th>Next Update (UTC)</th><th>CRL Size</th><th>Download (ms)</th><th>Signature</th><th>Revocations</th><th>Checked (UTC)</th><th>Previous (UTC)</th><th>Type</th><th>Details</th>");
        _ = builder.AppendLine("</tr></thead><tbody>");
        for (var i = 0; i < run.Results.Count; i++)
        {
            AppendRow(builder, run.Results[i], i);
        }
        _ = builder.AppendLine("</tbody></table></div></div>");
        _ = builder.AppendLine(FormattableString.Invariant($"<footer style=\"text-align:center;color:#6b7280;font-size:12px;margin-top:32px;\">Generated by CRL Monitor {GetVersion()} — © {DateTime.UtcNow:yyyy} Red Kestrel Consulting Limited</footer>"));
        _ = builder.AppendLine("</div></body></html>");
        return builder.ToString();
    }

    private static void AppendSummaryCard(StringBuilder builder, string label, int value, string? color)
    {
        var valueStyle = string.IsNullOrWhiteSpace(color) ? "summary-value" : $"summary-value\" style=\"color:{color}";
        _ = builder.AppendLine("<div class=\"summary-card\">");
        _ = builder.AppendLine(FormattableString.Invariant($"<div class=\"summary-label\">{label}</div>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<div class=\"{valueStyle}\">{value}</div>"));
        _ = builder.AppendLine("</div>");
    }

    private static void AppendRow(StringBuilder builder, CrlCheckResult result, int rowIndex)
    {
        var parsed = result.ParsedCrl;
        var statusClass = $"status-{result.Status.ToDisplayString()}";
        var rowClass = result.Status is CrlStatus.Error or CrlStatus.Expired ? " class=\"row-" + result.Status.ToDisplayString() + "\"" : string.Empty;
        _ = builder.AppendLine("<tr" + rowClass + ">");

        // Collapsible URI
        var fullUri = result.Uri.ToString();
        var escapedUri = Escape(fullUri);
        const int maxUriLength = 40;
        if (fullUri.Length > maxUriLength)
        {
            var truncated = Escape(fullUri[0..maxUriLength] + "...");
            var uriId = FormattableString.Invariant($"uri{rowIndex}");
            _ = builder.Append(FormattableString.Invariant($"<td><span id=\"{uriId}-short\" class=\"uri-short\">"));
            _ = builder.Append(truncated);
            _ = builder.Append("</span>&nbsp;");
            _ = builder.Append(FormattableString.Invariant($"<a href=\"#\" class=\"uri-toggle\" id=\"{uriId}-link\" onclick=\"toggleUri('{uriId}'); return false;\">(show)</a>"));
            _ = builder.Append(FormattableString.Invariant($"<span id=\"{uriId}-full\" class=\"uri-full\" style=\"display:none;\">{escapedUri}</span>"));
            _ = builder.AppendLine("</td>");
        }
        else
        {
            _ = builder.AppendLine(FormattableString.Invariant($"<td>{escapedUri}</td>"));
        }
        _ = builder.AppendLine(FormattableString.Invariant($"<td class=\"issuer\">{Escape(parsed?.Issuer ?? string.Empty)}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td class=\"{statusClass}\">{Escape(result.Status.ToDisplayString())}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{FormatDate(parsed?.ThisUpdate)}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{FormatDate(parsed?.NextUpdate)}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{result.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{result.DownloadDuration?.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) ?? string.Empty}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{Escape(CsvReportFormatter.NormalizeSignatureStatus(result.SignatureStatus))}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{parsed?.RevokedSerialNumbers?.Count.ToString(CultureInfo.InvariantCulture) ?? string.Empty}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{FormatDate(result.CheckedAtUtc)}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{FormatDate(result.PreviousFetchUtc)}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{Escape(parsed == null ? string.Empty : parsed.IsDelta ? "Delta" : "Full")}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{Escape(result.ErrorInfo ?? string.Empty)}</td>"));
        _ = builder.AppendLine("</tr>");
    }

    private static string FormatDate(DateTime? value)
    {
        var formatted = TimeFormatter.FormatUtc(value);
        if (string.IsNullOrEmpty(formatted))
        {
            return string.Empty;
        }

        // Split date and time onto separate lines (yyyy-MM-dd<br>HH:mm:ssZ)
        var spaceIndex = formatted.IndexOf(' ', StringComparison.Ordinal);
        return spaceIndex > 0
            ? formatted[0..spaceIndex] + "<br>" + formatted[(spaceIndex + 1)..]
            : formatted;
    }

    private static Summary BuildSummary(IReadOnlyList<CrlCheckResult> results)
    {
        return new Summary(
            results.Count,
            results.Count(r => r.Status == CrlStatus.Ok),
            results.Count(r => r.Status == CrlStatus.Warning),
            results.Count(r => r.Status == CrlStatus.Expiring),
            results.Count(r => r.Status == CrlStatus.Expired),
            results.Count(r => r.Status == CrlStatus.Error));
    }

    private static string Escape(string value)
    {
        return System.Net.WebUtility.HtmlEncode(value);
    }

    private static string GetVersion()
    {
        var version = typeof(HtmlReportWriter).Assembly.GetName().Version;
        if (version == null)
        {
            return "v1.0.0";
        }

        var build = version.Build >= 0 ? version.Build : 0;
        return FormattableString.Invariant($"v{version.Major}.{version.Minor}.{build}");
    }

    private readonly record struct Summary(int Total, int Ok, int Warning, int Expiring, int Expired, int Errors);
}
