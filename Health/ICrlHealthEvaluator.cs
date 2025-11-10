using CrlMonitor.Crl;

namespace CrlMonitor.Health;

internal interface ICrlHealthEvaluator
{
    HealthEvaluationResult Evaluate(ParsedCrl parsedCrl, CrlConfigEntry entry, DateTime utcNow);
}
