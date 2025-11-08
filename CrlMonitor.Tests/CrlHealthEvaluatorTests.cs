using System;
using CrlMonitor.Crl;
using CrlMonitor.Health;
using CrlMonitor.Models;
using CrlMonitor.Tests.TestUtilities;
using Xunit;

namespace CrlMonitor.Tests;

/// <summary>
/// Tests the CRL health evaluator.
/// </summary>
public static class CrlHealthEvaluatorTests
{
    /// <summary>
    /// Healthy when below the configured expiry threshold.
    /// </summary>
    [Fact]
    public static void HealthyWhenBelowThreshold()
    {
        var evaluator = new CrlHealthEvaluator();
        var (parsed, _, _, _) = CrlTestBuilder.BuildParsedCrl(signWithDifferentKey: false);
        var entry = new CrlConfigEntry(new Uri("http://example.com"), SignatureValidationMode.None, null, 0.8, null);

        var result = evaluator.Evaluate(parsed, entry, parsed.ThisUpdate.AddHours(1));

        Assert.Equal("Healthy", result.Status);
    }

    /// <summary>
    /// Expiring when beyond the configured ratio.
    /// </summary>
    [Fact]
    public static void ExpiringWhenPastThreshold()
    {
        var evaluator = new CrlHealthEvaluator();
        var (parsed, _, _, _) = CrlTestBuilder.BuildParsedCrl(false);
        var entry = new CrlConfigEntry(new Uri("http://example.com"), SignatureValidationMode.None, null, 0.5, null);

        var result = evaluator.Evaluate(parsed, entry, parsed.ThisUpdate.AddDays(4));

        Assert.Equal("Expiring", result.Status);
    }

    /// <summary>
    /// Expired when current time surpasses next update.
    /// </summary>
    [Fact]
    public static void ExpiredWhenPastNextUpdate()
    {
        var evaluator = new CrlHealthEvaluator();
        var (parsed, _, _, _) = CrlTestBuilder.BuildParsedCrl(false);
        var entry = new CrlConfigEntry(new Uri("http://example.com"), SignatureValidationMode.None, null, 0.5, null);

        var result = evaluator.Evaluate(parsed, entry, parsed.NextUpdate!.Value.AddMinutes(1));

        Assert.Equal("Expired", result.Status);
    }
}
