# CRL Monitor Functional Specification

## Purpose

Deliver a pluggable certificate revocation list (CRL) monitoring engine that supports multiple transport schemes, flexible signature validation, configurable alerting/reporting, and can power both CLI and UI surfaces.

## In-Scope

- Fetching CRLs over HTTP(S) and LDAP(S).
- Parsing CRLs and extracting issuer, validity, and revocation data.
- Evaluating CRL health (OK/EXPIRING/EXPIRED/ERROR).
- Persisting per-URI state (last successful fetch timestamp).
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
   - User supplies config (`config.json`) and URI list (`uri_list.txt`).
   - Console shows progress banner → summary table → optional warnings.
   - CSV artifact produced when enabled.
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
- Email reports: scheduled summaries (daily/weekly) with optional CSV attachment via SMTP.
- Email alerts: status-based notifications (ERROR/EXPIRED/EXPIRING/WARNING/OK, typically ERROR/EXPIRED) with cooldowns.

### Non-Functional

- **Resilience:** No single failure (fetch, parse, write) should crash the run; result row captures error.
- **Extensibility:** Support additional transports, reporters, validation strategies.
- **Testability:** Core pipeline components individually testable with mocks/fakes.
- **Observability:** Log structured events for warning conditions and signature validation decisions.
- **Security:** Avoid blocking operations on untrusted input (size limit, timeouts). Prevent environment-dependent deadlocks.

### Configuration Inputs

- Standard config fields (SMTP, reporting flags, timeouts, caches).
- Global `smtp` block (host/port/username/from/starttls) used by all email reporters.
- `html_report_enabled` plus `html_report_path`/`html_report_url` to emit a shareable HTML report and include links in emails.
- `reports` block (enabled/frequency/recipients) controlling scheduled summaries.
- `alerts` block (enabled/statuses/cooldown/recipients) controlling which statuses trigger notifications.
- `max_crl_size_bytes` cap applied globally (default 10 MB) with optional per-URI `max_crl_size_bytes` overrides to prevent oversized payloads.
- `signature_validation_level` with default `full-chain`.
- `alert_on_signature_failure` boolean to trigger diagnostics.
- `smtp.password` field optional when `SMTP_PASSWORD` environment variable is present; loader falls back to the env var to avoid storing secrets in config.

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
