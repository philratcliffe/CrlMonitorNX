namespace CrlMonitor.Fetching;

internal sealed class FetcherMapping(IEnumerable<string> schemes, ICrlFetcher fetcher) : IFetcherMapping
{
    public IEnumerable<string> Schemes { get; } = schemes ?? throw new ArgumentNullException(nameof(schemes));
    public ICrlFetcher Fetcher { get; } = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
}
