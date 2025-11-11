using CrlMonitor.Licensing;

namespace CrlMonitor.Tests.Licensing;

/// <summary>
/// Tests the behaviour of <see cref="LicenseBootstrapper" />.
/// </summary>
public static class LicenseBootstrapperTests
{
    /// <summary>
    /// Ensures that missing licence files prevent execution and direct users to support.
    /// </summary>
    [Fact]
    public static async Task EnsureLicensedAsyncThrowsWhenLicenseMissing()
    {
        using var context = new LicenseTestContext();
        context.EnsureLicenseMissing();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => LicenseBootstrapper.EnsureLicensedAsync(CancellationToken.None));

        Assert.Contains("support@redkestrel.co.uk", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request code", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures that trial licences report the remaining days and allow execution while valid.
    /// </summary>
    [Fact]
    public static async Task EnsureLicensedAsyncReportsTrialDaysWhenActive()
    {
        using var context = new LicenseTestContext();
        context.WriteTrialLicense(DateTime.UtcNow.AddDays(10));

        using var capture = ConsoleCapture.Start();
        await LicenseBootstrapper.EnsureLicensedAsync(CancellationToken.None);

        var output = capture.GetOutput();
        Assert.Contains("Trial mode", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("30", output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures that expired trials stop execution with sales contact instructions.
    /// </summary>
    [Fact]
    public static async Task EnsureLicensedAsyncThrowsWhenTrialExpired()
    {
        using var context = new LicenseTestContext();
        context.WriteTrialLicense(DateTime.UtcNow.AddDays(10));
        context.SeedTrialTimestamp(DateTime.UtcNow.AddDays(-40));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => LicenseBootstrapper.EnsureLicensedAsync(CancellationToken.None));

        Assert.Contains("Trial period expired", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sales@redkestrel.co.uk", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
