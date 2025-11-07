namespace CrlMonitor.Health;

internal sealed record HealthEvaluationResult(string Status, string? Message)
{
    public static HealthEvaluationResult Healthy() => new("Healthy", null);
    public static HealthEvaluationResult Expiring(string message) => new("Expiring", message);
    public static HealthEvaluationResult Expired(string message) => new("Expired", message);
    public static HealthEvaluationResult Unknown(string message) => new("Unknown", message);
}
