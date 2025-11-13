# Trial Diagnostic Codes in CrlMonitor

## Overview

CrlMonitor logs TS (Trial Storage) diagnostic codes during startup to help diagnose trial licence issues. These codes indicate which storage locations successfully persisted or retrieved trial activation data.

## How CrlMonitor Uses RedKestrel.Licensing

### Licence Bootstrapping

During startup, `LicenseBootstrapper.EnsureLicensedAsync()` validates the licence:

```csharp
// 1. Validate licence file
var validation = await validator.ValidateAsync(cancellationToken);

// 2. If trial licence, enforce trial period
if (validation.License?.Type == LicenseType.Trial)
{
    await EnforceTrialAsync(cancellationToken);
}
```

### Trial Enforcement and Logging

```csharp
// Create trial options
var trialOptions = new TrialOptions {
    CompanyName = "RedKestrel",
    ProductName = "CrlMonitor",
    StorageKey = "CrlMonitor_Trial_2025-11-09",
    TrialDays = 30
};

// Create storage and manager
var storage = TrialStorageFactory.CreateDefault(trialOptions);
var manager = new TrialManager(trialOptions, storage);

// Evaluate trial status (returns TrialStatus with TS codes)
var status = await manager.EvaluateAsync(cancellationToken);

// Log trial status with TS codes
LoggingSetup.LogTrialStatus(status.DaysRemaining, status.ReadCode, status.WriteCode);
```

## Log File Examples

### First Run (Trial Activation)

```
2025-11-13 17:30:45.123 +00:00 [INF] CRL Monitor starting
2025-11-13 17:30:45.234 +00:00 [INF] Version: 2.0.0.0
2025-11-13 17:30:45.345 +00:00 [INF] License validation: VALID
2025-11-13 17:30:45.456 +00:00 [INF] License type: Trial
2025-11-13 17:30:45.567 +00:00 [INF] Trial period: VALID (30 days remaining)
2025-11-13 17:30:45.678 +00:00 [INF] TS Read Code: TS-R-0000
2025-11-13 17:30:45.789 +00:00 [INF] TS Write Code: TS-W-0111
```

**Interpretation**:
- `TS-R-0000` = No trial data found (expected on first run)
- `TS-W-0111` = File, Settings, Registry written successfully
  - Binary: `0111`
  - Bit 3 (IsolatedStorage): `0` = Failed
  - Bit 2 (Registry): `1` = Success
  - Bit 1 (Settings): `1` = Success
  - Bit 0 (File): `1` = Success

### Subsequent Run (Normal Operation)

```
2025-11-14 08:15:30.123 +00:00 [INF] CRL Monitor starting
2025-11-14 08:15:30.234 +00:00 [INF] Version: 2.0.0.0
2025-11-14 08:15:30.345 +00:00 [INF] License validation: VALID
2025-11-14 08:15:30.456 +00:00 [INF] License type: Trial
2025-11-14 08:15:30.567 +00:00 [INF] Trial period: VALID (29 days remaining)
2025-11-14 08:15:30.678 +00:00 [INF] TS Read Code: TS-R-0111
```

**Interpretation**:
- `TS-R-0111` = File, Settings, Registry readable
- No write code logged (only logged on first run)
- Trial data successfully retrieved from multiple locations

### Partial Failure Example

```
2025-11-15 12:00:00.123 +00:00 [INF] CRL Monitor starting
2025-11-15 12:00:00.234 +00:00 [INF] License validation: VALID
2025-11-15 12:00:00.345 +00:00 [INF] License type: Trial
2025-11-15 12:00:00.456 +00:00 [INF] Trial period: VALID (28 days remaining)
2025-11-15 12:00:00.567 +00:00 [INF] TS Read Code: TS-R-0011
```

**Interpretation**:
- `TS-R-0011` = Only File and Settings readable
  - Binary: `0011`
  - Bit 3 (IsolatedStorage): `0` = Failed
  - Bit 2 (Registry): `0` = Failed
  - Bit 1 (Settings): `1` = Success
  - Bit 0 (File): `1` = Success
- Trial still works (at least one location succeeded)
- Registry and IsolatedStorage unavailable

## Understanding TS Codes

### Format

```
TS-{R|W}-bbbb
```

- `R` = Read operation (loading existing trial data)
- `W` = Write operation (creating new trial data)
- `bbbb` = 4-bit binary indicating success (1) or failure (0)

### Bit Position to Storage Location Mapping

**Critical**: Bit positions are **left to right** (MSB to LSB).

```
TS Code:  TS-R-0111
Binary:         ||||
Bit:            3210
                ||||
Position 3 ────>0 = IsolatedStorage FAILED
Position 2 ────>1 = Registry SUCCESS
Position 1 ────>1 = Settings SUCCESS
Position 0 ────>1 = File SUCCESS
```

### Storage Location Details

| Position | Bit | Storage | Location (macOS/Linux) |
|----------|-----|---------|------------------------|
| 3 (MSB) | Leftmost | IsolatedStorage | User isolated storage |
| 2 | Middle-left | Registry | N/A (Windows only) |
| 1 | Middle-right | Settings | `~/.local/share/RedKestrel/CrlMonitor/settings.json` |
| 0 (LSB) | Rightmost | File | `~/.local/share/RedKestrel/CrlMonitor/.data_d3a9a070669681f7` |

| Position | Bit | Storage | Location (Windows) |
|----------|-----|---------|-------------------|
| 3 (MSB) | Leftmost | IsolatedStorage | User isolated storage |
| 2 | Middle-left | Registry | `HKCU\Software\RedKestrel\CrlMonitor\{hash}` |
| 1 | Middle-right | Settings | `%ProgramData%\RedKestrel\CrlMonitor\settings.json` |
| 0 (LSB) | Rightmost | File | `%ProgramData%\RedKestrel\CrlMonitor\.data_d3a9a070669681f7` |

