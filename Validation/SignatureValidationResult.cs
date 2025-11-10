namespace CrlMonitor.Validation;

internal sealed record SignatureValidationResult(string Status, string? ErrorMessage)
{
    public static SignatureValidationResult Valid()
    {
        return new SignatureValidationResult("Valid", null);
    }

    public static SignatureValidationResult Invalid(string message)
    {
        return new SignatureValidationResult("Invalid", message);
    }

    public static SignatureValidationResult Skipped(string message)
    {
        return new SignatureValidationResult("Skipped", message);
    }

    public static SignatureValidationResult Failure(string message)
    {
        return new SignatureValidationResult("Error", message);
    }
}
