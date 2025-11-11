## Licensing Scripts

Utilities that help CrlMonitor engineers work with the shared `LicenseGenerator` CLI.

### Key Generation

`generate-license-keys.sh` wraps the `LicenseGenerator` project in `../RedKestrel.Licensing`. It writes a key pair to `~/.CrlMonitorLicenseKeys/crl-monitor-license-keys.txt` (creating the directory if needed).

1. Ensure the `RedKestrel.Licensing` repository is checked out as a sibling of this repo.
2. Set the passphrase environment variable once per shell session:
   ```bash
   export LICENSE_PASSPHRASE='choose-a-strong-secret'
   ./Licensing/scripts/generate-license-keys.sh
   ```
   If `LICENSE_PASSPHRASE` is not set the script will prompt for it and show the export example above.
3. The script refuses to overwrite an existing `crl-monitor-license-keys.txt`; move/backup the old file if you intend to rotate keys.
4. Keep the generated file private—do not commit it.

### Installing the Public Key

After generating a key pair, run `install-public-key.sh` to copy the public key into `Licensing/LicenseBootstrapper.cs`:

```bash
./Licensing/scripts/install-public-key.sh
```

Pass a custom key-file path as the first argument if you stored it elsewhere. The script replaces the `PublicKey` constant so the app validates licenses issued with the new key.

### Sample Generator Configuration

`licensegenerator.config.json` holds the defaults CrlMonitor uses when calling `generate-license`. Copy and customize it before running:

```bash
cp Licensing/scripts/licensegenerator.config.json ~/crlmonitor.licensegenerator.json
dotnet run --project ../RedKestrel.Licensing/LicenseGenerator -- \
  generate-license --config ~/crlmonitor.licensegenerator.json --request-code <code> ...
```

Update the config to point at the key file created by the script (`"keysPath": "~/.CrlMonitorLicenseKeys/crl-monitor-license-keys.txt"`).

### Generating the Bundled Trial Licence

Use `generate-trial-license.sh` to create the 12-month trial `license.lic` that ships with every release:

```bash
export LICENSE_PASSPHRASE='choose-a-strong-secret'   # or enter it interactively
./Licensing/scripts/generate-trial-license.sh
```

By default the script reads `licensegenerator.config.json`, computes the expiry date (default 12 months ahead) and sets the licence to expire at 23:59:59 UTC on that day. The generated file is written to `Licensing/generated_licenses/trial/<yyyy-mm-dd>-license.lic`, where the folder is ignored by git so you can regenerate safely. Pass a different config or output path if needed:

```bash
./Licensing/scripts/generate-trial-license.sh ~/crlmonitor.config.json ./artifacts/CrlMonitor/license.lic
```

Before publishing a release, copy the freshly generated trial licence from `Licensing/generated_licenses/trial/` into the shipping ZIP alongside the binaries.

The bundled trial licence is stamped with:
- Customer name: `Trial User`
- Customer email: `support@redkestrel.co.uk`

Update the config if those defaults ever change.
