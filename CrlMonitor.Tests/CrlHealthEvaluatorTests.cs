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
        var now = DateTime.UtcNow;
        var (parsed, _, _, _) = CrlTestBuilder.BuildParsedCrl(false, now, now.AddDays(10));
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
        var now = DateTime.UtcNow;
        var (parsed, _, _, _) = CrlTestBuilder.BuildParsedCrl(false, now, now.AddHours(4));
        var entry = new CrlConfigEntry(new Uri("http://example.com"), SignatureValidationMode.None, null, 0.5, null);

        var result = evaluator.Evaluate(parsed, entry, parsed.ThisUpdate.AddHours(3));

        Assert.Equal("Expiring", result.Status);
    }

    /// <summary>
    /// Expired when current time surpasses next update.
    /// </summary>
    [Fact]
    public static void ExpiredWhenPastNextUpdate()
    {
        var evaluator = new CrlHealthEvaluator();
        var now = DateTime.UtcNow;
        var (parsed, _, _, _) = CrlTestBuilder.BuildParsedCrl(false, now.AddDays(-2), now.AddDays(-1));
        var entry = new CrlConfigEntry(new Uri("http://example.com"), SignatureValidationMode.None, null, 0.5, null);

        var result = evaluator.Evaluate(parsed, entry, parsed.NextUpdate!.Value.AddMinutes(1));

        Assert.Equal("Expired", result.Status);
    }
}
