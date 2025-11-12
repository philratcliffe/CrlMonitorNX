using System.Globalization;
using System.IO.IsolatedStorage;
using System.Text;
using CrlMonitor.Licensing;

namespace CrlMonitor.Tests.Licensing;

internal sealed class LicenseTestContext : IDisposable
{
    private readonly string _licensePath;
    private readonly string? _backupPath;
    private readonly string _trialDataDirectory;

    public LicenseTestContext()
    {
        this._licensePath = Path.Combine(AppContext.BaseDirectory, "license.lic");
        this._backupPath = BackupExistingFile(this._licensePath);

        this._trialDataDirectory = Path.Combine(Path.GetTempPath(), $"crlmonitor-trial-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(this._trialDataDirectory);

        LicenseBootstrapper.SetTestTrialDataDirectory(this._trialDataDirectory);
    }

    public void EnsureLicenseMissing()
    {
        if (File.Exists(this._licensePath))
        {
            File.Delete(this._licensePath);
        }
    }

    public void WriteTrialLicense(DateTime expiresUtc)
    {
        var content = TestLicenseFactory.CreateTrialLicense(expiresUtc);
        File.WriteAllText(this._licensePath, content, Encoding.UTF8);
        LicenseBootstrapper.SetTestPublicKey(TestLicenseFactory.PublicKey);
    }

    public void SeedTrialTimestamp(DateTime timestampUtc)
    {
        var hash = TestLicenseFactory.ComputeStorageHash(LicenseBootstrapper.TrialStorageKey);
        var file = Path.Combine(this._trialDataDirectory, $".data_{hash}");
        var content = timestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        File.WriteAllText(file, content, Encoding.UTF8);
    }

    public static void ClearIsolatedStorage()
    {
        var hash = TestLicenseFactory.ComputeStorageHash(LicenseBootstrapper.TrialStorageKey);
        var fileName = $".data_{hash}";
        try
        {
            using var store = IsolatedStorageFile.GetUserStoreForAssembly();
            if (store.FileExists(fileName))
            {
                store.DeleteFile(fileName);
            }
        }
        catch (IsolatedStorageException)
        {
            // Ignore cleanup failures
        }
    }

    public void Dispose()
    {
        LicenseBootstrapper.ResetTestOverrides();

        if (File.Exists(this._licensePath))
        {
            File.Delete(this._licensePath);
        }

        if (this._backupPath != null && File.Exists(this._backupPath))
        {
            File.Move(this._backupPath, this._licensePath);
        }

        try
        {
            Directory.Delete(this._trialDataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // ignore cleanup failures in tests
        }
        catch (UnauthorizedAccessException)
        {
            // ignore cleanup failures in tests
        }
    }

    private static string? BackupExistingFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var backupPath = Path.Combine(Path.GetDirectoryName(path)!, $"license.lic.backup-{Guid.NewGuid():N}");
        File.Move(path, backupPath, overwrite: true);
        return backupPath;
    }
}
