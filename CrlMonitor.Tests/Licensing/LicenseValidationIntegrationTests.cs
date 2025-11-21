using CrlMonitor.Licensing;
using RedKestrel.Licensing;
using Standard.Licensing;

namespace CrlMonitor.Tests.Licensing;

/// <summary>
/// Integration tests for end-to-end license generation and validation.
/// These would have caught the request code binding mismatch.
/// </summary>
public static class LicenseValidationIntegrationTests
{
    /// <summary>
    /// Integration test: license created for current machine should validate successfully.
    /// This is the key test that would have caught the H- vs S- prefix bug.
    /// </summary>
    [Fact]
    public static async Task LicenseCreatedForCurrentMachineValidatesSuccessfully()
    {
        // Get the request code for this machine (as would be used in license generation)
        var requestCode = LicenseBootstrapper.CreateRequestCode();

        // Create a test license for this machine
        var license = CreateTestLicenseForRequestCode(requestCode);
        var licenseXml = license.ToString();

        // Write license to temp file
        using var temp = new TempFolder();
        var licensePath = Path.Combine(temp.Path, "test.lic");
        await File.WriteAllTextAsync(licensePath, licenseXml);

        // Now validate using the same validator logic as production
        var fileAccessor = new LicenseFileAccessor(
            new LicenseFileOptions { LicenseFilePath = licensePath });
        var validator = new LicenseValidator(
            fileAccessor,
            new LicenseValidationOptions { PublicKey = GetTestPublicKey() });

        var result = await validator.ValidateAsync(CancellationToken.None);

        // Should succeed because we're on the same machine
        Assert.True(result.Success, $"Validation failed: {result.ErrorMessage}");
        Assert.Equal(LicenseValidationError.None, result.Error);
    }

    /// <summary>
    /// Integration test: license created for different machine should fail validation.
    /// </summary>
    [Fact]
    public static async Task LicenseCreatedForDifferentMachineFailsValidation()
    {
        // Create license with a fake request code (different machine)
        var fakeRequestCode = "H-FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF";
        var license = CreateTestLicenseForRequestCode(fakeRequestCode);
        var licenseXml = license.ToString();

        using var temp = new TempFolder();
        var licensePath = Path.Combine(temp.Path, "test.lic");
        await File.WriteAllTextAsync(licensePath, licenseXml);

        var fileAccessor = new LicenseFileAccessor(
            new LicenseFileOptions { LicenseFilePath = licensePath });
        var validator = new LicenseValidator(
            fileAccessor,
            new LicenseValidationOptions { PublicKey = GetTestPublicKey() });

        var result = await validator.ValidateAsync(CancellationToken.None);

        // Should fail with RequestCodeMismatch
        Assert.False(result.Success);
        Assert.Equal(LicenseValidationError.RequestCodeMismatch, result.Error);
        Assert.Contains("not valid for this user or machine", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    private static License CreateTestLicenseForRequestCode(string requestCode)
    {
        var attributes = new Dictionary<string, string> {
            ["RequestCode"] = requestCode,
            ["Product"] = "CrlMonitor"
        };

        var licenseXml = TestLicenseFactory.CreateLicense(
            LicenseType.Standard,
            DateTime.UtcNow.AddYears(1),
            attributes);

        return License.Load(licenseXml);
    }

    private static string GetTestPublicKey()
    {
        return TestLicenseFactory.PublicKey;
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString())).FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(this.Path, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
