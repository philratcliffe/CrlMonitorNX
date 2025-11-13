# Code Review Instructions for Codex

## Overview

Please review the implementation of TS (Trial Storage) diagnostic codes across two repositories:
1. RedKestrel.Licensing library
2. CrlMonitorNX application

This feature adds diagnostic codes to track which trial data storage locations succeed or fail during read/write operations.

## What Was Implemented

### Core Feature: TS Diagnostic Codes

TS codes indicate which storage backends successfully read/wrote trial data:
- Format: `TS-{R|W}-bbbb` where bbbb is 4-bit binary
- Bit 3 (leftmost): IsolatedStorage
- Bit 2: Registry (Windows)
- Bit 1: Settings (JSON)
- Bit 0 (rightmost): File
- Example: `TS-R-1011` = File, Settings, IsolatedStorage readable (typical macOS/Linux)
- Example: `TS-R-0111` = File, Settings, Registry readable (Windows with IsolatedStorage failed)

### New Storage Backend

Added SettingsTrialStorage as 4th storage location:
- JSON file with `ProfileCreated_{hash}` keys
- ISO 8601 timestamp values
- Cached JsonSerializerOptions to satisfy CA1869

## Repository 1: RedKestrel.Licensing

**Location**: `/Users/philratcliffe/RiderProjects/RedKestrel.Licensing`

### Commits to Review (7 total)

1. **Add TS diagnostic code generation for trial storage**
   - Files: `Trial/TrialDiagnostics.cs`, `Trial/StorageOperationResult.cs`
   - Tests: `RedKestrel.Licensing.Tests/TrialDiagnosticsTests.cs`

2. **Add SettingsTrialStorage for JSON-based trial persistence**
   - Files: `Trial/SettingsTrialStorage.cs`
   - Tests: `RedKestrel.Licensing.Tests/SettingsTrialStorageTests.cs`

3. **Add diagnostic tracking to CompositeTrialStorage**
   - Files: `Trial/CompositeTrialStorage.cs`
   - Tests: `RedKestrel.Licensing.Tests/CompositeTrialStorageTests.cs`

4. **Add TS diagnostic codes to TrialStatus and TrialManager**
   - Files: `Trial/TrialManager.cs`
   - Tests: `RedKestrel.Licensing.Tests/TrialManagerTests.cs`

5. **Add SettingsTrialStorage to default factory storage chain**
   - Files: `Trial/TrialStorageFactory.cs`
   - Tests: Existing TrialManagerTests validates factory integration

6. **Add trial storage and TS diagnostic code documentation**
   - Files: `TRIAL-STORAGE.md`

### Files to Review in Detail

#### New Files
- `RedKestrel.Licensing/Trial/TrialDiagnostics.cs`
- `RedKestrel.Licensing/Trial/StorageOperationResult.cs`
- `RedKestrel.Licensing/Trial/SettingsTrialStorage.cs`
- `RedKestrel.Licensing.Tests/TrialDiagnosticsTests.cs`
- `RedKestrel.Licensing.Tests/SettingsTrialStorageTests.cs`
- `TRIAL-STORAGE.md`

#### Modified Files
- `RedKestrel.Licensing/Trial/CompositeTrialStorage.cs`
  - Added: `ReadEarliestWithDiagnosticsAsync()` method
  - Added: `WriteAllWithDiagnosticsAsync()` method

- `RedKestrel.Licensing/Trial/TrialManager.cs`
  - Modified: `EvaluateAsync()` to use diagnostic methods and generate TS codes
  - Modified: `TrialStatus` record to include `ReadCode` and `WriteCode` properties

- `RedKestrel.Licensing/Trial/TrialStorageFactory.cs`
  - Modified: `CreateDefault()` to include SettingsTrialStorage in chain

- `RedKestrel.Licensing.Tests/CompositeTrialStorageTests.cs`
  - Added: 2 tests for diagnostic methods

- `RedKestrel.Licensing.Tests/TrialManagerTests.cs`
  - Added: 2 tests for TS code population

### Test Coverage

All tests passing: **46 tests total**

New tests added:
- 13 tests in TrialDiagnosticsTests.cs (all TS code generation scenarios)
- 9 tests in SettingsTrialStorageTests.cs (full SettingsTrialStorage coverage)
- 2 tests in CompositeTrialStorageTests.cs (diagnostic methods)
- 2 tests in TrialManagerTests.cs (TS code population)

## Repository 2: CrlMonitorNX

**Location**: `/Users/philratcliffe/RiderProjects/CrlMonitorNX`

