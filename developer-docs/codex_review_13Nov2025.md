# Codex Review – 13 Nov 2025

**Scope**: Entire `CrlMonitorNX` repository @ `3bd06dc4`  
**Reviewer**: Codex (automated)  
**Tests executed**: `dotnet test CrlMonitor.Tests/CrlMonitor.Tests.csproj --verbosity minimal`

---

## Findings (ordered by severity)

1. **Absolute Linux `file://` URIs resolve to the wrong path**  
   `ConfigLoader.TryCreateFileUri()` trims every leading `/` before combining with the config directory (`ConfigLoader.cs:155-170`). A URI such as `file:///etc/pki/foo.crl` is converted to a relative segment `etc/pki/foo.crl`, so the monitor attempts to read `<configDir>/etc/pki/foo.crl` instead of `/etc/pki/foo.crl`. Result: absolute file URIs silently point at the wrong file, and duplicate detection may also misbehave because the normalized `Uri` never matches the actual file. **Fix**: treat paths that start with `/` on Unix as absolute (skip the `TrimStart('/', '\\')` and allow `Path.GetFullPath` to preserve the root), and add regression tests that cover both Unix and Windows-style absolute `file://` values.

2. **Functional specification still references `uri_list.txt` input**  
   The functional spec describes the CLI workflow as “User supplies config (`config.json`) and URI list (`uri_list.txt`).” (`developer-docs/crl-monitor-functional-spec.md:29-33`). The current implementation only loads URIs from the JSON document and never touches `uri_list.txt`, so anyone following the spec will expect an extra file that is neither parsed nor validated. **Fix**: update the spec (and any onboarding materials that point at it) to describe the actual JSON-only configuration shape, or reinstate support for `uri_list.txt` if it’s still required.

3. **Warning suppressions lack the mandated justification comments**  
   Repository policy (AGENTS.md:17-21 & 89-92) states that every suppressed warning must include a detailed inline explanation. Files such as `Program.cs:149-177` and `Reporting/ConsoleReporter.cs:69-141` disable CA1303 around routine console writes without any comment beyond the pragma itself. The same pattern repeats dozens of times throughout `ConsoleReporter`. This violates the coding directive and leaves reviewers guessing why globalization warnings were suppressed instead of addressed. **Fix**: either remove the suppressions by localizing strings properly or add a one-line justification next to each pragma explaining why the warning cannot be fixed (for example, “console utility intentionally emits English diagnostics”). Running analyzers after the change ensures no undocumented suppressions linger.

4. **Days-to-expiry logging mixes UTC expiry with local time**  
   Both `Logging/LoggingSetup.cs:67-78` and `Licensing/LicenseBootstrapper.cs:154-162` compute `daysUntilExpiry` by subtracting `DateTime.Now` from `license.Expiration`. Licence files are issued in UTC, so hosts running outside UTC (most of them) can log “0 days remaining” up to 23 hours early or late depending on local offset. The monitor might therefore claim a licence is already expired while it still has hours left, confusing operators. **Fix**: use `DateTime.UtcNow` (or `DateTimeOffset`) for the subtraction and make sure the result is never negative when the licence is still valid.

5. **Trial reset script never touches the registry hive that the product actually uses**  
   The Windows cleanup script only deletes `HKCU:\Software\RedKestrel\CrlMonitor\.data_<hash>` (`scripts/Clear-CrlMonitor-Trial.ps1:79-97`). However, the shipping product stores trial data under `Registry.LocalMachine` (`RedKestrel.Licensing/Trial/TrialOptions.cs:73-79` via `TrialStorageFactory.CreateDefault`, `TrialStorageFactory.cs:32-36`). As a result, the script reports success yet the HKLM key—and therefore the trial timestamp—remains. Removing the HKLM key also requires administrative rights, which the script never checks for, leading to “access denied” errors on Windows. **Fix**: update the script to detect whether it’s running elevated, warn otherwise, and delete both HKLM and HKCU branches so the trial actually resets.

---

### Next steps
1. Patch `ConfigLoader` and add tests for Unix absolute `file://` handling.
2. Bring `developer-docs/crl-monitor-functional-spec.md` in line with the JSON-only configuration workflow.
3. Clean up or justify the CA1303 suppressions in `Program.cs` and `Reporting/ConsoleReporter.cs`.
4. Switch licence logging to `DateTime.UtcNow` and add a regression test that freezes time.
5. Repair `scripts/Clear-CrlMonitor-Trial.ps1` to operate on HKLM (with elevation checks) so Windows developers can reliably reset trials.
