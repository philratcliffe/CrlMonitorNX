using CrlMonitor.Licensing;
using RedKestrel.Licensing;

namespace CrlMonitor.Tests.Licensing;

/// <summary>
/// Ensures request code generation is consistent between license creation and validation.
/// </summary>
public static class RequestCodeConsistencyTests
{
    /// <summary>
    /// Unit test: request code generated for license creation must match validation.
    /// This would have caught the binding mismatch bug.
    /// </summary>
    [Fact]
    public static void CreateRequestCodeMatchesValidatorRequestCode()
    {
        // Generate request code as used when creating license
        var creationRequestCode = LicenseBootstrapper.CreateRequestCode();

        // Generate request code as used by validator (simulating what validator does)
        var generator = new RequestCodeGenerator();
        var validationRequestCode = generator.Generate();

        // These MUST match - same machine should generate same request code
        Assert.Equal(creationRequestCode, validationRequestCode);
    }

    /// <summary>
    /// Unit test: request codes should be stable across multiple calls on same machine.
    /// </summary>
    [Fact]
    public static void CreateRequestCodeIsStable()
    {
        var code1 = LicenseBootstrapper.CreateRequestCode();
        var code2 = LicenseBootstrapper.CreateRequestCode();

        Assert.Equal(code1, code2);
    }

    /// <summary>
    /// Unit test: request codes should have expected prefix based on binding type.
    /// Machine-bound should start with H- or M-.
    /// </summary>
    [Fact]
    public static void CreateRequestCodeUsesMachineBinding()
    {
        var requestCode = LicenseBootstrapper.CreateRequestCode();

        // Machine-bound request codes start with H- (Hardware) or M- (Machine)
        // NOT S- (Software/User) or U- (User)
        Assert.True(
            requestCode.StartsWith("H-", StringComparison.Ordinal) ||
            requestCode.StartsWith("M-", StringComparison.Ordinal),
            $"Expected machine-bound request code (H- or M-), got: {requestCode}");
    }
}