### Commits to Review (2 total)

1. **Add TS diagnostic code logging to trial status**
   - Files: `Logging/LoggingSetup.cs`, `Licensing/LicenseBootstrapper.cs`

2. **Add TS diagnostic code interpretation docs**
   - Files: `docs/TRIAL-DIAGNOSTICS.md`

### Files to Review in Detail

#### Modified Files
- `Logging/LoggingSetup.cs`
  - Modified: `LogTrialStatus()` signature to accept `readCode` and `writeCode` parameters
  - Added: Logging for TS Read Code (always)
  - Added: Conditional logging for TS Write Code (first run only)

- `Licensing/LicenseBootstrapper.cs`
  - Modified: `EnforceTrialAsync()` to pass TS codes from TrialStatus to logging
  - Removed: TODO comment about TS codes

#### New Files
- `docs/TRIAL-DIAGNOSTICS.md`

## Review Checklist

### Code Quality

- [ ] **AGENTS.md Compliance**: Verify all code follows `/Users/philratcliffe/RiderProjects/CrlMonitorNX/AGENTS.md` rules
  - No warnings disabled (check for `#pragma warning disable`)
  - UK spelling in code and comments
  - Concise commit messages following 50/72 rule
  - No references to Claude/AI in commits

- [ ] **Analyzer Compliance**: All CA* and IDE* rules satisfied
  - CA1869: JsonSerializerOptions cached in SettingsTrialStorage
  - CA1307: StringComparison.Ordinal used in all string comparisons
  - CA1707: No underscores in test method names
  - IDE0046, IDE0028, IDE0090, IDE0058, IDE0005 all satisfied

- [ ] **Test Coverage**: All new code has comprehensive tests
  - TrialDiagnostics: All code paths covered (13 tests)
  - SettingsTrialStorage: Read/write success/failure scenarios (9 tests)
  - CompositeTrialStorage: Diagnostic methods tested (2 tests)
  - TrialManager: TS code population verified (2 tests)

### Correctness

- [ ] **TS Code Bit Ordering**: Verify left-to-right (MSB to LSB) mapping
  - Bit 3 (leftmost) = IsolatedStorage
  - Bit 2 = Registry
  - Bit 1 = Settings
  - Bit 0 (rightmost) = File
  - Check TrialDiagnostics.cs implementation matches documentation

- [ ] **Storage Order Consistency**: Factory creates storage in correct order
  - File (first)
  - Settings (second)
  - Registry (third, Windows only)
  - IsolatedStorage (fourth)
  - Matches SslDecoder pattern

- [ ] **SettingsTrialStorage Implementation**:
  - ISO 8601 format used for timestamps ("O" format)
  - Key format: `ProfileCreated_{hash}`
  - UTC conversion applied correctly
  - Exception handling covers all failure scenarios

- [ ] **Diagnostic Methods**: CompositeTrialStorage correctly tracks per-storage success
  - Type checking uses `is` operator for each storage implementation
  - Earliest timestamp logic preserved
  - Success flags set correctly

- [ ] **TrialStatus Changes**: Backward compatibility maintained
  - ReadCode always populated
  - WriteCode nullable (only on first run)
  - DaysRemaining, DaysElapsed, IsValid logic unchanged

### Integration

- [ ] **CrlMonitor Logging**:
  - TS codes passed from TrialStatus to LoggingSetup correctly
  - Read code always logged
  - Write code conditionally logged (null check)
  - Log format matches expected pattern

- [ ] **Cross-Repository Integration**:
  - CrlMonitorNX correctly references updated RedKestrel.Licensing
  - API surface changes compatible
  - No breaking changes to existing consumers

### Documentation

- [ ] **TRIAL-STORAGE.md** (RedKestrel.Licensing):
  - Explains all 4 storage locations
  - TS code format documented correctly
  - Bit mapping clearly explained
  - Example codes cover common scenarios
  - Platform-specific behavior documented
  - Troubleshooting guide comprehensive

- [ ] **TRIAL-DIAGNOSTICS.md** (CrlMonitorNX):
  - Log file examples accurate
  - TS code interpretation clear
  - Bit position mapping matches implementation
  - Storage location paths correct for macOS/Linux/Windows
  - Quick reference table accurate
  - Troubleshooting scenarios practical

## Specific Areas of Concern

### 1. TS Code Bit Ordering

**Critical**: Verify the bit ordering is consistent across:
- `TrialDiagnostics.cs` implementation
- `TRIAL-STORAGE.md` documentation
- `TRIAL-DIAGNOSTICS.md` documentation
- All test assertions

