# Integration Tests

Python-based integration tests for CrlMonitor that verify end-to-end functionality.

## Running Integration Tests

### Run All Tests (Unit + Integration)

```bash
dotnet test -p:Integration=true
```

### Run Unit Tests Only (Default)

```bash
dotnet test
```

### Run Integration Tests Standalone

```bash
# CSV report test
python3 integration_tests/test_csv_report.py -v

# SMTP report test
python3 integration_tests/test_smtp_report.py -v

# Both tests
python3 integration_tests/test_csv_report.py -v && python3 integration_tests/test_smtp_report.py -v
```

## Requirements

- Python 3.8+
- No additional Python packages required (uses only stdlib)

## Test Coverage

### test_csv_report.py (1 test)
- Verifies CSV report generation
- Tests file:// URI handling
- Validates CA certificate verification
- Checks state persistence

### test_smtp_report.py (6 tests)
- `test_email_report_and_alert_sent_via_local_smtp` - Basic email sending
- `test_report_frequency_guard_prevents_duplicate_sends` - Frequency guard blocks duplicate reports
- `test_report_frequency_guard_allows_send_after_threshold` - Sends after threshold elapsed
- `test_report_sends_when_frequency_omitted` - Always send mode (testing)
- `test_report_sends_with_clock_skew_future_timestamp` - Clock skew handling (defensive)
- `test_report_sends_with_corrupt_state_file` - Corrupt state recovery (defensive)

## Test Output

Tests create temporary files in `integration_tests/test_output/` which are automatically cleaned up after each run. To keep test output for inspection:

```bash
python3 integration_tests/test_smtp_report.py -keep_test_output
```

## CI/CD Integration

The integration tests run automatically when using `-p:Integration=true`:

```bash
# In CI pipeline
dotnet test -p:Integration=true --logger "trx;LogFileName=test-results.trx"
```

This ensures compatibility with Windows/Linux/macOS build agents that may not have Python installed by default.
