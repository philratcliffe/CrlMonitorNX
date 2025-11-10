# Developer Quick Notes

## .NET SDK Selection on macOS

Homebrew installs .NET 8 side-by-side with newer bundles, but the default `dotnet` shim may point at `/opt/homebrew/Cellar/dotnet/9.*`. When you see missing-runtime errors, force the CLI to use the 8.0 toolset:

```bash
export DOTNET_ROOT=/opt/homebrew/opt/dotnet@8
export PATH="$DOTNET_ROOT/bin:$PATH"
```

After setting those variables, `dotnet test` picks up `Microsoft.NETCore.App 8.0.x` without requiring roll-forward flags. Verify with `dotnet --list-runtimes`.

## .NET SDK Selection on Windows

Prefer installing .NET 8 via the official installer. If multiple SDKs are present, run `where dotnet` and ensure the first entry is under `C:\Program Files\dotnet`. When using Visual Studio, set the solution SDK version with a `global.json` if needed:

```json
{
  "sdk": { "version": "8.0.0", "rollForward": "latestMinor" }
}
```

PowerShell prompt check:
```powershell
dotnet --list-runtimes
```
Confirms that `Microsoft.NETCore.App 8.0.x` is available system-wide.

## .NET SDK Selection on Linux

Use the Microsoft package feeds (e.g., `apt install dotnet-sdk-8.0`) and confirm `/usr/share/dotnet` is ahead of any custom installs in `PATH`. If multiple versions co-exist, export `DOTNET_ROOT=/usr/share/dotnet` inside your shell profile and re-run `dotnet --list-runtimes` to ensure 8.0 is active.

## Integration Tests

Optional end-to-end checks live under `integration_tests/` and are not wired into `dotnet test`:

- `python integration_tests/test_csv_report.py` – runs the CLI against a sample CRL and validates the CSV output.
- `python integration_tests/test_smtp_report.py` – spins up a local SMTP server, triggers an email report, and inspects the captured MIME payload.

Both scripts accept `-keep_test_output` to retain generated configs/artifacts for debugging.

## Windows Release Packaging

Use `./publish-windows.sh` to build the production bundle for Windows x64. The helper script:

- Ensures the git worktree is clean and performs `dotnet list package --vulnerable`.
- Runs a self-contained `dotnet publish` (single file, no debug symbols).
- Copies `config.json` into the publish directory so administrators have a ready-made template.
- Zips the output as `CrlMonitorNX-v<version>-Windows.zip`, then validates the archive (integrity, required files, absence of `.pdb`, and basic size sanity).

The ZIP is emitted next to the script and is ready for distribution/testing on Windows hosts.
