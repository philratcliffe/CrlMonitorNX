namespace CrlMonitor.Reporting;

/// <summary>
/// Formats CRL expiry time as human-readable string.
/// </summary>
internal static class ExpiresInFormatter
{
    /// <summary>
    /// Formats time until CRL expires showing days, hours, or Expired.
    /// </summary>
    /// <param name="nextUpdate">Next update timestamp.</param>
    /// <returns>Formatted string: X days, X hours, Expired, or empty.</returns>
    public static string Format(DateTime? nextUpdate)
    {
        if (!nextUpdate.HasValue)
        {
            return string.Empty;
        }

        var remaining = nextUpdate.Value - DateTime.UtcNow;

        if (remaining.TotalHours < 0)
        {
            return "Expired";
        }

        if (remaining.TotalHours <= 48)
        {
            var hours = (int)Math.Round(remaining.TotalHours, MidpointRounding.AwayFromZero);
            return FormattableString.Invariant($"{hours} hours");
        }

        var days = (int)Math.Round(remaining.TotalDays, MidpointRounding.AwayFromZero);
        return FormattableString.Invariant($"{days} days");
    }
}
