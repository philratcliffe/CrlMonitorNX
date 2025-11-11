using System.Text.Json;

namespace CrlMonitor.Eula;

internal static class EulaAcceptanceManager
{
    private const string AcceptanceFileName = "eula-acceptance.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static string? AcceptanceFilePathOverride { get; set; }

    internal static Func<EulaMetadata, CancellationToken, ValueTask<bool>>? PromptOverride { get; set; }

    public static async Task EnsureAcceptedAsync(CancellationToken cancellationToken)
    {
        var metadata = EulaMetadataProvider.GetMetadata();
        var record = LoadAcceptance();
        if (record != null && Matches(record, metadata))
        {
            return;
        }

        if (!await PromptForAcceptanceAsync(metadata, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Licence agreement not accepted.");
        }

        var acceptanceRecord = new EulaAcceptanceRecord {
            LicenseAccepted = true,
            AcceptedDate = DateTime.UtcNow,
            AcceptedLicenseHash = metadata.Hash,
            AcceptedLicenseVersion = metadata.Version,
            AcceptedLicenseEffectiveDate = metadata.EffectiveDate
        };
        SaveAcceptance(acceptanceRecord);
    }

    private static EulaAcceptanceRecord? LoadAcceptance()
    {
        var path = GetAcceptanceFilePath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<EulaAcceptanceRecord>(json, JsonOptions);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void SaveAcceptance(EulaAcceptanceRecord record)
    {
        var path = GetAcceptanceFilePath();
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _ = Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(record, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to persist EULA acceptance info: {ex.Message}", ex);
        }
    }

    private static string GetAcceptanceFilePath()
    {
        return string.IsNullOrWhiteSpace(AcceptanceFilePathOverride)
            ? Path.Combine(AppContext.BaseDirectory, AcceptanceFileName)
            : AcceptanceFilePathOverride!;
    }

    private static bool Matches(EulaAcceptanceRecord record, EulaMetadata metadata)
    {
        return record.LicenseAccepted &&
               string.Equals(record.AcceptedLicenseHash, metadata.Hash, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(record.AcceptedLicenseVersion, metadata.Version, StringComparison.Ordinal) &&
               string.Equals(record.AcceptedLicenseEffectiveDate, metadata.EffectiveDate, StringComparison.Ordinal);
    }

    private static async ValueTask<bool> PromptForAcceptanceAsync(EulaMetadata metadata, CancellationToken cancellationToken)
    {
        if (PromptOverride != null)
        {
            return await PromptOverride(metadata, cancellationToken).ConfigureAwait(false);
        }

        if (!Console.IsInputRedirected && !Console.IsOutputRedirected)
        {
            Console.WriteLine();
            Console.WriteLine("=========== BEGIN EULA ===========");
            Console.WriteLine(metadata.Text);
            Console.WriteLine("============ END EULA ============");
            Console.WriteLine();
            Console.Write("Type 'accept' to agree and continue [accept/decline]: ");
            var response = Console.ReadLine();
            return string.Equals(response, "accept", StringComparison.OrdinalIgnoreCase);
        }

        throw new InvalidOperationException("Cannot display the licence agreement in the current environment.");
    }
}
