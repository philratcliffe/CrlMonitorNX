using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal interface IReporter
{
    Task ReportAsync(CrlCheckRun run, CancellationToken cancellationToken);
}
