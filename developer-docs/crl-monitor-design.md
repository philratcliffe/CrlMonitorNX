# CRL Monitor Design Document

## Overview

This design describes a modular CRL monitoring engine implemented as a set of composable services. The architecture emphasises testability, async friendliness, and clean separation between the monitoring pipeline and presentation (CLI/UI).

## Architecture Diagram (Conceptual)

```
          ┌──────────────┐
          │  Scheduler   │  (CLI / Service / UI)
          └──────┬───────┘
                 │ invokes
         ┌───────▼────────┐
         │ CrlCheckRunner │
         └───────┬────────┘
                 │ uses
 ┌───────────────┼──────────────────────────────────────────────┐
 │               │                         │                    │
 │       ┌───────▼──────┐      ┌───────────▼────────┐  ┌────────▼────────┐
 │       │ IFetcher     │      │ ICrlParser         │  │ ICrlHealthEval │
 │       │ (HTTP/LDAP)  │      │ (Signature levels) │  │ (OK/EXPIRING/…)│
 │       └───────┬──────┘      └───────────┬────────┘  └────────┬────────┘
 │               │                         │                    │
 │        ┌──────▼───────┐        ┌────────▼────────┐   ┌────────▼────────┐
 │        │ IStateStore  │        │ Diagnostics      │   │ Reporters       │
 │        │ (async)      │        │ Aggregator       │   │ (Console/CSV/…) │
 │        └──────────────┘        └──────────────────┘   └────────────────┘
 └─────────────────────────────────────────────────────────────────────────┘
```

## Key Components

### Hosting & Scheduling

- **Windows Task Scheduler** bootstraps the process once at startup (and optionally restarts it on failure). The scheduled task is registered by an install script and set to “Run whether user is logged on or not” so the monitor survives logoff.
- **Registration script** (`scripts/register-task.ps1`) sets up the scheduled task by:
  - Prompting for the user account/password (or accepting parameters) that should run the monitor.
  - Copying/ensuring the executable path and working directory.
  - Creating a task via `Register-ScheduledTask` with triggers (AtStartup) and settings (restart on failure, run whether user logged on or not).
  - Writing an event/log entry on success.
- **Unregister script** (`scripts/unregister-task.ps1`) deletes the scheduled task and offers to stop any running instance.
- **In-process Web API/UI:** The monitor hosts a lightweight Kestrel server exposing an HTTP API (`/status`, `/results`, `/config`, `/run-now`) and serving a small web UI for configuring schedules and viewing results. By default the server binds to `http://localhost:<port>` so only the local machine can access it.
- **Authentication:**
  - Localhost binding bypasses auth for convenience.
  - When `listen_addresses` includes non-local addresses, the API requires HTTPS and an API key defined in config (e.g., `api_key`). Clients send `Authorization: Bearer <key>`.
  - Optionally, Windows Authentication can be enabled for domain environments (`use_windows_auth = true`).
  - Advanced deployments can enable client certificates in the future.
- **Quartz.NET** runs inside the process and drives the CRL check cadence per `config.json` (e.g., every 90 minutes). Quartz jobs are the only mechanism that kicks off monitoring runs; the scheduled task simply ensures the host is running.
- **Singleton Guard:** On startup, the process acquires a named mutex (or PID file) to ensure only one instance runs at a time. If Task Scheduler accidentally launches a second copy, the mutex acquisition fails and the new process exits quietly after logging a warning.

### CrlCheckRunner

Orchestrates the monitoring pipeline for a collection of URIs.

- Accepts a list of `CrlCheckRequest` and `RunOptions`.
- For each request:
  1. Resolve transport via `IFetcherStrategy`.
  2. Await fetch (`FetchAsync`) to obtain bytes + timing.
  3. Parse bytes via `ICrlParser` (returns `ParsedCrl` with signature status/errors).
  4. Evaluate health via `ICrlHealthEvaluator`.
  5. Record state via `IStateStore` (non-blocking failure handling).
  6. Compose `CrlCheckResult`.