### Quick Reference Table

| Code | Binary | File | Settings | Registry | IsolatedStorage |
|------|--------|------|----------|----------|-----------------|
| `TS-R-1111` | 1111 | ✓ | ✓ | ✓ | ✓ |
| `TS-R-0111` | 0111 | ✓ | ✓ | ✓ | ✗ |
| `TS-R-1011` | 1011 | ✓ | ✓ | ✗ | ✓ |
| `TS-R-0011` | 0011 | ✓ | ✓ | ✗ | ✗ |
| `TS-R-1101` | 1101 | ✓ | ✗ | ✓ | ✓ |
| `TS-R-0101` | 0101 | ✓ | ✗ | ✓ | ✗ |
| `TS-R-1001` | 1001 | ✓ | ✗ | ✗ | ✓ |
| `TS-R-0001` | 0001 | ✓ | ✗ | ✗ | ✗ |
| `TS-R-0000` | 0000 | ✗ | ✗ | ✗ | ✗ |

## Common Scenarios

### Healthy First Run (macOS/Linux)

```
TS Read Code: TS-R-0000  (no data found)
TS Write Code: TS-W-0111 (File, Settings, Registry written)
```

**Note**: Registry shows success despite being N/A because it's skipped gracefully on non-Windows platforms.

### Healthy Subsequent Run (macOS/Linux)

```
TS Read Code: TS-R-0111  (File, Settings, Registry readable)
```

No write code logged (only appears on first run).

### Settings File Corrupted

```
TS Read Code: TS-R-0101  (File and Registry readable, Settings failed)
```

**Action**: Trial still works. Settings file may be corrupted but other locations provide backup.

### All Storage Failed (First Run)

```
TS Read Code: TS-R-0000  (no data found - expected)
TS Write Code: TS-W-0000 (all writes failed)
```

**Action**: Critical error. Application cannot activate trial. Check:
- Disk space
- File permissions
- Program data directory accessibility

### All Storage Failed (Subsequent Run)

```
TS Read Code: TS-R-0000  (no data readable)
```

**Likely Causes**:
- Trial data deleted
- Storage locations corrupted
- Wrong storage key/hash
- Severe permissions issue

## Troubleshooting Guide

### Read Code: TS-R-0000 (After First Run)

**Symptoms**: Trial resets or appears as first run
**Possible Causes**:
1. User deleted trial files
2. Application uninstalled/reinstalled
3. Storage key changed in code
4. Permissions changed blocking access

**Resolution**:
- Check if trial files exist in storage locations
- Verify file permissions
- Check application logs for storage access errors

### Read Code: TS-R-0001 (File Only)

**Symptoms**: Only file storage working
**Possible Causes**:
1. Isolated storage corrupted
2. Registry access blocked (Windows)
3. Settings JSON corrupted

**Resolution**:
- Trial continues to work (partial success acceptable)
- Consider investigating why other storage failed
- Check file permissions on settings.json

### Write Code: TS-W-0000

**Symptoms**: Cannot activate trial
**Possible Causes**:
1. Disk full
2. No write permissions to program data directory
3. Anti-virus blocking writes
4. Directory doesn't exist and cannot be created

**Resolution**:
- Check disk space
- Verify directory exists: `~/.local/share/RedKestrel/CrlMonitor/` (Linux/Mac)
- Check write permissions
- Temporarily disable anti-virus to test

### Partial Success Codes (e.g., TS-R-0011, TS-W-0101)

**Symptoms**: Some storage locations unavailable
**Impact**: Trial continues to work (designed for redundancy)

**Action**:
- Trial functionality maintained
- Optional: Investigate why some locations failed
- Monitor for patterns (e.g., always same bits failing)

## Platform-Specific Expected Codes

### Windows

**First Run**:
- Read: `TS-R-0000`
- Write: `TS-W-1111` (all 4 locations)

**Subsequent Run**:
- Read: `TS-R-1111`

### macOS / Linux

**First Run**:
- Read: `TS-R-0000`
- Write: `TS-W-0111` (Registry bit shows success but N/A on these platforms)

**Subsequent Run**:
- Read: `TS-R-0111`

## When to Escalate

### Critical Issues (Requires Immediate Action)

1. **TS-W-0000**: Cannot write to any storage location
   - Trial activation fails
   - User cannot start trial

2. **TS-R-0000 after activation**: All trial data lost
   - Trial resets unexpectedly
   - Potential tampering or data loss

### Warning Issues (Monitor)

1. **Consistently low bit count**: e.g., always `TS-R-0001`
   - Trial works but lacks redundancy
   - Single point of failure

2. **Changing patterns**: Different bits failing across runs
   - Storage instability
   - Potential filesystem issues

### Informational (No Action Required)

1. **Partial success with 2+ bits set**: e.g., `TS-R-0011`, `TS-R-0111`
   - Normal operation
   - Redundancy working as designed

## Related Files

- `Licensing/LicenseBootstrapper.cs` - Trial enforcement and logging
- `Logging/LoggingSetup.cs` - Trial status logging implementation
- See `RedKestrel.Licensing/TRIAL-STORAGE.md` for detailed storage implementation

## Storage Key

Current CrlMonitor storage key: `CrlMonitor_Trial_2025-11-09`

SHA256 hash (first 16 chars): `d3a9a070669681f7`

This hash is used in:
- File storage: `.data_d3a9a070669681f7`
- Settings key: `ProfileCreated_d3a9a070669681f7`
- Registry path: `...\CrlMonitor\d3a9a070669681f7` (Windows)
- Isolated storage: `.data_d3a9a070669681f7`
