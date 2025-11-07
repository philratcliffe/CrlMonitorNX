using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal sealed class CompositeReporter : IReporter
{
    private readonly IReadOnlyList<IReporter> _reporters;

    public CompositeReporter(IReadOnlyList<IReporter> reporters)
    {
        _reporters = reporters ?? new List<IReporter>();
    }

    public async Task ReportAsync(CrlCheckRun run, CancellationToken cancellationToken)
    {
        foreach (var reporter in _reporters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await reporter.ReportAsync(run, cancellationToken).ConfigureAwait(false);
        }
    }
}
