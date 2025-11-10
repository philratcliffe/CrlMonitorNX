namespace CrlMonitor.Health;

internal sealed record HealthEvaluationResult(string Status, string? Message)
{
    public static HealthEvaluationResult Healthy()
    {
        return new HealthEvaluationResult("Healthy", null);
    }

    public static HealthEvaluationResult Expiring(string message)
    {
        return new HealthEvaluationResult("Expiring", message);
    }

    public static HealthEvaluationResult Expired(string message)
    {
        return new HealthEvaluationResult("Expired", message);
    }

    public static HealthEvaluationResult Unknown(string message)
    {
        return new HealthEvaluationResult("Unknown", message);
    }
}
