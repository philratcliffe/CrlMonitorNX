using CrlMonitor.Licensing;
using Serilog;

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
        if (!OperatingSystem.IsMacOS())
        {
            // Test only runs on macOS where trial storage locations are predictable
            return;
        }

        // Clear any persisted trial timestamp from previous app runs
        LicenseTestContext.ClearIsolatedStorage();

        using var context = new LicenseTestContext();
        var now = DateTime.UtcNow;

        context.WriteTrialLicense(now.AddDays(10));
        context.SeedTrialTimestamp(now);

        var logFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.log");

        try
        {
            // Configure Serilog to write to temp log file
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(logFile, formatProvider: System.Globalization.CultureInfo.InvariantCulture)
                .CreateLogger();

            await LicenseBootstrapper.EnsureLicensedAsync(CancellationToken.None);
            await Log.CloseAndFlushAsync();

            var logContents = await File.ReadAllTextAsync(logFile);
            Assert.Contains("Trial period: VALID", logContents, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("30 days remaining", logContents, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }
        }
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
