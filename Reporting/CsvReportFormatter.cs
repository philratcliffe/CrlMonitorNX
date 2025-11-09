using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal static class CsvReportFormatter
{
    public static async Task WriteAsync(TextWriter writer, CrlCheckRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(run);

        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            NewLine = Environment.NewLine,
            HasHeaderRecord = true
        };

        var generatedStamp = FormatTimestamp(run.GeneratedAtUtc);
        await writer.WriteLineAsync($"# report_generated_utc,{generatedStamp}").ConfigureAwait(false);
        using var csv = new CsvWriter(writer, csvConfig, leaveOpen: true);
        WriteHeader(csv);
        await csv.NextRecordAsync().ConfigureAwait(false);

        foreach (var result in run.Results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteRow(csv, result);
            await csv.NextRecordAsync().ConfigureAwait(false);
        }
    }

    public static string NormalizeSignatureStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return string.Empty;
        }

        if (status.Equals("Valid", StringComparison.OrdinalIgnoreCase))
        {
            return "VALID";
        }

        if (status.Equals("Invalid", StringComparison.OrdinalIgnoreCase))
        {
            return "INVALID";
        }

        if (status.Equals("Skipped", StringComparison.OrdinalIgnoreCase))
        {
            return "DISABLED";
        }

        return status;
    }

    private static void WriteHeader(CsvWriter csv)
    {
        csv.WriteField("URI");
        csv.WriteField("Issuer_Name");
        csv.WriteField("Status");
        csv.WriteField("This_Update_UTC");
        csv.WriteField("Next_Update_UTC");
        csv.WriteField("CRL_Size_bytes");
        csv.WriteField("Download_Duration_ms");
        csv.WriteField("Signature_Valid");
        csv.WriteField("Revoked_Count");
        csv.WriteField("Checked_Time_UTC");
        csv.WriteField("Previous_Checked_Time_UTC");
        csv.WriteField("CRL_Type");
        csv.WriteField("Status_Details");
    }

    private static void WriteRow(CsvWriter csv, CrlCheckResult result)
    {
        var parsed = result.ParsedCrl;
        var issuer = parsed?.Issuer ?? string.Empty;
        var thisUpdate = FormatNullableTimestamp(parsed?.ThisUpdate);
        var nextUpdate = FormatNullableTimestamp(parsed?.NextUpdate);
        var size = result.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var downloadMs = result.DownloadDuration?.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) ?? string.Empty;
        var signature = NormalizeSignatureStatus(result.SignatureStatus);
        var revokedCount = parsed?.RevokedSerialNumbers?.Count;
        var checkedTime = FormatTimestamp(result.CheckedAtUtc);
        var previousChecked = result.PreviousFetchUtc.HasValue ? FormatTimestamp(result.PreviousFetchUtc.Value) : string.Empty;
        var crlType = parsed == null ? string.Empty : (parsed.IsDelta ? "Delta" : "Full");
        var statusDetails = result.ErrorInfo ?? string.Empty;

        csv.WriteField(result.Uri.ToString());
        csv.WriteField(issuer);
        csv.WriteField(result.Status);
        csv.WriteField(thisUpdate);
        csv.WriteField(nextUpdate);
        csv.WriteField(size);
        csv.WriteField(downloadMs);
        csv.WriteField(signature);
        csv.WriteField(revokedCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        csv.WriteField(checkedTime);
        csv.WriteField(previousChecked);
        csv.WriteField(crlType);
        csv.WriteField(statusDetails);
    }

    private static string FormatTimestamp(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static string FormatNullableTimestamp(DateTime? value)
    {
        return value.HasValue ? FormatTimestamp(value.Value) : string.Empty;
    }
}
