import argparse
import csv
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path


@dataclass
class CsvResult:
    header_timestamp: datetime
    row: dict[str, str]


# Global flag to control test output cleanup
KEEP_TEST_OUTPUT = False


class CsvReportIntegrationTest(unittest.TestCase):
    def setUp(self) -> None:
        self.test_output_dir = Path(__file__).parent / "test_output"
        if self.test_output_dir.exists():
            shutil.rmtree(self.test_output_dir)
        self.test_output_dir.mkdir(exist_ok=True)

    def tearDown(self) -> None:
        if not KEEP_TEST_OUTPUT and self.test_output_dir.exists():
            shutil.rmtree(self.test_output_dir)

    def test_single_file_crl_outputs_expected_csv_row(self) -> None:
        repo_root = Path(__file__).resolve().parents[1]
        crl_path = (repo_root / "examples" / "crls" / "DigiCertGlobalRootCA.crl").resolve()
        ca_path = (repo_root / "examples" / "CA-certs" / "DigiCertGlobalRootCA.crt").resolve()
        expected_uri = crl_path.as_uri()
        expected_issuer = "C=US,O=DigiCert Inc,OU=www.digicert.com,CN=DigiCert Global Root CA"

        output_csv = self.test_output_dir / "report.csv"
        state_file = self.test_output_dir / "state.json"
        config_path = self.test_output_dir / "config.json"
        config = {
            "console_reports": False,
            "csv_reports": True,
            "csv_output_path": str(output_csv),
            "csv_append_timestamp": False,
            "fetch_timeout_seconds": 30,
            "max_parallel_fetches": 1,
            "state_file_path": str(state_file),
            "uris": [
                {
                    "uri": expected_uri,
                    "signature_validation_mode": "ca-cert",
                    "ca_certificate_path": str(ca_path),
                    "expiry_threshold": 0.8,
                }
            ],
        }
        config_path.write_text(json.dumps(config, indent=2), encoding="utf-8")

        first = self._run_and_parse(repo_root, config_path, output_csv)
        self.assertEqual(first.row["URI"], expected_uri)
        self.assertEqual(first.row["Issuer_Name"], expected_issuer)
        self.assertEqual(first.row["Status"], "OK")
        self.assertEqual(first.row["This_Update_UTC"], "2025-11-05T19:45:58Z")
        self.assertEqual(first.row["Next_Update_UTC"], "2025-11-26T19:45:58Z")
        self.assertEqual(first.row["CRL_Size_bytes"], str(crl_path.stat().st_size))
        self.assertEqual(first.row["Download_Duration_ms"], "0")
        self.assertEqual(first.row["Signature_Valid"], "True")
        self.assertEqual(first.row["Revoked_Count"], "6")
        self.assertEqual(first.row["Previous_Checked_Time_UTC"], "")
        self.assertEqual(first.row["CRL_Type"], "Full")
        self.assertEqual(first.row["Status_Details"], "")
        first_checked = _parse_utc(first.row["Checked_Time_UTC"])
        self._assert_header_matches_row(first.header_timestamp, first_checked)

        second = self._run_and_parse(repo_root, config_path, output_csv)
        self.assertEqual(second.row["Previous_Checked_Time_UTC"], first.row["Checked_Time_UTC"])
        second_checked = _parse_utc(second.row["Checked_Time_UTC"])
        self.assertGreaterEqual(second_checked, first_checked)
        self._assert_header_matches_row(second.header_timestamp, second_checked)
        self.assertEqual(second.row["CRL_Type"], "Full")
        self.assertEqual(second.row["Status_Details"], "")

    def _run_and_parse(self, repo_root: Path, config_path: Path, csv_path: Path) -> "CsvResult":
        subprocess.run(
            [
                "dotnet",
                "run",
                "--project",
                "CrlMonitor.csproj",
                str(config_path),
            ],
            cwd=repo_root,
            check=True,
            capture_output=True,
        )

        content = csv_path.read_text(encoding="utf-8").splitlines()
        header_line = next(line for line in content if line.startswith("# report_generated_utc"))
        header_timestamp = _parse_utc(header_line.split(",", 1)[1])
        csv_lines = [line for line in content if not line.startswith("#")]
        rows = list(csv.DictReader(csv_lines))
        if len(rows) != 1:
            raise AssertionError("Expected single row in CSV output")
        return CsvResult(header_timestamp, rows[0])

    def _assert_header_matches_row(self, header_time: datetime, checked_time: datetime) -> None:
        now = datetime.now(timezone.utc)
        self.assertLess(abs((now - checked_time).total_seconds()), 120)
        self.assertLess(abs((checked_time - header_time).total_seconds()), 5)


def _parse_utc(value: str) -> datetime:
    if not value:
        raise AssertionError("Expected timestamp value")
    return datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="CRL Monitor CSV report integration test",
        epilog="Examples:\n  python test_csv_report.py\n  python test_csv_report.py -keep_test_output",
        formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument("-keep_test_output", action="store_true", help="keep test output files after completion")

    # Check for any non-flag args or unrecognised flags
    all_args = sys.argv[1:]
    non_flag_args = [arg for arg in all_args if not arg.startswith('-')]
    flag_args = [arg for arg in all_args if arg.startswith('-') and not arg.startswith('-v')]
    unknown_flags = [arg for arg in flag_args if arg not in ['-keep_test_output', '-h', '--help']]

    if non_flag_args:
        print(f"error: unrecognised argument: {non_flag_args[0]}", file=sys.stderr)
        print(file=sys.stderr)
        parser.print_help(sys.stderr)
        sys.exit(2)

    if unknown_flags:
        print(f"error: unrecognised argument: {unknown_flags[0]}", file=sys.stderr)
        print(file=sys.stderr)
        parser.print_help(sys.stderr)
        sys.exit(2)

    args, remaining = parser.parse_known_args()

    if args.keep_test_output:
        KEEP_TEST_OUTPUT = True

    sys.argv = [sys.argv[0]] + remaining
    unittest.main()
