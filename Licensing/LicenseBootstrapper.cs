using System;
using System.Threading;
using System.Threading.Tasks;
using RedKestrel.Licensing;
using RedKestrel.Licensing.Trial;

namespace CrlMonitor.Licensing;

internal static class LicenseBootstrapper
{
    private const string CompanyName = "RedKestrel";
    private const string ProductName = "CrlMonitor";
    private const string TrialStorageKey = "CrlMonitor_Trial_2025-11-09";

    // TODO: replace with the production RSA public key used to issue licenses for CrlMonitor.
    private const string PublicKey =
        "<RSAKeyValue><Modulus>tQ0J9ugVf+1Sc9qZSuFTYnJv96skcYvN5pz50jdK5PajcNC8KQ4Op7/aNm5FkCbgwYJtKBXPSqV655zE+vDlDsZ3KEFqgmrEtNd3np+hdV9w7qMID+Yojf+rkR1ZXolGCseWQN0WrJQvjJt+QeL4MieCUxiiyj0qiRqKoJTGy5k=</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

    public static async Task EnsureLicensedAsync(CancellationToken cancellationToken)
    {
        var fileAccessor = new LicenseFileAccessor();
        var validator = new LicenseValidator(fileAccessor, new LicenseValidationOptions
        {
            PublicKey = PublicKey
        });

        var validation = await validator.ValidateAsync(cancellationToken).ConfigureAwait(false);
        if (validation.Success)
        {
            return;
        }

        if (validation.Error != LicenseValidationError.FileNotFound)
        {
            Console.WriteLine("License validation failed: {0}", validation.ErrorMessage ?? validation.Error.ToString());
        }

        var trialOptions = new TrialOptions
        {
            CompanyName = CompanyName,
            ProductName = ProductName,
            StorageKey = TrialStorageKey,
            TrialDays = 30
        };
        var storage = TrialStorageFactory.CreateDefault(trialOptions);
        var trialManager = new TrialManager(trialOptions, storage);
        var trialStatus = await trialManager.EvaluateAsync(cancellationToken).ConfigureAwait(false);
        if (!trialStatus.IsValid)
        {
            var requestCode = CreateRequestCode();
            throw new InvalidOperationException($"Trial period expired. Please contact support with request code: {requestCode}");
        }

        Console.WriteLine("Trial mode: {0} day(s) remaining", trialStatus.DaysRemaining);
        if (validation.Error == LicenseValidationError.FileNotFound)
        {
            var requestCode = CreateRequestCode();
            Console.WriteLine("No license installed. Request code: {0}", requestCode);
        }
    }

    private static string CreateRequestCode()
    {
        var generator = new RequestCodeGenerator();
        var options = new RequestCodeGeneratorOptions
        {
            BindingOrder = new[]
            {
                RequestCodeBinding.MachineName,
                RequestCodeBinding.MacAddress
            }
        };
        return generator.Generate(options);
    }
}
