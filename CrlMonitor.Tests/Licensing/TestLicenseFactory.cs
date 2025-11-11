using System.Security.Cryptography;
using System.Text;
using Standard.Licensing;
using Standard.Licensing.Security.Cryptography;

namespace CrlMonitor.Tests.Licensing;

internal static class TestLicenseFactory
{
    private const string Passphrase = "CrlMonitor-Test-Passphrase";
    private static readonly Lazy<(string PrivateKey, string PublicKey)> KeyPair = new(CreateKeyPair);

    internal static string PublicKey => KeyPair.Value.PublicKey;

    internal static string CreateTrialLicense(DateTime expiresUtc)
    {
        return CreateLicense(LicenseType.Trial, expiresUtc, null);
    }

    internal static string CreateLicense(
        LicenseType type,
        DateTime expiresUtc,
        IDictionary<string, string>? attributes)
    {
        var builder = License.New()
            .WithUniqueIdentifier(Guid.NewGuid())
            .As(type)
            .ExpiresAt(expiresUtc)
            .LicensedTo("Test User", "test@example.com");

        if (attributes is not null)
        {
            builder = builder.WithAdditionalAttributes(attributes);
        }

        var license = builder.CreateAndSignWithPrivateKey(KeyPair.Value.PrivateKey, Passphrase);
        return license.ToString();
    }

    internal static string ComputeStorageHash(string storageKey)
    {
        var bytes = Encoding.UTF8.GetBytes(storageKey);
        var hash = SHA256.HashData(bytes);
        return BitConverter.ToString(hash, 0, 4).Replace("-", string.Empty, StringComparison.Ordinal);
    }

    private static (string PrivateKey, string PublicKey) CreateKeyPair()
    {
        var generator = KeyGenerator.Create();
        var pair = generator.GenerateKeyPair();
        return (pair.ToEncryptedPrivateKeyString(Passphrase), pair.ToPublicKeyString());
    }
}
