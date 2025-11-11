using RedKestrel.Licensing;
using RedKestrel.Licensing.Trial;
using Standard.Licensing;

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
        var status = await manager.EvaluateAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsValid)
        {
            ThrowTrialExpiryException();
        }

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
        var message = FormattableString.Invariant(
            $"Licence validation failed ({reason}). Please contact support@redkestrel.co.uk with request code: {requestCode}");
        throw new InvalidOperationException(message);
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
