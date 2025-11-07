using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrlMonitor.Fetching;

internal interface ICrlFetcher
{
    Task<FetchedCrl> FetchAsync(CrlConfigEntry entry, CancellationToken cancellationToken);
}
