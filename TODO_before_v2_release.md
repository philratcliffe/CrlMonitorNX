# TODO Before v2.0 Release

## Logging Requirements

### Overview
Add structured logging with Serilog to provide visibility into application behaviour and assist with troubleshooting.

### Implementation

**Packages:**
- `Serilog`
- `Serilog.Sinks.File`
- `Serilog.Extensions.Logging` (for Microsoft.Extensions.Logging integration if needed)

**Configuration field:**
```json
{
  "log_level": "Information"
}
```

**Supported levels:**
- `Verbose` - Very detailed, typically only enabled when diagnosing issues
- `Debug` - Less detailed than verbose, useful for development
- `Information` - Default level, tracks general application flow
- `Warning` - Abnormal or unexpected events that don't stop the application
- `Error` - Errors and exceptions
- `Fatal` - Critical errors that cause application termination

**Default:** `Error` (minimal logging in production)

**Rolling file configuration:**
- Directory: `logs/`
- Pattern: `logs/crlmonitor-{Date}.log`
- Retention: 7 days (automatic deletion of older files)
- Format: JSON or text (consider JSON for structured querying)

### Log Scenarios

**Information level:**
- Application startup/shutdown
- Report skipped due to frequency guard (already TODO in EmailReportReporter.cs:42)
- Alert skipped due to cooldown
- EULA acceptance/rejection
- Configuration loaded successfully

**Warning level:**
- Clock skew detected (negative elapsed time) (already TODO in EmailReportReporter.cs:38)
- State file corrupt/unreadable (fallback behaviour triggered)
- SMTP connection issues (retry scenarios)
- CRL fetch timeouts

**Error level:**
- Configuration validation failures
- SMTP send failures (after retries)
- State file write failures
- Licence validation failures
- Unhandled exceptions

### Code Locations to Update

**EmailReportReporter.cs:**
- Line 38: Log warning when clock skew detected
- Line 42: Log info when report skipped due to frequency

**AlertReporter.cs:**
- Log info when alerts skipped due to cooldown

**ConfigLoader.cs:**
- Log info on successful load
- Errors already throw exceptions (captured at Program.cs level)

**Program.cs:**
- Configure Serilog on startup
- Log startup/shutdown events
- Log unhandled exceptions

### Implementation Notes

**Configuration:**
```csharp
var logLevel = document.LogLevel ?? "Error";
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(ParseLogLevel(logLevel))
    .WriteTo.File(
        path: "logs/crlmonitor-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();
```

**UK Spelling:**
- Use "Serialise" in code comments
- Package name "Serilog" remains unchanged (proper noun)

**Testing:**
- Add config validation tests for log_level field
- Test invalid log level values
- Test file rotation behaviour (manual verification)

### Security Considerations

- **DO NOT** log sensitive data:
  - Passwords (SMTP, LDAP)
  - Email content
  - Licence keys
- **DO** log sanitised versions:
  - URIs (safe to log)
  - Recipient email addresses (consider privacy requirements)
  - Timing information
  - Status codes

### Documentation

Update README.md with:
- Log file location
- How to change log level
- Log retention policy
- How to disable logging (set level to "Fatal" or omit field)

---

## Other Items

(Add additional v2.0 release requirements here as they arise)
