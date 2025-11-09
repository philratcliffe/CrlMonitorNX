## CrlMonitorAgent + Config UI Plan

### Overview
- Rename existing console runner to `CrlMonitorAgent.exe`; it keeps the monitoring loop headless.
- Build a new Windows desktop GUI (`CrlMonitor.exe`) dedicated to editing config, monitoring status, and orchestrating the agent.
- Use Task Scheduler only to keep the agent alive at logon/startup; use Quartz (or similar) inside the agent to honour user-defined run intervals without extra privileges.

### Agent responsibilities
- Host the monitoring pipeline (current behaviour) plus a lightweight localhost-only HTTP API for IPC (ASP.NET Core minimal API on configurable port; bind `127.0.0.1` by default).
- Read `config.json` and new `run_frequency` field (cron string or interval minutes). Validate strictly; reject invalid values before scheduling.
- Embed Quartz Scheduler:
  - `IJob` runs the CRL sweep.
  - Re-schedule when config changes.
  - Optionally persist schedule state (RAM-only is acceptable if each run reloads config on startup).
- Watch `config.json` for atomic updates (Write temp + move pattern) and reload safely. On reload, cancel current Quartz triggers then apply new ones.
- Expose API endpoints (authenticated by shared secret/token file) for:
  - `GET /status` – return current schedule, last/next run, health.
  - `POST /run-now` – trigger immediate job.
  - `POST /reload-config` – force reload after GUI saves.

### GUI responsibilities
- Load/validate `config.json` using shared library (extract config models + validation logic to shared assembly referenced by both agent and GUI).
- Provide UI to edit URIs, validation settings, sensitive paths, plus new scheduling field (e.g., dropdown for “every N minutes” or advanced cron entry).
- On save:
  - Write to temp file, then replace `config.json`.
  - Call agent’s `/reload-config`.
- Show live status (last run, next run, errors) by polling `GET /status`.
- Offer “Run now” button hitting `/run-now`.
- On startup, check Task Scheduler for `\CrlMonitor\Agent` task:
  - If missing, prompt user to create.
  - Creation uses user-level Task Scheduler (no admin). Trigger at logon or system startup, action runs `CrlMonitorAgent.exe`, settings “If already running, do nothing”.
  - Optionally allow storing credentials to run while user logged off; document implications.

### Task Scheduler integration
- Keep a single scheduled task per user/workstation created via `Microsoft.Win32.TaskScheduler` library.
- Configure:
  - Trigger: At logon (default) and optionally At startup.
  - Action: Launch `CrlMonitorAgent.exe`.
  - Settings: “Run task as soon as possible after missed start”, “Restart every X minutes if fails”, “Do not start new instance” to avoid duplicates.
- GUI routine:
  1. Query scheduler for task existence.
  2. If disabled, offer enable.
  3. If missing, guide through creation (path to agent, working dir, triggers).

### Security considerations
- HTTP listener bound to loopback only; port configurable via config/UI.
- Protect API with random token stored outside repo (e.g., `%ProgramData%\CrlMonitor\agent.token`). GUI reads same token; regenerate via UI if needed.
- Never expose secrets (LDAP credentials, CA paths) via API/logs; mask in GUI except when editing.
- Validate all user inputs in GUI before save; show friendly errors referencing policy (`AnalysisMode` etc.).

### Migration steps
1. Rename project/output to `CrlMonitorAgent` (update csproj, docs, integration tests).
2. Extract config/validation to shared library referenced by agent + GUI.
3. Add new `run_frequency` field to config + schema docs; wire Quartz scheduler.
4. Implement agent’s localhost API + config reload.
5. Scaffold GUI (WPF/WinUI) using shared library; build forms for config editing.
6. Integrate Task Scheduler detection/creation flow.
7. Update documentation + installers referencing new binaries.

### Open questions
- Exact syntax + validation rules for `run_frequency`? (interval vs cron)
- Need agent to run while user logged off? If yes, Task Scheduler must store creds or solution must become a Windows Service.
- Preferred GUI tech (WPF, WinUI, MAUI, Avalonia)? Choose based on deployment targets.
