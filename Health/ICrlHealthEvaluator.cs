using System;
using CrlMonitor.Crl;
using CrlMonitor.Models;

namespace CrlMonitor.Health;

internal interface ICrlHealthEvaluator
{
    HealthEvaluationResult Evaluate(ParsedCrl parsedCrl, CrlConfigEntry entry, DateTime utcNow);
}
