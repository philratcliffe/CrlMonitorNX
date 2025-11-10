using CrlMonitor.Crl;

namespace CrlMonitor.Health;

internal sealed class CrlHealthEvaluator : ICrlHealthEvaluator
{
    public HealthEvaluationResult Evaluate(ParsedCrl parsedCrl, CrlConfigEntry entry, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(parsedCrl);
        ArgumentNullException.ThrowIfNull(entry);

        if (parsedCrl.NextUpdate == null)
        {
            return HealthEvaluationResult.Unknown("Next update not provided.");
        }

        var nextUpdate = parsedCrl.NextUpdate.Value;
        if (utcNow >= nextUpdate)
        {
            return HealthEvaluationResult.Expired($"Next update {nextUpdate:u} is in the past.");
        }

        var span = nextUpdate - parsedCrl.ThisUpdate;
        if (span <= TimeSpan.Zero)
        {
            return HealthEvaluationResult.Unknown("CRL validity window invalid.");
        }

        var elapsed = utcNow - parsedCrl.ThisUpdate;
        var ratio = elapsed.TotalSeconds / span.TotalSeconds;
        return ratio >= entry.ExpiryThreshold
            ? HealthEvaluationResult.Expiring($"CRL is {ratio:P0} through validity window.")
            : HealthEvaluationResult.Healthy();
    }
}
