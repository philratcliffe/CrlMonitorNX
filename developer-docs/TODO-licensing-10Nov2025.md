# Licensing Plan (10 Nov 2025)

## Goals
- Integrate RedKestrel.Licensing runtime (license validator + trial manager) into CrlMonitor.
- Establish a repeatable issuance workflow using the shared LicenseGenerator.
- Document release steps so future devs can issue host licences and public trial licences safely.

## Runtime tasks
1. **Public key swap**
   - Generate the CrlMonitor ECDSA key pair via LicenseGenerator (`generate-keys --algorithm ecdsa --output crlmonitor-license-keys.txt`).
   - Store the file securely (outside repo). Copy the public key XML into `Licensing/LicenseBootstrapper`.

2. **Trial configuration**
   - Keep `TrialStorageKey` in source; bump only when we want to grant a new 30-day trial window.
   - Document the current value in this file so release engineers know when it changed last.

3. **Bootstrap UX**
   - Decide whether console output is sufficient or if we need log-file integration.
   - Future: surface licence status to the planned GUI.

## Release workflow (developers/ops)
1. **Per-product config**
   - Create `licensing/licensing.config.json` with non-secret defaults:
     ```json
     {
       "companyName": "Red Kestrel",
       "productName": "CrlMonitor",
       "keysPath": "../secure/crlmonitor-license-keys.txt",
       "defaultExpiryMonths": 12,
       "algorithm": "ecdsa",
       "attributes": {
         "Product": "CrlMonitor"
       }
     }
     ```
   - This file guides the generator but contains no secrets.

2. **Bundled trial licence**
   - Every release: `dotnet run --project ../RedKestrel.Licensing/LicenseGenerator -- generate-license --config licensing/licensing.config.json --type trial --expires 2026-12-31 --output artifacts/license.lic`
   - Copy `license.lic` into the ZIP next to executables.

3. **Host licence issuance**
   - Collect the machine request code (from the bootstrapper output).
   - Command: `dotnet run --project ../RedKestrel.Licensing/LicenseGenerator -- generate-license --config licensing/licensing.config.json --type standard --company "Acme" --request-code H-ABCDE123 --expires 2026-11-30 --output licenses/acme.crlmonitor.lic`
   - Deliver the `.lic` to the customer; instruct them to place it beside `CrlMonitor.exe`.

4. **Trial storage key bumps**
   - Update `Licensing/LicenseBootstrapper.TrialStorageKey` when we want to re-open trials (e.g., major feature release). Note the new value + reasoning in this doc.

## Outstanding questions
- Should we provide a helper script in `tools/` to wrap the `dotnet run` commands?
- Where do we store the secure key file path so build agents can access it (env var vs secrets vault)?
- Do we need telemetry/logging for licence failures?

