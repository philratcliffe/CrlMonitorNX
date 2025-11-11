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
            throw new InvalidOperationException("ERROR: Licence agreement not accepted.\nPlease run the application again and accept the EULA to continue.");
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
        if (!string.IsNullOrWhiteSpace(AcceptanceFilePathOverride))
        {
            return AcceptanceFilePathOverride!;
        }

        // Try ProgramData on Windows first, fallback to exe directory
        if (OperatingSystem.IsWindows())
        {
            var programDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "RedKestrel",
                "CrlMonitor",
                "accepted.json");

            // Test if we can write to ProgramData location
            var directory = Path.GetDirectoryName(programDataPath);
            if (!string.IsNullOrWhiteSpace(directory) && TryEnsureDirectoryWritable(directory))
            {
                RemoveLegacyAcceptanceFile(programDataPath);
                return programDataPath;
            }
        }

        // Fallback to executable directory
        var path = Path.Combine(GetExecutableDirectory(), AcceptanceFileName);
        RemoveLegacyAcceptanceFile(path);
        return path;
    }

    private static bool TryEnsureDirectoryWritable(string directory)
    {
#pragma warning disable CA1031 // Defensive: test if directory writable, any exception means not writable
        try
        {
            _ = Directory.CreateDirectory(directory);
            return true;
        }
        catch
        {
            return false;
        }
#pragma warning restore CA1031
    }

    private static string GetExecutableDirectory()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            return Path.GetFullPath(baseDirectory);
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var processDirectory = Path.GetDirectoryName(Path.GetFullPath(processPath));
            if (!string.IsNullOrWhiteSpace(processDirectory))
            {
                return processDirectory!;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static void RemoveLegacyAcceptanceFile(string targetPath)
    {
        try
        {
            var legacyPath = Path.Combine(AppContext.BaseDirectory, AcceptanceFileName);
            var targetFullPath = Path.GetFullPath(targetPath);
            var legacyFullPath = Path.GetFullPath(legacyPath);
            if (!string.Equals(targetFullPath, legacyFullPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(legacyFullPath))
            {
                File.Delete(legacyFullPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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
#if WINDOWS
            if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            {
                return WindowsEulaDialog.TryShow(metadata);
            }
#endif
            DisplayEulaWithPaging(metadata.Text);
            Console.WriteLine();
            Console.Write("Type 'accept' to agree and continue [accept/decline]: ");
            var response = Console.ReadLine();
            return string.Equals(response, "accept", StringComparison.OrdinalIgnoreCase);
        }

        throw new InvalidOperationException("Cannot display the licence agreement in the current environment.");
    }

    private static void DisplayEulaWithPaging(string text)
    {
        const int PageSize = 24;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        Console.WriteLine("=========== BEGIN EULA ===========");
        for (var i = 0; i < lines.Length; i++)
        {
            Console.WriteLine(lines[i]);
            if ((i + 1) % PageSize == 0 && i + 1 < lines.Length)
            {
                Console.WriteLine();
                Console.WriteLine("-- Press Enter to continue, or type 'q' then Enter to abort --");
                var response = Console.ReadLine();
                if (string.Equals(response, "q", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Licence agreement not accepted.");
                }
            }
        }

        Console.WriteLine("============ END EULA ============");
    }

}
