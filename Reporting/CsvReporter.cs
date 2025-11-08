using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal sealed class CsvReporter : IReporter
{
    private readonly string _outputPath;
    private readonly bool _appendTimestamp;

    public CsvReporter(string outputPath, bool appendTimestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        _outputPath = outputPath;
        _appendTimestamp = appendTimestamp;
    }

    public async Task ReportAsync(CrlCheckRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();

        var path = _appendTimestamp ? AppendTimestamp(_outputPath) : _outputPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            NewLine = Environment.NewLine,
            HasHeaderRecord = true
        };

        var generatedStamp = FormatTimestamp(run.GeneratedAtUtc);
        await writer.WriteLineAsync($"# report_generated_utc,{generatedStamp}").ConfigureAwait(false);
        using var csv = new CsvWriter(writer, csvConfig);
        WriteHeader(csv);
        await csv.NextRecordAsync().ConfigureAwait(false);

        foreach (var result in run.Results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteRow(csv, result);
            await csv.NextRecordAsync().ConfigureAwait(false);
        }
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
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
        var signature = FormatSignatureStatus(result.SignatureStatus);
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
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static string FormatNullableTimestamp(DateTime? value)
    {
        return value.HasValue ? FormatTimestamp(value.Value) : string.Empty;
    }

    private static string FormatSignatureStatus(string? status)
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

    private static string AppendTimestamp(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(directory, $"{baseName}_{timestamp}{extension}");
    }
}
