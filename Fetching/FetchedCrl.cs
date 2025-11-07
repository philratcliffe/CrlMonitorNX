using System;

namespace CrlMonitor.Fetching;

internal sealed record FetchedCrl(
    byte[] Content,
    TimeSpan Duration,
    long ContentLength);
