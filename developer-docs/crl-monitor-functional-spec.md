# CRL Monitor Functional Specification

## Purpose

Deliver a pluggable certificate revocation list (CRL) monitoring engine that supports multiple transport schemes, flexible signature validation, configurable alerting/reporting, and can power both CLI and UI surfaces.

## In-Scope

- Fetching CRLs over HTTP(S) and LDAP(S).
- Parsing CRLs and extracting issuer, validity, and revocation data.
- Evaluating CRL health (OK/EXPIRING/EXPIRED/ERROR).
- Persisting CRL monitor state (per-URI last fetch timestamps, per-alert cooldowns, and last report send time) in a resilient file format.
- Generating console, CSV, and optional email reports plus targeted alert e-mails.
- Capturing diagnostics (state persistence failures, signature validation warnings, configuration warnings).
- Configurable signature validation modes (initial release):
  - `none` — skip signature checks and mark status as "Skipped".
  - `ca-cert` — verify the CRL signature against a CA certificate provided per URI (with optional default).
- Future modes (e.g., full-chain validation, revocation-aware) can be added once requirements stabilise.

## Out-of-Scope (Initial Release)

- UI portals (web/desktop) — pipeline must enable later integration.
- Certificate trust-anchor configuration UI (future extension).
- Parallel/distributed execution (initial run is single-process, sequential or lightly parallelised).
- Persistent database storage beyond simple state file.

## User Flows

1. **CLI Run**
   - User supplies single config file (`config.json`) containing CRL URIs and all settings.
   - Console shows banner with license info → CRL status table → summary → optional error details.
   - CSV and HTML reports produced when enabled.
   - Exit code signals overall health (0 = healthy, 1 = any failure/warnings).

2. **Scheduled Service (Future)**
   - Pipeline invoked on schedule, results pushed to downstream systems (logs, UI, alerts).

## Requirements

### Functional

- Validate configuration and return actionable errors for missing/invalid fields.
- Resolve passwords from environment variables when configured.
- For each URI:
  - Fetch CRL using correct transport.
  - Enforce max size limits.
  - Parse CRL and capture signature validity according to configured level.
  - Determine health status and produce result row.
  - Record fetch duration (ms).
  - Update state file on success, tolerant of IO failures.
- Aggregate warnings (state, signature, config) and surface via logging, email reports, and alerts.
- Provide reporters:
  - Console: table output, safe when output redirected/non-interactive.
  - CSV: deterministic schema matching existing format.
-   - HTML: static summary file plus optional public URL when `html_report_enabled` is true.
- Email reports: scheduled summaries delivered via SMTP when `reports.enabled` is true, optionally throttled by `reports.report_frequency_hours` (1–8760 hours; omit to send every run) and with CSV attachments when configured.
- Email alerts: status-based notifications (ERROR/EXPIRED/EXPIRING/WARNING/OK, typically ERROR/EXPIRED) with cooldowns.

### Non-Functional

- **Resilience:** No single failure (fetch, parse, write) should crash the run; result row captures error.
- **Extensibility:** Support additional transports, reporters, validation strategies.
- **Testability:** Core pipeline components individually testable with mocks/fakes.
- **Observability:** Log structured events for warning conditions and signature validation decisions.
- **Security:** Avoid blocking operations on untrusted input (size limit, timeouts). Prevent environment-dependent deadlocks.

### Configuration Inputs

- Core runtime controls: `console_reports`, `csv_reports`, `csv_output_path`, `csv_append_timestamp`, `state_file_path`, `fetch_timeout_seconds` (1–600 seconds), `max_parallel_fetches` (1–64), and `max_crl_size_bytes` (default 10 MB, overridable per URI) govern how the monitor executes and stores results.
- HTML reporting: `html_report_enabled` with paired `html_report_path` (required when enabled) and optional `html_report_url` expose the generated summary file locally and/or via a published URL.
- Global `smtp` block (host/port/username/password/from/enable_starttls) is required whenever reports or alerts are enabled. The password can be omitted if `SMTP_PASSWORD` is set in the environment; the loader falls back automatically.
- `reports` block controls scheduled email summaries: `enabled`, `recipients`, optional custom `subject`, `include_summary`, `include_full_csv`, and optional `report_frequency_hours`. The frequency guard must be greater than 0 and at most 8760 hours (one year); omitting it sends a report on every execution.
- `alerts` block governs targeted notifications: `enabled`, `recipients`, `statuses` (subset of OK/WARNING/EXPIRING/EXPIRED/ERROR), `cooldown_hours` (0–168), `subject_prefix`, and `include_details`. Alerts also inherit the global SMTP settings.
- `uris` collection defines every CRL to fetch. Each entry supplies a `uri`, `signature_validation_mode` (`none` or `ca-cert`), optional `ca_certificate_path` (mandatory for `ca-cert`), `expiry_threshold` (0–1), optional per-entry `max_crl_size_bytes`, and optional `ldap` credentials (username/password) for LDAP targets.
- CA certificates above 200 KB are treated as invalid inputs; the signature validator skips them with a warning to prevent processing corrupted anchors.

### Outputs

- Console stdout summary.
- CSV file when enabled.
- HTML report written to configurable path and linked from emails.
- Scheduled email reports (when configured) with optional CSV attachment.
- Alert emails when selected statuses trigger and cooldown permits.
- Exit code 0/1.
- Structured logs (Serilog) containing warnings and timing.

## Acceptance Criteria

- Running with default config fetches all URIs, produces console table, and optionally CSV.
- Invalid URI yields “Failed” result without aborting run.
- CRL exceeding max size yields a WARNING result with “Skipped: CRL exceeded <limit>” status detail and runtime diagnostics.
- Signature validation mode `none` marks results as "Skipped" without warnings.
- Signature validation mode `ca-cert` verifies CRLs against the supplied certificate path.
- State file write errors logged and reported without crashing.
- Console reporting works when output redirected (no cursor exceptions).

## Future Enhancements

- UI front end consuming the same pipeline via service APIs.
- Persistent datastore for historical trend analysis.
- Trust-anchor management interface.
- More granular health categories (e.g., signature failures vs fetch failures).