Current implementation uses **left-to-right** (MSB to LSB):
```csharp
$"TS-R-{bit3}{bit2}{bit1}{bit0}"
//       ^    ^    ^    ^
//       ISO  REG  SET  FILE
```

### 2. Storage Order in Factory

Verify `TrialStorageFactory.CreateDefault()` creates storage in this order:
1. FileTrialStorage
2. SettingsTrialStorage ← NEW
3. RegistryTrialStorage (Windows only)
4. IsolatedStorageTrialStorage

This order must match the bit ordering in TS codes.

### 3. SettingsTrialStorage JSON Format

Verify settings.json format matches specification:
```json
{
  "ProfileCreated_D3A9A070": "2025-11-13T17:30:45.1234567Z"
}
```

Key points:
- Key prefix: `ProfileCreated_`
- Hash: 8 uppercase hex characters (first 4 bytes of SHA256)
- Timestamp format: ISO 8601 ("O" format)
- Multiple trial keys supported in same file

### 4. Null Safety

Verify nullable annotations correct:
- `TrialStatus.WriteCode` is nullable (`string?`)
- `LoggingSetup.LogTrialStatus()` checks WriteCode for null before logging
- Test assertions handle null WriteCode correctly

### 5. Test Method Naming

Verify all test methods follow existing convention (PascalCase without underscores):
- ✓ `GenerateTsReadCodeAllSuccessReturns1111`
- ✗ `GenerateTsReadCode_AllSuccess_Returns1111`

No `#pragma warning disable CA1707` should be present.

## Commands to Run

### Build Both Projects
```bash
# RedKestrel.Licensing
cd /Users/philratcliffe/RiderProjects/RedKestrel.Licensing
dotnet build --verbosity normal

# CrlMonitorNX
cd /Users/philratcliffe/RiderProjects/CrlMonitorNX
dotnet build --verbosity normal
```

### Run Tests
```bash
# RedKestrel.Licensing (46 tests)
cd /Users/philratcliffe/RiderProjects/RedKestrel.Licensing
dotnet test --verbosity normal

# CrlMonitorNX
cd /Users/philratcliffe/RiderProjects/CrlMonitorNX
dotnet test CrlMonitor.Tests/CrlMonitor.Tests.csproj --verbosity normal
```

### View Commits
```bash
# RedKestrel.Licensing (last 7 commits)
cd /Users/philratcliffe/RiderProjects/RedKestrel.Licensing
git log --oneline -7

# CrlMonitorNX (last 2 commits)
cd /Users/philratcliffe/RiderProjects/CrlMonitorNX
git log --oneline -2
```

### View Diffs
```bash
# RedKestrel.Licensing
cd /Users/philratcliffe/RiderProjects/RedKestrel.Licensing
git diff HEAD~7..HEAD

# CrlMonitorNX
cd /Users/philratcliffe/RiderProjects/CrlMonitorNX
git diff HEAD~2..HEAD
```

## Expected Review Outcome

Please provide:

1. **Overall Assessment**: Pass/Fail with summary
2. **Issues Found**: List of bugs, inconsistencies, or violations
3. **Suggestions**: Non-blocking improvements or optimizations
4. **Documentation Review**: Accuracy and completeness of both markdown files
5. **Test Coverage Analysis**: Any gaps in test scenarios

## Priority Issues to Flag

**Critical** (Must Fix):
- Incorrect bit ordering in TS codes
- Storage order mismatch
- Test failures
- Warning suppression with `#pragma`
- Breaking API changes

**High** (Should Fix):
- Incorrect documentation
- Missing null checks
- Exception handling gaps
- Test coverage gaps

**Medium** (Nice to Have):
- Code style improvements
- Documentation clarity
- Additional test scenarios

## Context

This implementation follows the pattern established in SslDecoder (another RedKestrel project) which uses similar diagnostic codes. The goal is to help diagnose trial licence issues in production by logging which storage locations are functioning.

Trial data is persisted to 4 independent locations for redundancy. The system continues to work as long as at least one location succeeds, but the TS codes help identify partial failures.

## Questions to Consider

1. Is the bit ordering intuitive and clearly documented?
2. Are all edge cases covered in tests?
3. Is the logging output useful for troubleshooting?
4. Are the documentation examples accurate and helpful?
5. Could any code be simplified without losing functionality?
6. Are there any security concerns with logging diagnostic codes?
7. Is the JsonSerializerOptions caching implemented correctly?
8. Do the docs accurately reflect the implementation?
