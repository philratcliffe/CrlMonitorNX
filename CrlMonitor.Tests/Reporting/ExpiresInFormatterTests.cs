using CrlMonitor.Reporting;

namespace CrlMonitor.Tests.Reporting;

/// <summary>
/// Validates expires-in time formatting.
/// </summary>
public static class ExpiresInFormatterTests
{
    /// <summary>
    /// Ensures days display when > 48 hours.
    /// </summary>
    [Fact]
    public static void FormatDisplaysDaysWhenMoreThan48Hours()
    {
        var nextUpdate = DateTime.UtcNow.AddDays(12);
        var result = ExpiresInFormatter.Format(nextUpdate);
        Assert.Equal("12 days", result);
    }

    /// <summary>
    /// Ensures hours display when less than 48 hours.
    /// </summary>
    [Fact]
    public static void FormatDisplaysHoursWhenLessThan48Hours()
    {
        var nextUpdate = DateTime.UtcNow.AddHours(36);
        var result = ExpiresInFormatter.Format(nextUpdate);
        Assert.Equal("36 hours", result);
    }

    /// <summary>
    /// Ensures "Expired" displays when past Next Update.
    /// </summary>
    [Fact]
    public static void FormatDisplaysExpiredWhenPast()
    {
        var nextUpdate = DateTime.UtcNow.AddHours(-5);
        var result = ExpiresInFormatter.Format(nextUpdate);
        Assert.Equal("Expired", result);
    }

    /// <summary>
    /// Ensures empty string when Next Update is null.
    /// </summary>
    [Fact]
    public static void FormatReturnsEmptyWhenNull()
    {
        var result = ExpiresInFormatter.Format(null);
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Ensures boundary at exactly 48 hours shows hours.
    /// </summary>
    [Fact]
    public static void FormatShowsHoursAtExactly48Hours()
    {
        var nextUpdate = DateTime.UtcNow.AddHours(48);
        var result = ExpiresInFormatter.Format(nextUpdate);
        Assert.Equal("48 hours", result);
    }

    /// <summary>
    /// Ensures 49 hours shows days.
    /// </summary>
    [Fact]
    public static void FormatShowsDaysAt49Hours()
    {
        var nextUpdate = DateTime.UtcNow.AddHours(49);
        var result = ExpiresInFormatter.Format(nextUpdate);
        Assert.Equal("2 days", result);
    }
}
