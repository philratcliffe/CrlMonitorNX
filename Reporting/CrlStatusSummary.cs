using System.Collections.Generic;
using System.Linq;
using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal readonly record struct CrlStatusSummary(int Total, int Ok, int Warning, int Expiring, int Expired, int Errors)
{
    public static CrlStatusSummary FromResults(IEnumerable<CrlCheckResult> results)
    {
        var list = results.ToList();
        return new CrlStatusSummary(
            list.Count,
            list.Count(r => r.Status == CrlStatus.Ok),
            list.Count(r => r.Status == CrlStatus.Warning),
            list.Count(r => r.Status == CrlStatus.Expiring),
            list.Count(r => r.Status == CrlStatus.Expired),
            list.Count(r => r.Status == CrlStatus.Error));
    }
}
