# Obfuscation with Single-File Publishing

## The Challenge

By default, Obfuscar and `PublishSingleFile=true` are incompatible:

1. **Traditional obfuscation workflow** (used by SslDecoder):
   ```
   dotnet publish → produces 100+ DLL files → run Obfuscar → produces obfuscated DLLs
   ```
   - Result: Multi-file deployment with obfuscated code

2. **Single-file publishing workflow**:
   ```
   dotnet publish → bundles all DLLs into single .exe during publish
   ```
   - Problem: No separate DLL files exist after bundling to obfuscate

## The Solution: MSBuild Pipeline Integration

Instead of running Obfuscar after publishing, we integrate it **during** the publish process using MSBuild targets.

### How It Works

```
dotnet publish with PublishSingleFile=true
  ↓
1. Compile CrlMonitor.dll and dependencies to bin/Release/net8.0/win-x64/
  ↓
2. [OUR OBFUSCATION TARGET RUNS HERE] ←─────────┐
   - Read CrlMonitor.dll from output directory   │
   - Obfuscate to obj/Release/.../obfuscated/    │ Injected via
   - Replace original with obfuscated version     │ MSBuild target
  ↓                                               │
3. GenerateSingleFileBundle reads obfuscated DLL ─┘
  ↓
4. Creates single CrlMonitor.exe with obfuscated code bundled inside
```

### Key Implementation Details

#### 1. MSBuild Target Timing

The critical part is **when** the obfuscation runs:

```xml
<Target Name="ObfuscateBeforeSingleFile"
        AfterTargets="_CopyFilesToPublishDirectory"
        BeforeTargets="GenerateSingleFileBundle"
        Condition="'$(PublishSingleFile)' == 'true' AND '$(Configuration)' == 'Release'">
```

- **AfterTargets**: Ensures DLL exists in output directory
- **BeforeTargets**: Runs before bundling into single file
- **Condition**: Only runs for Release single-file builds

#### 2. Path Resolution

For single-file publishing, files are bundled from `$(OutDir)`, not `$(PublishDir)`:

```xml
<IntermediatePublishDir>$(OutDir)</IntermediatePublishDir>
<!-- e.g. bin/Release/net8.0/win-x64/ -->
```

#### 3. Dynamic Configuration

Obfuscar doesn't accept MSBuild properties via command line, so we:

1. Create `obfuscar.xml` template with placeholders:
   ```xml
   <Var name="InPath" value="INPATH_PLACEHOLDER" />
   <Var name="OutPath" value="OUTPATH_PLACEHOLDER" />
   ```

2. MSBuild reads template, replaces placeholders, writes temp config:
   ```xml
   <ObfuscarTemplate>$([System.IO.File]::ReadAllText('obfuscar.xml'))</ObfuscarTemplate>
   <ObfuscarConfigWithPaths>$(ObfuscarTemplate.Replace('INPATH_PLACEHOLDER', '$(IntermediatePublishDir)')...)</ObfuscarConfigWithPaths>
   ```

3. Run Obfuscar on generated temp config

#### 4. JSON Serialization Protection

CrlMonitor uses `[JsonPropertyName]` attributes on all JSON-serialized classes:

```csharp
public sealed record ConfigDocument
{
    [JsonPropertyName("console_reports")]
    public bool? ConsoleReports { get; init; }
    // ...
}
```

This protects JSON field names even when C# property names are obfuscated. No `<SkipType>` exclusions needed.

### Files and Configuration

**Created/Modified:**

1. **CrlMonitor.csproj** - MSBuild obfuscation target (lines 53-95)
2. **obfuscar.xml** - Obfuscation configuration template
3. **.config/dotnet-tools.json** - Declares obfuscar.globaltool dependency

**Obfuscation settings:**

- String hiding enabled
- Property/field/event renaming
- Method optimization
- Unicode names (maximum obfuscation)
- ILDASM suppression

### Requirements

**Build-time:**
- `dotnet tool restore` must run to install obfuscar.globaltool
- Only activates for Release builds with `PublishSingleFile=true`

**Runtime:**
- No impact on runtime behaviour
- All JSON serialization protected by `[JsonPropertyName]` attributes

### Verification

Obfuscation success can be verified by checking:

1. **Build output** shows obfuscation messages:
   ```
   ====== Running Obfuscar on CrlMonitor.dll ======
   info: Completed in 0.19 seconds
   ====== Obfuscation complete ======
   ```

2. **Mapping file** created at `obj/Release/net8.0/win-x64/obfuscated/Mapping.txt`:
   ```
   [CrlMonitor]CrlMonitor.ConfigLoader -> [CrlMonitor] .
   [CrlMonitor]CrlMonitor.CrlConfigEntry -> [CrlMonitor] .
   ```

3. **Single-file exe** created at expected size (~150MB for win-x64)

### Comparison to SslDecoder

| Aspect | SslDecoder | CrlMonitor |
|--------|-----------|------------|
| Obfuscation tool | obfuscar.globaltool | obfuscar.globaltool |
| When runs | After `dotnet publish` completes | During `dotnet publish` (MSBuild hook) |
| Output | 100+ files (DLLs + support files) | Single .exe file |
| JSON protection | `<SkipType>` exclusions in obfuscar.xml | `[JsonPropertyName]` attributes (no exclusions) |
| Script integration | publish-*.sh invokes obfuscar manually | MSBuild invokes obfuscar automatically |

### Troubleshooting

**"Unable to find assembly: CrlMonitor.dll"**
- Check MSBuild target runs after DLL is built but before bundling
- Verify `$(OutDir)` contains CrlMonitor.dll before obfuscation runs

**JSON deserialization errors after obfuscation**
- Ensure all JSON DTOs have `[JsonPropertyName]` attributes
- Check obfuscar.xml doesn't have conflicting `<SkipType>` entries

**Obfuscation not running**
- Confirm `PublishSingleFile=true` and `Configuration=Release`
- Run `dotnet tool restore` to install obfuscar.globaltool
- Check MSBuild verbosity: `dotnet publish -v:detailed`

### Credits

Solution based on MSBuild pipeline integration approach suggested by Gemini AI, adapted for .NET 8 single-file publishing with Obfuscar.
