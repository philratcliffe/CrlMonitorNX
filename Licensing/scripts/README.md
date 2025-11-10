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
3. Keep the generated file private—do not commit it.

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
