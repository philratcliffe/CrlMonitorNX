# Phase 1 Design (Async Fetch + Parallelism)

## Goals
- Execute CRL checks asynchronously with controlled parallelism.
- Maintain clear separation between configuration, fetching, parsing, health evaluation, and reporting.
- Keep code structured so later phases (UI, scheduler) can reuse the same services.

## Components

### Configuration & Models
- `RunOptions` (root): immutable record containing
  - Reporting flags/paths: `ConsoleReports`, `CsvReports`, `CsvOutputPath`, `CsvAppendTimestamp`
  - Runtime tuning: `FetchTimeoutSeconds`, `MaxParallelFetches`, `StateFilePath`
  - `IReadOnlyList<CrlConfigEntry>` describing each URI (see below)
- `CrlConfigEntry`: per-URI settings
  - `Uri` (absolute HTTP(S)/LDAP(S))
  - `SignatureValidationMode` (`none` | `ca-cert`)
  - `CaCertificatePath` (required when mode is `ca-cert`)
  - `ExpiryThreshold` (double between 0.1 and 1.0, default 0.8)
  - Optional `LdapCredentials` block (`Username`, `Password`) for LDAP/LDAPS URIs only
- `ConfigLoader`: reads grouped JSON (see `PreCrl-config.json`), enforces ranges, prevents duplicate URIs, and resolves relative paths against the config file’s directory. Default configs keep related keys grouped (reporting flags together, runtime tuning next, then URI entries) to make manual edits easy.
- `CrlCheckRequest` (CrlMonitor namespace): wraps URI + resolved CA cert path, expiry threshold, and LDAP credentials for the runner.
- `CrlCheckResult` (CrlMonitor namespace): status, issuer, timings, diagnostics, csv fields.
- `RunDiagnostics` (CrlMonitor namespace): thread-safe lists for state warnings, signature warnings, config warnings.

### Fetching
- Interface `ICrlFetcher`
  - Method: `Task<FetchedCrl> FetchAsync(CrlCheckRequest request, CancellationToken ct)`
  - `FetchedCrl` holds raw bytes, size, elapsed milliseconds.
- Implementations:
  - `HttpCrlFetcher` (HTTP/HTTPS) using shared `HttpClient`.
  - `LdapCrlFetcher` (LDAP/LDAPS) using `System.DirectoryServices.Protocols`.
- `FetcherResolver`: maps URI scheme to `ICrlFetcher`.

### Parsing & Signature Validation
- `ICrlParser`
  - Method: `ParsedCrl Parse(byte[] data, SignatureValidationContext context)`.
- `ParsedCrl` (existing file) extended with signature status/error.
- `SignatureValidationContext` contains mode and resolved CA cert (if `ca-cert`).
- `CrlParser` (under `Crl/`):
  - Uses BouncyCastle to decode CRL.
  - If mode `none`: mark signature status `Skipped`.
  - If mode `ca-cert`: load provided CA cert, run `crl.Verify(publicKey)`; mark status `Valid` or `Invalid`.

### Health Evaluation
- `CrlHealthEvaluator` (CrlMonitor namespace): method `CrlHealthStatus Evaluate(ParsedCrl parsed, DateTime now)` returning OK/EXPIRING/EXPIRED/ERROR based on next update + thresholds from config.

### State Store
- `IStateStore`
  - `Task<DateTime?> GetLastFetchAsync(Uri uri, CancellationToken ct)`
  - `Task SaveLastFetchAsync(Uri uri, DateTime fetchedAt, CancellationToken ct)`
  - `Task<DateTime?> GetLastReportSentAsync(CancellationToken ct)`
  - `Task SaveLastReportSentAsync(DateTime sentAtUtc, CancellationToken ct)`
  - `Task<DateTime?> GetAlertCooldownAsync(string key, CancellationToken ct)`
  - `Task SaveAlertCooldownAsync(string key, DateTime triggeredAtUtc, CancellationToken ct)`
- `FileStateStore` now persists a JSON object with `last_fetch`, `last_report_sent_utc`, and `alert_cooldowns` sections (and migrates legacy flat dictionaries). Uses async lock to stay thread-safe.

### Runner (Core Orchestrator)
- `CrlCheckRunner`
  - Constructor accepts `ICrlFetcherResolver`, `ICrlParser`, `CrlHealthEvaluator`, `IStateStore`, `ILogger`, `RunOptions`.
  - Method `Task<CrlCheckRun> RunAsync(IEnumerable<CrlCheckRequest> requests, CancellationToken ct)`
  - Uses `SemaphoreSlim(MaxParallelFetches)` to control concurrency.
  - For each request: spawn `Task` that fetches, parses, evaluates, updates state, and pushes result into a concurrent collection.
  - After `Task.WhenAll`, sorts results by URI order and returns `CrlCheckRun` containing results + diagnostics.

### Reporters
- `IReporter`
  - `Task ReportAsync(CrlCheckRun run, CancellationToken ct)`
