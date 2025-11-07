namespace CrlMonitor.Validation;

internal sealed record SignatureValidationResult(string Status, string? ErrorMessage)
{
    public static SignatureValidationResult Valid() => new("Valid", null);
    public static SignatureValidationResult Invalid(string message) => new("Invalid", message);
    public static SignatureValidationResult Skipped(string message) => new("Skipped", message);
    public static SignatureValidationResult Failure(string message) => new("Error", message);
}
