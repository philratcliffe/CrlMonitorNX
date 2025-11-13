using RedKestrel.Licensing;
using RedKestrel.Licensing.Trial;
using Standard.Licensing;
using CrlMonitor.Logging;
using Serilog;

namespace CrlMonitor.Licensing;

internal static class LicenseBootstrapper
{
    private const string CompanyName = "RedKestrel";
    private const string ProductName = "CrlMonitor";
    internal const string TrialStorageKey = "CrlMonitor_Trial_2025-11-09";
    private const int TrialDays = 30;

    // TODO: replace with the production RSA public key used to issue licenses for CrlMonitor.
    private const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE7FY+RdhgwaYodbZQfnJkBtUWN/3K9bWDjfOVfz4pByv5myFYj6XjJf7nwmPvACIXh5R8Dlx5SYpesuUQdAshJg==";

    public static async Task EnsureLicensedAsync(CancellationToken cancellationToken)
    {
        var fileAccessor = new LicenseFileAccessor();
        var validator = new LicenseValidator(fileAccessor, new LicenseValidationOptions {
            PublicKey = ResolvePublicKey()
        });

        var validation = await validator.ValidateAsync(cancellationToken).ConfigureAwait(false);

        LogLicenseValidation(validation);

        if (!validation.Success)
        {
            ThrowLicenceException(validation);
        }

        if (validation.License?.Type == LicenseType.Trial)
        {
            await EnforceTrialAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ResolvePublicKey()
    {
#if DEBUG
        return LicenseBootstrapperTestAccess.PublicKeyOverride ?? PublicKey;
#else
        return PublicKey;
#endif
    }

    private static string CreateRequestCode()
    {
        var generator = new RequestCodeGenerator();
        var options = new RequestCodeGeneratorOptions {
            BindingOrder = new[]
            {
                RequestCodeBinding.MachineName,
                RequestCodeBinding.MacAddress
            }
        };
        return generator.Generate(options);
    }

    private static async Task EnforceTrialAsync(CancellationToken cancellationToken)
    {
        var trialOptions = new TrialOptions {
            CompanyName = CompanyName,
            ProductName = ProductName,
            StorageKey = TrialStorageKey,
            TrialDays = TrialDays,
#if DEBUG
            ProgramDataOverridePath = LicenseBootstrapperTestAccess.TrialDataDirectoryOverride
#endif
        };

        var storage = TrialStorageFactory.CreateDefault(trialOptions);
        var manager = new TrialManager(trialOptions, storage);

        // TODO: Add TS status codes once RedKestrel.Licensing library supports them
        // For now, log basic trial status without diagnostic codes
        var status = await manager.EvaluateAsync(cancellationToken).ConfigureAwait(false);

        if (!status.IsValid)
        {
            ThrowTrialExpiryException();
        }

        LoggingSetup.LogTrialStatus(status.DaysRemaining);
        Console.WriteLine("Trial mode: {0} day(s) remaining", status.DaysRemaining);
    }

    private static void ThrowTrialExpiryException()
    {
        var requestCode = CreateRequestCode();
        var message = FormattableString.Invariant(
            $"Trial period expired. Please contact sales@redkestrel.co.uk with request code: {requestCode}");
        throw new InvalidOperationException(message);
    }

    private static void ThrowLicenceException(LicenseValidationResult validation)
    {
        var reason = string.IsNullOrWhiteSpace(validation.ErrorMessage)
            ? validation.Error.ToString()
            : validation.ErrorMessage;

        var requestCode = CreateRequestCode();

        // Check if this is an expired trial license (unexpected state)
        if (validation.Error == LicenseValidationError.Expired && IsTrialLicense(validation))
        {
            var message = FormattableString.Invariant(
                $"Unexpected licence state — trial licence has expired.\nPlease contact support@redkestrel.co.uk with request code:\n{requestCode}");
            throw new InvalidOperationException(message);
        }

        // Soften wording for common error messages
        if (reason.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            reason = "this product's licence has expired";
        }

        var standardMessage = FormattableString.Invariant(
            $"Licence validation failed — {reason}.\nPlease contact support@redkestrel.co.uk with request code:\n{requestCode}");
        throw new InvalidOperationException(standardMessage);
    }

    private static bool IsTrialLicense(LicenseValidationResult validation)
    {
        if (string.IsNullOrWhiteSpace(validation.LicensePath) || !File.Exists(validation.LicensePath))
        {
            return false;
        }

#pragma warning disable CA1031 // Defensive: any exception means we can't determine type, treat as non-trial
        try
        {
            var content = File.ReadAllText(validation.LicensePath);
            var license = License.Load(content);
            return license.Type == LicenseType.Trial;
        }
        catch
        {
            return false;
        }
#pragma warning restore CA1031
    }

    private static void LogLicenseValidation(LicenseValidationResult validation)
    {
        if (!string.IsNullOrWhiteSpace(validation.LicensePath) && File.Exists(validation.LicensePath))
        {
            var fileInfo = new FileInfo(validation.LicensePath);
            Log.Information("License file found at {LicensePath} ({FileSize} bytes)", validation.LicensePath, fileInfo.Length);
        }

        if (validation.Success && validation.License != null)
        {
            Log.Information("License validation: VALID");
            Log.Information("License type: {LicenseType}", validation.License.Type.ToString());
            Log.Information("License expires: {ExpirationDate}", validation.License.Expiration);

            var daysUntilExpiry = (validation.License.Expiration - DateTime.Now).Days;
            Log.Information("Days until expiration: {Days}", daysUntilExpiry);
        }
        else
        {
            Log.Warning("License validation: INVALID - {Error}", validation.ErrorMessage ?? validation.Error.ToString());
        }
    }

#if DEBUG
    internal static void SetTestPublicKey(string publicKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(publicKey);
        LicenseBootstrapperTestAccess.PublicKeyOverride = publicKey;
    }

    internal static void SetTestTrialDataDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        LicenseBootstrapperTestAccess.TrialDataDirectoryOverride = directory;
    }

    internal static void ResetTestOverrides()
    {
        LicenseBootstrapperTestAccess.Reset();
    }

    private static class LicenseBootstrapperTestAccess
    {
        internal static string? PublicKeyOverride { get; set; }
        internal static string? TrialDataDirectoryOverride { get; set; }

        internal static void Reset()
        {
            PublicKeyOverride = null;
            TrialDataDirectoryOverride = null;
        }
    }
#endif
}
