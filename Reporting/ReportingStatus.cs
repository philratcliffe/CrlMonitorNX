namespace CrlMonitor.Reporting;

internal sealed class ReportingStatus
{
    private readonly object _gate = new();

    public string? CsvPath { get; private set; }
    public bool CsvWritten { get; private set; }
    public string? HtmlReportPath { get; private set; }
    public bool HtmlWritten { get; private set; }
    public bool EmailReportSent { get; private set; }

    public void RecordCsv(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (this._gate)
        {
            this.CsvPath = path;
            this.CsvWritten = true;
        }
    }

    public void RecordHtml(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (this._gate)
        {
            this.HtmlReportPath = path;
            this.HtmlWritten = true;
        }
    }

    public void RecordEmailSent()
    {
        lock (this._gate)
        {
            this.EmailReportSent = true;
        }
    }
}
