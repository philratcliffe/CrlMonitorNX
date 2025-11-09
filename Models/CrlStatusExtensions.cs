namespace CrlMonitor.Models;

internal static class CrlStatusExtensions
{
    public static string ToDisplayString(this CrlStatus status)
    {
        return status switch
        {
            CrlStatus.Ok => "OK",
            CrlStatus.Warning => "WARNING",
            CrlStatus.Expiring => "EXPIRING",
            CrlStatus.Expired => "EXPIRED",
            CrlStatus.Error => "ERROR",
            _ => status.ToString().ToUpperInvariant()
        };
    }
}