- Aggregates `RunDiagnostics` (state failures, signature warnings, config warnings).
- Returns `CrlCheckRun` (results + diagnostics + total duration).

### IFetcher Strategy

```
public interface ICrlFetcher
{
    Task<FetchedCrl> FetchAsync(Uri uri, CancellationToken ct);
}

public sealed class HttpCrlFetcher : ICrlFetcher { ... }
public sealed class LdapCrlFetcher : ICrlFetcher { ... }
```

- Registered in DI keyed by scheme (`http`, `https`, `ldap`, `ldaps`).
- Shared timeout and max-size logic lives in base type/helper.

### ICrlParser

```
public interface ICrlParser
{
    Task<ParsedCrl> ParseAsync(byte[] data, SignatureValidationLevel level, CancellationToken ct);
}

public sealed class BouncyCastleCrlParser : ICrlParser { ... }
```

- Responsible for ASN.1 decoding and signature validation.
- Uses `ISignatureValidator` (see below) to reduce complexity.
- Returns `ParsedCrl` containing issuer, next update, revocation list, `SignatureStatus` (enum: `Valid`, `Invalid`, `Skipped`), and optional error message.

### Signature Validation Service

```
public enum SignatureValidationMode
{
    None,
    CaCertificate
}

public sealed record SignatureValidationContext(
    SignatureValidationMode Mode,
    X509Certificate2? DefaultCaCertificate,
    IReadOnlyDictionary<Uri, X509Certificate2> PerUriCertificates);

public interface ISignatureValidator
{
    SignatureValidationResult Validate(X509Crl crl, SignatureValidationContext context, Uri uri);
}
```

- Initial implementation supports two modes:
  - `None`: skip validation, return `Skipped` status and explanatory message.
  - `CaCertificate`: locate the issuer certificate via per-URI mapping or default, verify the CRL signature directly against that public key.
- Future modes (full chain, revocation) can extend the enum and implementation.
- If a required CA certificate is missing, the validator records a configuration warning and returns `Skipped` or `Invalid` per policy.

### State Store

```
public interface IStateStore
{
    Task<DateTime?> GetLastFetchAsync(Uri uri, CancellationToken ct);
    Task SaveLastFetchAsync(Uri uri, DateTime fetchedAt, CancellationToken ct);
}

public sealed class FileStateStore : IStateStore { ... }
```

- File-backed implementation with resilient IO (creates directories, swallows failures into diagnostics).
- Future DB-backed versions can slot in without touching pipeline.

### Diagnostics Aggregator

`RunDiagnostics` keeps separate collections for `StateWarnings`, `SignatureWarnings`, `ConfigurationWarnings`, and `RuntimeWarnings`.

The runner appends to these collections; reporters/loggers read from them.

### Reporters

`IReporter` interface:

```
public interface IReporter
{
    Task ReportAsync(CrlCheckRun run, CancellationToken ct);
}

public sealed class ConsoleReporter : IReporter { ... }
public sealed class CsvReporter : IReporter { ... }
public sealed class HtmlReporter : IReporter { ... }
public sealed class CompositeReporter : IReporter { ... }
```

- Reporters receive structured data; they do not perform business logic, and HTML/email/alert reporters link to the generated HTML file when configured.
- Console reporter depends on `IConsole` abstraction to avoid direct `System.Console` usage.
- CSV reporter reuses existing schema; uses `ICsvWriter` abstraction for testability.

## Async & Parallel Considerations

