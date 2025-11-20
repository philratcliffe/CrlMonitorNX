# CrlMonitor User Guide

## Quick Start

1. Extract the release ZIP to `C:\CrlMonitor\`
2. Run `CrlMonitor.exe` once to accept the EULA (or use `--accept-eula` for automated deployments)
3. Edit `config.json`:
   - Add your CRL URIs to the `uris` section
   - Set `html_report_path` and `csv_output_path` to desired locations
4. Run: `CrlMonitor.exe config.json`
5. Verify the generated HTML/CSV reports at the configured paths

You now have a working CRL monitoring tool.

**Note for automated deployments:** Use `CrlMonitor.exe --accept-eula config.json` to bypass interactive EULA acceptance.

### Next Steps (Optional)

* **Email Reports** – Enable `reports.enabled` and configure SMTP settings for scheduled email reports
* **Email Alerts** – Enable `alerts.enabled` and configure SMTP for status-based alerts
* **Scheduled Task** – Create a Windows Scheduled Task to run automatically (see Section 5)

---

## 1. Introduction

CrlMonitor checks Certificate Revocation Lists (CRLs), validates signatures, monitors expiry windows, and produces HTML/CSV reports and optional email alerts. It is designed to run unattended and is typically scheduled via Windows Task Scheduler.

## 2. Installation

Extract the release ZIP to a folder such as `C:\CrlMonitor\`. Ensure the account used to run the scheduled task (often LocalSystem) has read/write access to the folder and to any report/output locations.

## 3. Configuration File

The application reads a JSON configuration file defining CRLs, email settings, report preferences, logging, and license behaviour. The main sections include:

* **uris** – list of CRLs to check (`http`, `https`, `ldap`, `ldaps`, `file`)
* **smtp** – email server settings
* **reports** – report email schedule and recipients
* **alerts** – alert recipients, statuses to watch, cooldown hours
* **logging** – log paths and rolling retention
* **html_report_path / csv_output_path** – report file locations
* **use_system_proxy** – enable system proxy for HTTP fetches (default: true)

**Environment Variables (Windows only):** Path strings support environment variable expansion using `%VARIABLE%` syntax (e.g., `%ProgramData%\RedKestrel\CrlMonitor\report.csv`). Common variables include `%ProgramData%`, `%TEMP%`, `%USERPROFILE%`, and `%APPDATA%`.

### Configuration Fields

#### Top-Level Settings

* `console_reports` (bool) – Enable console output (default: true)
* `console_verbose` (bool) – Show detailed result notes and diagnostics vs simplified error summary (default: false)
* `csv_reports` (bool) – Enable CSV report generation (default: true)
* `csv_output_path` (string, required) – Path to CSV report file
* `csv_append_timestamp` (bool) – Append timestamp to CSV filename (default: false)
* `html_report_enabled` (bool) – Enable HTML report generation (default: false)
* `html_report_path` (string) – Path to HTML report file (required if html_report_enabled is true)
* `html_report_url` (string, optional) – URL where HTML report will be hosted (used in emails)
* `fetch_timeout_seconds` (int, required) – Timeout for CRL fetch operations (1-600)
* `max_parallel_fetches` (int, required) – Maximum concurrent fetches (1-64)
* `max_crl_size_bytes` (int) – Global maximum CRL size in bytes (default: 10485760 = 10MB)
* `use_system_proxy` (bool) – Use system proxy with integrated Windows auth (default: true)
* `state_file_path` (string, required) – Path to state file for tracking alert history. The application creates this file automatically; the parent directory must exist. Default: `%ProgramData%/RedKestrel/CrlMonitor/state.json`. Leave at default unless you have specific requirements.

#### Logging Section

```json
"logging": {
  "min_level": "Information",
  "log_file_path": "CrlMonitor.log",
  "rolling_interval": "Day",
  "retained_file_count_limit": 7
}
```

* `min_level` – Log level: Verbose, Debug, Information, Warning, Error, Fatal (default: Information)
* `log_file_path` – Relative or absolute path to log file
* `rolling_interval` – Infinite, Year, Month, Day, Hour, Minute (default: Day)
* `retained_file_count_limit` – Number of old log files to keep

#### SMTP Section

```json
"smtp": {
  "host": "smtp.example.com",
  "port": 587,
  "username": "user@example.com",
  "password": "",
  "from": "CRL Monitor <monitor@example.com>",
  "enable_starttls": true
}
```

* `host` (string, required) – SMTP server hostname
* `port` (int, required) – SMTP port (1-65535)
* `username` (string, required) – SMTP authentication username
* `password` (string) – SMTP password (can use SMTP_PASSWORD env variable)
* `from` (string, required) – From email address
* `enable_starttls` (bool) – Enable STARTTLS (default: true)

#### Reports Section

```json
"reports": {
  "enabled": false,
  "report_frequency_hours": 24,
  "recipients": ["admin@example.com"],
  "subject": "CRL Health Report",
  "include_summary": true,
  "include_full_csv": true
}
```

* `enabled` (bool) – Enable scheduled email reports
* `report_frequency_hours` (int) – Hours between reports
* `recipients` (array) – Email recipient list
* `subject` (string) – Email subject line
* `include_summary` (bool) – Include summary statistics in email
* `include_full_csv` (bool) – Attach full CSV report

#### Alerts Section

```json
"alerts": {
  "enabled": false,
  "recipients": ["alert@example.com"],
  "statuses": ["ERROR", "EXPIRED", "EXPIRING"],
  "cooldown_hours": 24,
  "subject_prefix": "[CRL Alert]",
  "include_details": true
}
```

* `enabled` (bool) – Enable status-based alerts
* `recipients` (array) – Email recipient list
* `statuses` (array) – Statuses to alert on: OK, WARNING, EXPIRING, EXPIRED, ERROR
* `cooldown_hours` (float) – Hours between repeat alerts for same CRL (0-168)
* `subject_prefix` (string) – Subject line prefix
* `include_details` (bool) – Include detailed CRL information

#### URIs Section

```json
"uris": [
  {
    "uri": "http://crl.example.com/example.crl",
    "signature_validation_mode": "ca-cert",
    "ca_certificate_path": "certs/ca.crt",
    "expiry_threshold": 0.8,
    "max_crl_size_bytes": 5242880,
    "ldap": {
      "username": "CN=Reader,DC=example,DC=com",
      "password": "secret"
    }
  }
]
```

* `uri` (string, required) – CRL URI (http/https/ldap/ldaps/file)
* `signature_validation_mode` (string) – Validation mode: "none", "ca-cert", "self-signed"
* `ca_certificate_path` (string) – Path to CA certificate (required if mode is "ca-cert")
* `expiry_threshold` (float) – Fraction of lifetime remaining before warning (0.1-1.0, default: 0.8)
* `max_crl_size_bytes` (int) – Per-CRL size limit (overrides global setting)
* `ldap` (object) – LDAP credentials (required for ldap/ldaps URIs)

## 4. Running CrlMonitor Manually

Run from PowerShell or CMD:

```
CrlMonitor.exe config.json
```

Or using full path:

```
CrlMonitor.exe C:\CrlMonitor\config.json
```

**Configuration File Location:** Keep `config.json` with `CrlMonitor.exe` (e.g., `C:\CrlMonitor\config.json`). If no argument supplied, app looks in exe directory. Output files (logs, reports, state) default to `%ProgramData%\RedKestrel\CrlMonitor\` following Windows conventions for application data.

### Exit Codes

* `0` – success
* `1` – failure (config error, license error, validation failure, etc.)

## 5. Running via Scheduled Task (Recommended)

Most deployments run CrlMonitor automatically.

### Creating the Scheduled Task

1. Open **Task Scheduler**.
2. Choose **Create Task…**.
3. Set **Run as** to `LocalSystem` or a suitable service account.
4. Add an **Action** calling:
   `C:\CrlMonitor\CrlMonitor.exe C:\CrlMonitor\config.json`
5. Add a **Trigger**:

   * Daily (every 24 hours), or
   * Every 4 hours for faster alerting

### Notes

* Console output does not appear in Task Scheduler. Check logs and reports at the paths defined in `config.json`.
* Ensure the task user has write permissions to the log/report folders.
* The EULA must be accepted on first run. Run manually once before scheduling, or use `--accept-eula` flag in automated deployments.
* License file must be accessible to the scheduled task account.

## 6. Reports

CrlMonitor generates two optional report types.

### HTML Report

A detailed dashboard including summary counts and a full table of CRLs with status, issuer, timestamps, signature verification, size, download time, revocation count, and previous check time.

Configured by:

* `html_report_enabled` (bool)
* `html_report_path` (string)
* `html_report_url` (string, optional - included in email alerts)

### CSV Report

A machine-readable CSV listing all CRL rows with columns: URI, Status, Fetch Time, Error, Issuer, This Update, Next Update, Signature Valid, Download Time, Size Bytes, Revocations, Previous Fetch.

Configured by:

* `csv_reports` (bool)
* `csv_output_path` (string)
* `csv_append_timestamp` (bool) – adds timestamped files if enabled

## 7. Alerts

Alerts are sent when a CRL enters a monitored state such as ERROR, EXPIRED, or EXPIRING.

Key fields:

* `alerts.enabled` (bool)
* `alerts.statuses` (array) – which statuses trigger alerts
* `alerts.recipients` (array)
* `alerts.cooldown_hours` (float)

Cooldown prevents repeated notifications if the CRL remains in the same state. State is tracked in the file specified by `state_file_path`.

## 8. Logging

Logging uses Serilog with rolling daily files. Main settings:

* `logging.min_level` – Verbose, Debug, Information, Warning, Error, Fatal (default: Information)
* `logging.log_file_path` – Path to log file (default: "CrlMonitor.log")
* `logging.rolling_interval` – Day, Hour, Minute, etc. (default: Day)
* `logging.retained_file_count_limit` – Number of old logs to keep (default: 7)

### Log File Location

* **Relative paths** (e.g., "CrlMonitor.log") resolve to `%ProgramData%\RedKestrel\CrlMonitor\` on Windows
  * Default location: `C:\ProgramData\RedKestrel\CrlMonitor\CrlMonitor.log`
  * Fallback: Executable directory if ProgramData is not writable
* **Absolute paths** (e.g., "C:\Logs\CrlMonitor.log") are used as-is

Log files include timestamps, log level, message, and exception details.

## 9. Licensing

CrlMonitor supports both trial and standard licenses.

* **Trial licenses** automatically enforce a 30-day usage period from first run.
* **Standard licenses** validate machine binding and expiry date.
* License errors stop execution and display a message with contact information.

### License File Location

Store the `license.lic` file in a location accessible to the user or service account:

* `C:\ProgramData\RedKestrel\CrlMonitor\license.lic` (recommended for Windows)
* Or in the executable directory

The application searches for `license.lic` in:
1. Application directory
2. User's home directory
3. Common application data folder

### Trial Period

Trial licenses show remaining days in console output and logs. After 30 days from first use, the application will stop running until a standard license is installed.

## 10. Troubleshooting

### Config file not found

Check the full path in the Task Scheduler **Action**. Ensure you're passing the config file path as the first argument.

### No HTML or CSV output

Confirm reporting is enabled in the config and that the scheduled-task user has write permissions to the output directories. Check logs for errors.

### SMTP not working

* Check `enable_starttls` settings, ports (587 for STARTTLS, 465 for SSL), credentials
* Verify firewall allows outbound SMTP connections
* Check logs for detailed error messages
* Password can be in config or `SMTP_PASSWORD` environment variable

### LDAP CRLs failing

* Check hostname resolution
* Verify LDAP credentials in config
* Ensure firewall allows LDAP/LDAPS traffic
* Use `ldaps://` for encrypted connections

