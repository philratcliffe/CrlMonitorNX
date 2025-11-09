# TODO for v2

## Secure credential storage

- **Encrypt SMTP and LDAP passwords with DPAPI (machine scope).**  
  As soon as the Config App captures credentials, encrypt them with DPAPI using `DataProtectionScope.LocalMachine`. This avoids surprises when the monitoring agent runs under a different account (e.g., LocalSystem via Task Scheduler). Store only the encrypted blobs plus a version marker in `config.json`, and have the agent decrypt at runtime. If decryption fails, log a warning, skip the affected action (reports/alerts), and keep the rest of the run alive. Provide a way to re-encrypt when the config is edited on another machine, since DPAPI machine scope cannot travel between hosts.

## Configuration loader refactor

- **Split ConfigLoader into focussed components.**  
  Today `ConfigLoader` handles parsing, validation, path resolution, LDAP credential checks, URI scheme enforcement, etc., in ~600 lines. For v2, keep a thin orchestrator and push per-area validation (SMTP, alerts, reports, URIs/paths) into separate helpers so each concern is unit-testable and easier to evolve.
