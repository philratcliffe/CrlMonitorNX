namespace CrlMonitor.Fetching;

internal interface ICrlFetcher
{
    Task<FetchedCrl> FetchAsync(CrlConfigEntry entry, CancellationToken cancellationToken);
}