### CRL signature invalid

* Verify the correct CA certificate is referenced in `ca_certificate_path`
* Ensure certificate file is in PEM or DER format
* Check certificate is not expired
* Use `signature_validation_mode: "none"` to disable validation (not recommended for production)

### EULA not accepted

Run the application manually once to accept the EULA. The acceptance is stored in `%ProgramData%\RedKestrel\CrlMonitor` and persists for scheduled runs.

**For automated deployments (IaC/Ansible/SCCM):** Use the `--accept-eula` flag to bypass interactive acceptance:

```
CrlMonitor.exe --accept-eula config.json
```

This is particularly useful for Infrastructure as Code deployments and automated configuration management.

### License validation failing

* Ensure `license.lic` file exists and is readable by the service account
* Check logs for detailed license validation errors
* For trial licenses, verify 30 days have not elapsed since first run
* Contact sales@redkestrel.co.uk for licensing issues

### Proxy authentication failing

* Ensure `use_system_proxy: true` in config
* Verify service account has network access
* Windows integrated authentication uses the service account's credentials
* Check proxy allows the service account to connect

---

## 11. Example Configuration

```json
{
  "logging": {
    "min_level": "Information",
    "log_file_path": "CrlMonitor.log",
    "rolling_interval": "Day",
    "retained_file_count_limit": 7
  },
  "console_reports": true,
  "console_verbose": false,
  "csv_reports": true,
  "csv_output_path": "%ProgramData%/RedKestrel/CrlMonitor/crl-report.csv",
  "csv_append_timestamp": false,
  "html_report_enabled": true,
  "html_report_path": "%ProgramData%/RedKestrel/CrlMonitor/crl-report.html",
  "html_report_url": "https://monitoring.example.com/crl-report.html",
  "fetch_timeout_seconds": 30,
  "max_parallel_fetches": 6,
  "max_crl_size_bytes": 10485760,
  "use_system_proxy": true,
  "state_file_path": "%ProgramData%/RedKestrel/CrlMonitor/state.json",
  "smtp": {
    "host": "smtp.example.com",
    "port": 587,
    "username": "crlmonitor@example.com",
    "password": "",
    "from": "CRL Monitor <crlmonitor@example.com>",
    "enable_starttls": true
  },
  "reports": {
    "enabled": true,
    "report_frequency_hours": 24,
    "recipients": ["admin@example.com"],
    "subject": "Daily CRL Health Report",
    "include_summary": true,
    "include_full_csv": true
  },
  "alerts": {
    "enabled": true,
    "recipients": ["alerts@example.com"],
    "statuses": ["ERROR", "EXPIRED"],
    "cooldown_hours": 24,
    "subject_prefix": "[CRL Alert]",
    "include_details": true
  },
  "uris": [
    {
      "uri": "http://crl3.digicert.com/DigiCertGlobalRootCA.crl",
      "signature_validation_mode": "ca-cert",
      "ca_certificate_path": "examples/CA-certs/DigiCertGlobalRootCA.crt",
      "expiry_threshold": 0.8
    },
    {
      "uri": "http://crl.globalsign.com/gsrsaovsslca2018.crl",
      "signature_validation_mode": "ca-cert",
      "ca_certificate_path": "examples/CA-certs/GlobalSignRSAOVSSLCA2018.pem",
      "expiry_threshold": 0.8
    }
  ]
}
```

---

## Support

For support, feature requests, or licensing enquiries:
* Email: support@redkestrel.co.uk
* Sales: sales@redkestrel.co.uk
