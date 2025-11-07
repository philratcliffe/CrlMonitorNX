# CRL Monitor Functional Specification

## Purpose

Deliver a pluggable certificate revocation list (CRL) monitoring engine that supports multiple transport schemes, flexible signature validation, configurable alerting/reporting, and can power both CLI and UI surfaces.

## In-Scope

- Fetching CRLs over HTTP(S) and LDAP(S).
- Parsing CRLs and extracting issuer, validity, and revocation data.
- Evaluating CRL health (OK/STALE/EXPIRED/ERROR).
- Persisting per-URI state (last successful fetch timestamp).
- Generating console, CSV, and OPTIONAL email reports.
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
- Aggregate warnings (state, signature, config) and surface via logging and reports.
- Provide reporters:
  - Console: table output, safe when output redirected/non-interactive.
  - CSV: deterministic schema matching existing format.
  - (Future) Email: placeholder warnings when enabled but unimplemented.

### Non-Functional

- **Resilience:** No single failure (fetch, parse, write) should crash the run; result row captures error.
- **Extensibility:** Support additional transports, reporters, validation strategies.
- **Testability:** Core pipeline components individually testable with mocks/fakes.
- **Observability:** Log structured events for warning conditions and signature validation decisions.
- **Security:** Avoid blocking operations on untrusted input (size limit, timeouts). Prevent environment-dependent deadlocks.

### Configuration Inputs

- Standard config fields (SMTP, reporting flags, timeouts, caches).
- `signature_validation_level` with default `full-chain`.
- `alert_on_signature_failure` boolean to trigger diagnostics.

### Outputs

- Console stdout summary.
- CSV file when enabled.
- Exit code 0/1.
- Structured logs (Serilog) containing warnings and timing.

## Acceptance Criteria

- Running with default config fetches all URIs, produces console table, and optionally CSV.
- Invalid URI yields “Failed” result without aborting run.
- CRL exceeding max size yields failure result and warning.
- Signature validation mode `none` marks results as "Skipped" without warnings.
- Signature validation mode `ca-cert` verifies CRLs against the supplied certificate path.
- State file write errors logged and reported without crashing.
- Console reporting works when output redirected (no cursor exceptions).

## Future Enhancements

- UI front end consuming the same pipeline via service APIs.
- Persistent datastore for historical trend analysis.
- Trust-anchor management interface.
- More granular health categories (e.g., signature failures vs fetch failures).