- `ConsoleReporter`: consumes `IConsole` abstraction so it can be disabled or redirected.
- `CsvReporter`: writes CSV to `CsvOutputPath`, appending timestamp if enabled; CSV formatting lives in `CsvReportFormatter` so other reporters can reuse it.
- `HtmlReporter`: renders the CRL summary + table into a modern HTML page stored at `html_report_path` when `html_report_enabled` is true.
- `EmailReportReporter`: honours `reports` config, produces multipart (plain + HTML) emails, links to the generated HTML report when `html_report_url` is provided, and tracks send timestamps via `IStateStore`.
- `AlertReporter`: inspects each result for selected statuses (e.g., ERROR, EXPIRED, EXPIRING, WARNING), enforces cooldowns per URI/status combination, and dispatches alert emails via the same global SMTP settings.
- `CompositeReporter`: takes config flags, holds reporters, and calls `ReportAsync` sequentially.

### Program Flow (Phase 1)
1. `Program` loads config via `ConfigLoader`.
2. Builds service instances (fetchers, parser, evaluator, state store, reporters).
3. Reads URIs from `UriListLoader` into `CrlCheckRequest`s (resolving CA cert per URI/default).
4. Creates `CrlCheckRunner` and runs `RunAsync` with `CancellationToken`.
5. Passes `CrlCheckRun` to `Reporter` for console/CSV output.
6. Exit code determined by presence of errors/warnings.

### Async & Parallel Safety Notes
- Shared HttpClient injected once; fetchers are stateless.
- Parser uses per-call BouncyCastle instances to avoid shared state.
- `SemaphoreSlim` ensures we never exceed `MaxParallelFetches`.
- Diagnostics collections use `ConcurrentQueue<string>`.
- State store writes use `SemaphoreSlim` or `AsyncLock` to serialize file access.

### Files Summary
``
CrlMonitorNX
├── CrlMonitor.csproj
├── Program.cs
├── ConfigLoader.cs
├── RunOptions.cs
├── UriListLoader.cs
├── Fetcher
│   ├── ICrlFetcher.cs
│   ├── HttpCrlFetcher.cs
│   ├── LdapCrlFetcher.cs
│   └── FetcherResolver.cs
├── Parser
│   ├── ICrlParser.cs
│   ├── CrlParser.cs
│   └── SignatureValidationContext.cs
├── Health
│   └── CrlHealthEvaluator.cs
├── State
│   ├── IStateStore.cs
│   └── FileStateStore.cs
├── Runner
│   ├── CrlCheckRunner.cs
│   └── CrlCheckRun.cs
├── Reporting
│   ├── IReporter.cs
│   ├── ConsoleReporter.cs
│   ├── CsvReporter.cs
│   └── Reporter.cs
├── Diagnostics
│   └── RunDiagnostics.cs
├── Models
│   ├── CrlCheckRequest.cs
│   ├── CrlCheckResult.cs
│   └── Result.cs (existing)
└── CrlMonitor.Tests
    ├── CrlMonitor.Tests.csproj
    └── ConfigLoaderTests.cs
``
├── Program.cs
├── ConfigLoader.cs
├── RunOptions.cs
├── UriListLoader.cs
├── Fetcher
│   ├── ICrlFetcher.cs
│   ├── HttpCrlFetcher.cs
│   ├── LdapCrlFetcher.cs
│   └── FetcherResolver.cs
├── Parser
│   ├── ICrlParser.cs
│   ├── CrlParser.cs
│   └── SignatureValidationContext.cs
├── Health
│   └── CrlHealthEvaluator.cs
├── State
│   ├── IStateStore.cs
│   └── FileStateStore.cs
├── Runner
│   ├── CrlCheckRunner.cs
│   └── CrlCheckRun.cs
├── Reporting
│   ├── IReporter.cs
│   ├── ConsoleReporter.cs
│   ├── CsvReporter.cs
│   └── Reporter.cs
├── Diagnostics
│   └── RunDiagnostics.cs
└── Models
    ├── CrlCheckRequest.cs
    ├── CrlCheckResult.cs
    └── Result.cs (existing)
``

```
CrlMonitor.csproj
Program.cs
ConfigLoader.cs
RunOptions.cs
UriListLoader.cs
Fetcher/
  ICrlFetcher.cs
  HttpCrlFetcher.cs
  LdapCrlFetcher.cs
  FetcherResolver.cs
Parser/
  ICrlParser.cs
  CrlParser.cs
  SignatureValidationContext.cs
Health/
  CrlHealthEvaluator.cs
State/
  IStateStore.cs
  FileStateStore.cs
Runner/
  CrlCheckRunner.cs
  CrlCheckRun.cs
Reporting/
  IReporter.cs
  ConsoleReporter.cs
  CsvReporter.cs
  Reporter.cs
Diagnostics/
  RunDiagnostics.cs
Models/
  CrlCheckRequest.cs
  CrlCheckResult.cs
  Result.cs (existing)
```

## Next Steps
- Minimal slice: finish ConfigLoader/RunOptions/UriListLoader and add `CrlMonitor.Tests` with config validation tests.
- Build fetcher/parser/state components following this layout.
- Build fetcher/parser/state components following this layout.
- Wire runners/reporters and add TDD coverage per component.
