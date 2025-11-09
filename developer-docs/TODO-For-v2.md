# TODO for v2

## Secure credential storage

- **Encrypt SMTP and LDAP passwords with DPAPI (machine scope).**  
  As soon as the Config App captures credentials, encrypt them with DPAPI using `DataProtectionScope.LocalMachine`. This avoids surprises when the monitoring agent runs under a different account (e.g., LocalSystem via Task Scheduler). Store only the encrypted blobs plus a version marker in `config.json`, and have the agent decrypt at runtime. If decryption fails, log a warning, skip the affected action (reports/alerts), and keep the rest of the run alive. Provide a way to re-encrypt when the config is edited on another machine, since DPAPI machine scope cannot travel between hosts.
