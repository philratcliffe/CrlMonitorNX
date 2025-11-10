namespace CrlMonitor.Fetching;

internal interface IFetcherResolver
{
    ICrlFetcher Resolve(Uri uri);
}