- Entire pipeline is async; `Main` becomes `static async Task<int>`.
- Use `ConfigureAwait(false)` inside lower-level services.
- Supports sequential runs by default; optional parallelism via `Parallel.ForEachAsync` (or `Task.WhenAll`) guarded by a configuration knob such as `max_parallel_fetches` (default 1).
- Each URI is processed end-to-end (fetch → parse → validate → evaluate → state update) within a single async task; results/diagnostics are buffered and reported once tasks complete to keep console/CSV output deterministic.
- Shared services (loggers, reporters) remain single-threaded entry points; worker tasks push results/diagnostics into thread-safe collections that are drained after the parallel phase.

### Parallelism + BouncyCastle Safety

- BouncyCastle parser/validator types are instantiated per task (e.g., `new X509CrlParser()` inside the worker) to avoid shared mutable state.
- CA certificates provided for the `ca-cert` mode are loaded once into `X509Certificate2` instances and treated as read-only; they can be reused safely across tasks.
- No global BouncyCastle state is shared; secure random instances or other primitives are created on demand.
- Other dependencies:
  - `HttpClient` remains a singleton (thread-safe) injected into fetchers.
  - `Serilog` is thread-safe; log entries may interleave but stay consistent.
  - `CsvHelper` is only used after the parallel phase (single thread), so no additional guards are required.
- This strategy ensures we can scale out to multiple concurrent CRL checks without racing inside third-party libraries.

## Configuration

`RunOptions` derived from JSON config:

```
public record RunOptions(
    SignatureValidationLevel SignatureValidationLevel,
    bool AlertOnSignatureFailure,
    TimeSpan FetchTimeout,
    long MaxCrlSizeBytes,
    bool EnableCsv,
    bool EnableConsole,
    IReadOnlyList<string> ConfigWarnings
);
```

Future fields may include concurrency limits, custom trust anchors, etc.

## Logging Strategy

- Use `ILogger` abstraction (Serilog implementation) injected into services.
- Logging levels:
  - `Information` only for high-level events (run start/end, successful signature validation in full-chain mode).
  - `Warning` for diagnostics that should bubble up (state save failures, signature invalid, unsupported config).
  - `Debug` for store probing and detailed chain insights.
- Diagnostics aggregator mirrors log warnings so they reach reporters.

## Error Handling

- Each pipeline stage catches its own exceptions and converts them into `Result<T>` patterns.
- The runner never throws on per-URI failure; it records a `Failed` result.
- Only catastrophic failures (config load, dependency initialisation) bubble to the top-level `Main` handler.

## Testing Strategy

- **Unit Tests:**
  - Mock `ICrlFetcher`, `ICrlParser`, `ICrlHealthEvaluator`, `IStateStore`, `IConsole`, `ILogger`.
  - Validate that runner handles success/failure paths and aggregates diagnostics.
- **Integration Tests:**
  - Use in-memory HTTP server to serve CRLs.
  - Provide fixture CRLs for signature validation.
  - Test reporters end-to-end with captured console/CSV output.
- **Performance Tests (Future):**
  - Measure throughput with large URI lists and evaluate async benefits.

## Incremental Adoption Plan

1. Extract current fetch/parse/state logic into interfaces without changing behaviour.
2. Introduce async signatures while keeping synchronous wrappers for CLI entry point.
3. Swap to async `Main` and ensure tests updated.
4. Add diagnostics aggregator and reporters once pipeline stable.

## Risks & Mitigations

- **Complexity creep:** Mitigate with small, well-defined services and unit tests per component.
- **Certificate store variability:** Provide extensive logging and allow overrides for custom trust anchors in future iteration.
- **Async IO availability:** Some operations (CSV writing, state file) remain synchronous; document and, if needed, offload to background tasks or `Task.Run` to avoid blocking the runner.
- **Console capabilities:** Use `IConsole` abstraction to avoid repeating recent cursor issues.

## Future Extensions

- REST API host exposing the runner via HTTP endpoints.
- Real-time UI with SignalR/Streaming to display CRL checks as they complete.
- Database-backed history for trend analysis and SLA reporting.
- Pluggable alert channels (email, Slack, Teams) leveraging the same diagnostics data.
