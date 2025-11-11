using System.Text.Json;
using CrlMonitor.Eula;

namespace CrlMonitor.Tests.Eula;

/// <summary>
/// Tests for <see cref="EulaAcceptanceManager"/>.
/// </summary>
public static class EulaAcceptanceManagerTests
{
    /// <summary>
    /// Verifies that acceptance info is persisted when the user agrees.
    /// </summary>
    [Fact]
    public static async Task EnsureAcceptedCreatesAcceptanceFile()
    {
        using var scope = new AcceptanceTestScope();
        var metadata = EulaMetadataProvider.GetMetadata();
        var called = false;
        EulaAcceptanceManager.PromptOverride = (_, _) => {
            called = true;
            return ValueTask.FromResult(true);
        };

        await EulaAcceptanceManager.EnsureAcceptedAsync(CancellationToken.None);

        Assert.True(called);
        var record = scope.ReadRecord();
        Assert.True(record.LicenseAccepted);
        Assert.Equal(metadata.Hash, record.AcceptedLicenseHash);
        Assert.Equal(metadata.Version, record.AcceptedLicenseVersion);
        Assert.Equal(metadata.EffectiveDate, record.AcceptedLicenseEffectiveDate);
    }

    /// <summary>
    /// Verifies no prompt occurs when acceptance matches current EULA.
    /// </summary>
    [Fact]
    public static async Task EnsureAcceptedSkipsPromptWhenAlreadyAccepted()
    {
        using var scope = new AcceptanceTestScope();
        var metadata = EulaMetadataProvider.GetMetadata();
        scope.WriteRecord(new EulaAcceptanceRecord {
            LicenseAccepted = true,
            AcceptedDate = DateTime.UtcNow,
            AcceptedLicenseHash = metadata.Hash,
            AcceptedLicenseVersion = metadata.Version,
            AcceptedLicenseEffectiveDate = metadata.EffectiveDate
        });

        var called = false;
        EulaAcceptanceManager.PromptOverride = (_, _) => {
            called = true;
            return ValueTask.FromResult(true);
        };

        await EulaAcceptanceManager.EnsureAcceptedAsync(CancellationToken.None);

        Assert.False(called);
    }

    /// <summary>
    /// Verifies that rejection results in an exception.
    /// </summary>
    [Fact]
    public static async Task EnsureAcceptedThrowsWhenUserDeclines()
    {
        using var scope = new AcceptanceTestScope();
        EulaAcceptanceManager.PromptOverride = (_, _) => ValueTask.FromResult(false);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => EulaAcceptanceManager.EnsureAcceptedAsync(CancellationToken.None));
    }

    private sealed class AcceptanceTestScope : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _path;

        public AcceptanceTestScope()
        {
            var directory = Directory.CreateTempSubdirectory();
            this._path = Path.Combine(directory.FullName, "eula.json");
            EulaAcceptanceManager.AcceptanceFilePathOverride = this._path;
        }

        public EulaAcceptanceRecord ReadRecord()
        {
            var json = File.ReadAllText(this._path);
            return JsonSerializer.Deserialize<EulaAcceptanceRecord>(json)!;
        }

        public void WriteRecord(EulaAcceptanceRecord record)
        {
            var json = JsonSerializer.Serialize(record, JsonOptions);
            File.WriteAllText(this._path, json);
        }

        public void Dispose()
        {
            EulaAcceptanceManager.AcceptanceFilePathOverride = null;
            EulaAcceptanceManager.PromptOverride = null;
            if (File.Exists(this._path))
            {
                File.Delete(this._path);
            }

            var dir = Path.GetDirectoryName(this._path);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
