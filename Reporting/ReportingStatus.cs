namespace CrlMonitor.Reporting;

internal sealed class ReportingStatus
{
    private readonly object _gate = new();

    public string? CsvPath { get; private set; }
    public bool CsvWritten { get; private set; }
    public bool EmailReportSent { get; private set; }

    public void RecordCsv(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            CsvPath = path;
            CsvWritten = true;
        }
    }

    public void RecordEmailSent()
    {
        lock (_gate)
        {
            EmailReportSent = true;
        }
    }
}
