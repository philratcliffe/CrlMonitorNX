using System;
using System.Globalization;

namespace CrlMonitor.Reporting;

internal static class TimeFormatter
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss'Z'";

    public static string FormatUtc(DateTime value)
    {
        return value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
    }

    public static string FormatUtc(DateTime? value)
    {
        return value.HasValue ? FormatUtc(value.Value) : string.Empty;
    }
}
