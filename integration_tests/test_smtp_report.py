import argparse
import base64
import json
import shutil
import socket
import socketserver
import subprocess
import sys
import threading
import time
import unittest
from dataclasses import dataclass
from email import message_from_string
from email.message import Message
from pathlib import Path
from typing import Any, cast

KEEP_TEST_OUTPUT = False


@dataclass
class CapturedMessage:
    mail_from: str
    recipients: list[str]
    data: str


class _ThreadedSMTPServer(socketserver.ThreadingMixIn, socketserver.TCPServer):
    allow_reuse_address = True

    def __init__(self, server_address: tuple[str, int], mailbox: list[CapturedMessage]):
        super().__init__(server_address, _SmtpRequestHandler)
        self.mailbox = mailbox


class _SmtpRequestHandler(socketserver.StreamRequestHandler):
    def handle(self) -> None:
        mail_from = ""
        recipients: list[str] = []
        self._write("220 localhost\r\n")

        while True:
            line = self.rfile.readline()
            if not line:
                break
            command = line.decode("utf-8", errors="replace").strip()
            upper = command.upper()

            if upper.startswith("EHLO") or upper.startswith("HELO"):
                self._write("250-localhost\r\n250 AUTH LOGIN\r\n")
            elif upper.startswith("AUTH LOGIN"):
                self._handle_auth(command)
            elif upper.startswith("MAIL FROM:"):
                mail_from = self._extract_address(command[10:])
                self._write("250 OK\r\n")
            elif upper.startswith("RCPT TO:"):
                recipients.append(self._extract_address(command[8:]))
                self._write("250 OK\r\n")
            elif upper == "DATA":
                self._write("354 End data with <CR><LF>.<CR><LF>\r\n")
                data = self._read_data()
                server = cast(_ThreadedSMTPServer, self.server)
                server.mailbox.append(
                    CapturedMessage(mail_from=mail_from, recipients=list(recipients), data=data)
                )
                self._write("250 OK\r\n")
            elif upper == "RSET":
                mail_from = ""
                recipients.clear()
                self._write("250 OK\r\n")
            elif upper == "QUIT":
                self._write("221 Bye\r\n")
                break
            else:
                self._write("250 OK\r\n")

    def _handle_auth(self, command: str) -> None:
        parts = command.split()
        username = ""
        if len(parts) == 3:
            username = self._decode_base64(parts[2])
            self._write("334 UGFzc3dvcmQ6\r\n")
        else:
            self._write("334 VXNlcm5hbWU6\r\n")
            username_line = self.rfile.readline().strip()
            username = self._decode_base64(username_line.decode("utf-8", errors="replace"))
            self._write("334 UGFzc3dvcmQ6\r\n")

        password_line = self.rfile.readline().strip()
        self._decode_base64(password_line.decode("utf-8", errors="replace"))
        _ = username  # not used, but ensures decoding occurs
        self._write("235 2.7.0 Authentication successful\r\n")

    def _read_data(self) -> str:
        lines: list[str] = []
        while True:
            chunk = self.rfile.readline()
            if not chunk:
                break
            if chunk == b".\r\n":
                break
            lines.append(chunk.decode("utf-8", errors="replace"))
        return "".join(lines)

    @staticmethod
    def _extract_address(value: str) -> str:
        trimmed = value.strip()
        if trimmed.startswith("<") and trimmed.endswith(">"):
            trimmed = trimmed[1:-1]
        if ":" in trimmed:
            trimmed = trimmed.split(":", 1)[1]
        return trimmed.strip()

    @staticmethod
    def _decode_base64(value: str) -> str:
        try:
            return base64.b64decode(value.encode("utf-8"), validate=True).decode("utf-8", errors="replace")
        except Exception:
            return ""

    def _write(self, payload: str) -> None:
        self.wfile.write(payload.encode("utf-8"))
        self.wfile.flush()

class _SmtpServerController:
    def __init__(self, host: str, port: int):
        self._host = host
        self._port = port
        self.messages: list[CapturedMessage] = []
        self._server = _ThreadedSMTPServer((self._host, self._port), self.messages)
        self._thread = threading.Thread(target=self._server.serve_forever, kwargs={"poll_interval": 0.1}, daemon=True)

    def __enter__(self) -> "_SmtpServerController":
        self._thread.start()
        return self

    def __exit__(self, exc_type, exc_val, exc_tb) -> None:
        self._server.shutdown()
        self._server.server_close()
        self._thread.join(timeout=2)


class SmtpReportIntegrationTest(unittest.TestCase):
    def setUp(self) -> None:
        self.test_output_dir = Path(__file__).parent / "test_output" / "smtp"
        if self.test_output_dir.exists():
            shutil.rmtree(self.test_output_dir)
        self.test_output_dir.mkdir(parents=True, exist_ok=True)

    def tearDown(self) -> None:
        if not KEEP_TEST_OUTPUT and self.test_output_dir.exists():
            shutil.rmtree(self.test_output_dir)

    def test_email_report_and_alert_sent_via_local_smtp(self) -> None:
        repo_root = Path(__file__).resolve().parents[1]
        crl_path = (repo_root / "examples" / "crls" / "DigiCertGlobalRootCA.crl").resolve()
        ca_path = (repo_root / "examples" / "CA-certs" / "DigiCertGlobalRootCA.crt").resolve()
        missing_crl = self.test_output_dir / "missing.crl"

        output_csv = self.test_output_dir / "report.csv"
        state_file = self.test_output_dir / "state.json"
        config_path = self.test_output_dir / "config.json"

        smtp_host = "127.0.0.1"
        smtp_port = _find_free_port()

        config: dict[str, Any] = {
            "console_reports": False,
            "csv_reports": False,
            "csv_output_path": str(output_csv),
            "csv_append_timestamp": False,
            "fetch_timeout_seconds": 30,
            "max_parallel_fetches": 1,
            "state_file_path": str(state_file),
            "smtp": {
                "host": smtp_host,
                "port": smtp_port,
                "username": "reporter@example.com",
                "password": "password!",
                "from": "CRL Monitor <monitor@example.com>",
                "enable_starttls": False,
            },
            "reports": {
                "enabled": True,
                "report_frequency_hours": 24,
                "recipients": ["recipient@example.com"],
                "subject": "CRL Health Report",
                "include_summary": True,
                "include_full_csv": False,
            },
            "alerts": {
                "enabled": True,
                "recipients": ["alerts@example.com"],
                "statuses": ["ERROR"],
                "cooldown_hours": 0,
                "subject_prefix": "[CRL Alert]",
                "include_details": True
            },
            "uris": [
                {
                    "uri": crl_path.as_uri(),
                    "signature_validation_mode": "ca-cert",
                    "ca_certificate_path": str(ca_path),
                    "expiry_threshold": 0.8,
                },
                {
                    "uri": missing_crl.as_uri(),
                    "signature_validation_mode": "none",
                    "expiry_threshold": 0.8
                }
            ],
        }
        config_path.write_text(json.dumps(config, indent=2), encoding="utf-8")

        with _SmtpServerController(smtp_host, smtp_port) as smtp_server:
            self._run_monitor(repo_root, config_path)
            self._wait_for_messages(smtp_server.messages, expected=2, timeout_seconds=5)
            report_message = self._find_message(smtp_server.messages, "Subject: CRL Health Report")
            alert_message = self._find_message(smtp_server.messages, "[CRL Alert]")

        self.assertEqual(report_message.mail_from, "monitor@example.com")
        self.assertEqual(report_message.recipients, ["recipient@example.com"])
        self.assertIn("Content-Type: multipart/alternative", report_message.data)
        plain_body = self._extract_body_part(report_message.data, "text/plain")
        html_body = self._extract_body_part(report_message.data, "text/html")
        self.assertIn("Please find attached the CRL Health Report", plain_body)
        self.assertIn("Please find attached the CRL Health Report", html_body)

        self.assertEqual(alert_message.mail_from, "monitor@example.com")
        self.assertEqual(alert_message.recipients, ["alerts@example.com"])
        alert_body = self._extract_body_part(alert_message.data, "text/plain")
        self.assertIn("issue(s) detected during the latest CRL check", alert_body)
        self.assertIn("CRL file not found", alert_body)

    def _run_monitor(self, repo_root: Path, config_path: Path) -> None:
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

    @staticmethod
    def _wait_for_messages(messages: list[CapturedMessage], expected: int, timeout_seconds: float) -> None:
        deadline = time.time() + timeout_seconds
        while time.time() < deadline:
            if len(messages) >= expected:
                return
            time.sleep(0.1)
        raise AssertionError(f"Expected {expected} SMTP message(s), saw {len(messages)}")

    @staticmethod
    def _find_message(messages: list[CapturedMessage], subject_marker: str) -> CapturedMessage:
        for message in messages:
            if subject_marker in message.data:
                return message
        raise AssertionError(f"Message containing '{subject_marker}' not found")

    @staticmethod
    def _extract_body_part(raw_message: str, content_type: str) -> str:
        parsed: Message = message_from_string(raw_message)
        for part in parsed.walk():
            if part.get_content_type() != content_type:
                continue
            payload = part.get_payload(decode=True)
            if payload is None:
                continue
            charset = part.get_content_charset() or "utf-8"
            return payload.decode(charset, errors="replace")
        raise AssertionError(f"{content_type} part not found in message")

    def test_report_frequency_guard_prevents_duplicate_sends(self) -> None:
        """Core behavior: frequency guard blocks reports within threshold, but allows alerts."""
        repo_root = Path(__file__).resolve().parents[1]
        crl_path = (repo_root / "examples" / "crls" / "DigiCertGlobalRootCA.crl").resolve()
        ca_path = (repo_root / "examples" / "CA-certs" / "DigiCertGlobalRootCA.crt").resolve()
        missing_crl = self.test_output_dir / "missing.crl"

        state_file = self.test_output_dir / "state.json"
        config_path = self.test_output_dir / "config.json"

        smtp_host = "127.0.0.1"
        smtp_port = _find_free_port()

        config: dict[str, Any] = {
            "console_reports": False,
            "csv_reports": False,
            "csv_output_path": str(self.test_output_dir / "report.csv"),
            "csv_append_timestamp": False,
            "fetch_timeout_seconds": 30,
            "max_parallel_fetches": 1,
            "state_file_path": str(state_file),
            "smtp": {
                "host": smtp_host,
                "port": smtp_port,
                "username": "reporter@example.com",
                "password": "password!",
                "from": "CRL Monitor <monitor@example.com>",
                "enable_starttls": False,
            },
            "reports": {
                "enabled": True,
                "report_frequency_hours": 24,
                "recipients": ["recipient@example.com"],
                "subject": "CRL Health Report",
                "include_summary": True,
                "include_full_csv": False,
            },
            "alerts": {
                "enabled": True,
                "recipients": ["alerts@example.com"],
                "statuses": ["ERROR"],
                "cooldown_hours": 0,
                "subject_prefix": "[CRL Alert]",
                "include_details": True
            },
            "uris": [
                {
                    "uri": crl_path.as_uri(),
                    "signature_validation_mode": "ca-cert",
                    "ca_certificate_path": str(ca_path),
                    "expiry_threshold": 0.8,
                },
                {
                    "uri": missing_crl.as_uri(),
                    "signature_validation_mode": "none",
                    "expiry_threshold": 0.8
                }
            ],
        }
        config_path.write_text(json.dumps(config, indent=2), encoding="utf-8")

        # Run 1: Both report and alert should be sent (first run, no state)
        with _SmtpServerController(smtp_host, smtp_port) as smtp_server:
            self._run_monitor(repo_root, config_path)
            self._wait_for_messages(smtp_server.messages, expected=2, timeout_seconds=5)
            report_message = self._find_message(smtp_server.messages, "Subject: CRL Health Report")
            alert_message = self._find_message(smtp_server.messages, "[CRL Alert]")

        self.assertEqual(report_message.recipients, ["recipient@example.com"])
        self.assertEqual(alert_message.recipients, ["alerts@example.com"])

        # Verify state file created with last_report_sent_utc
        self.assertTrue(state_file.exists(), "State file should be created after first run")
        state_data = json.loads(state_file.read_text(encoding="utf-8"))
        self.assertIn("last_report_sent_utc", state_data, "State must track last_report_sent_utc")
        first_report_timestamp = state_data["last_report_sent_utc"]
        self.assertIsNotNone(first_report_timestamp, "Timestamp must be set after sending report")

        # Run 2: Only alert should be sent (report blocked by frequency guard)
        with _SmtpServerController(smtp_host, smtp_port) as smtp_server:
            self._run_monitor(repo_root, config_path)
            self._wait_for_messages(smtp_server.messages, expected=1, timeout_seconds=5)
            # Should ONLY have alert, no report
            self.assertEqual(len(smtp_server.messages), 1, "Only alert should be sent on second run")
            alert_message = self._find_message(smtp_server.messages, "[CRL Alert]")
            self.assertEqual(alert_message.recipients, ["alerts@example.com"])
            # Verify no report message
            with self.assertRaises(AssertionError):
                self._find_message(smtp_server.messages, "Subject: CRL Health Report")

        # Verify state timestamp unchanged (report was not sent)
        state_data = json.loads(state_file.read_text(encoding="utf-8"))
        self.assertEqual(state_data["last_report_sent_utc"], first_report_timestamp,
                        "Timestamp should not change when report skipped by frequency guard")

    def test_report_frequency_guard_allows_send_after_threshold(self) -> None:
        """Frequency guard allows report send when threshold elapsed."""
        repo_root = Path(__file__).resolve().parents[1]
        crl_path = (repo_root / "examples" / "crls" / "DigiCertGlobalRootCA.crl").resolve()
        ca_path = (repo_root / "examples" / "CA-certs" / "DigiCertGlobalRootCA.crt").resolve()

        state_file = self.test_output_dir / "state.json"
        config_path = self.test_output_dir / "config.json"

        smtp_host = "127.0.0.1"
        smtp_port = _find_free_port()

        # Pre-populate state with old timestamp (25 hours ago)
        from datetime import datetime, timedelta, timezone
        old_timestamp = (datetime.now(timezone.utc) - timedelta(hours=25)).isoformat()
        state_file.write_text(json.dumps({
            "last_fetch": {},
            "alert_cooldowns": {},
            "last_report_sent_utc": old_timestamp
        }), encoding="utf-8")

        config: dict[str, Any] = {
            "console_reports": False,
            "csv_reports": False,
            "csv_output_path": str(self.test_output_dir / "report.csv"),
            "csv_append_timestamp": False,
            "fetch_timeout_seconds": 30,
            "max_parallel_fetches": 1,
            "state_file_path": str(state_file),
            "smtp": {
                "host": smtp_host,
                "port": smtp_port,
                "username": "reporter@example.com",
                "password": "password!",
                "from": "CRL Monitor <monitor@example.com>",
                "enable_starttls": False,
            },
            "reports": {
                "enabled": True,
                "report_frequency_hours": 24,
                "recipients": ["recipient@example.com"],
                "subject": "CRL Health Report",
                "include_summary": True,
                "include_full_csv": False,
            },
            "alerts": {
                "enabled": False,
                "recipients": [],
                "statuses": [],
                "cooldown_hours": 0,
                "subject_prefix": "",
                "include_details": False
            },
            "uris": [
                {
                    "uri": crl_path.as_uri(),
                    "signature_validation_mode": "ca-cert",
                    "ca_certificate_path": str(ca_path),
                    "expiry_threshold": 0.8,
                }
            ],
        }
        config_path.write_text(json.dumps(config, indent=2), encoding="utf-8")

        # Run: Report should be sent (threshold exceeded)
        with _SmtpServerController(smtp_host, smtp_port) as smtp_server:
            self._run_monitor(repo_root, config_path)
            self._wait_for_messages(smtp_server.messages, expected=1, timeout_seconds=5)
            report_message = self._find_message(smtp_server.messages, "Subject: CRL Health Report")

        self.assertEqual(report_message.recipients, ["recipient@example.com"])

        # Verify state timestamp updated to new value
        state_data = json.loads(state_file.read_text(encoding="utf-8"))
        new_timestamp = state_data["last_report_sent_utc"]
        self.assertNotEqual(new_timestamp, old_timestamp, "Timestamp must be updated after sending report")

    def test_report_sends_when_frequency_omitted(self) -> None:
        """Omitting report_frequency_hours means always send (for testing/debugging)."""
        repo_root = Path(__file__).resolve().parents[1]
        crl_path = (repo_root / "examples" / "crls" / "DigiCertGlobalRootCA.crl").resolve()
        ca_path = (repo_root / "examples" / "CA-certs" / "DigiCertGlobalRootCA.crt").resolve()

        state_file = self.test_output_dir / "state.json"
        config_path = self.test_output_dir / "config.json"

        smtp_host = "127.0.0.1"
        smtp_port = _find_free_port()

        config: dict[str, Any] = {
            "console_reports": False,
            "csv_reports": False,
            "csv_output_path": str(self.test_output_dir / "report.csv"),
            "csv_append_timestamp": False,
            "fetch_timeout_seconds": 30,
            "max_parallel_fetches": 1,
            "state_file_path": str(state_file),
            "smtp": {
                "host": smtp_host,
                "port": smtp_port,
                "username": "reporter@example.com",
                "password": "password!",
                "from": "CRL Monitor <monitor@example.com>",
                "enable_starttls": False,
            },
            "reports": {
                "enabled": True,
                # NO report_frequency_hours field - should always send
                "recipients": ["recipient@example.com"],
                "subject": "CRL Health Report",
                "include_summary": True,
                "include_full_csv": False,
            },
            "alerts": {
                "enabled": False,
                "recipients": [],
                "statuses": [],
                "cooldown_hours": 0,
                "subject_prefix": "",
                "include_details": False
            },
            "uris": [
                {
                    "uri": crl_path.as_uri(),
                    "signature_validation_mode": "ca-cert",
                    "ca_certificate_path": str(ca_path),
                    "expiry_threshold": 0.8,
                }
            ],
        }
        config_path.write_text(json.dumps(config, indent=2), encoding="utf-8")

        # Run 1: Report should send
        with _SmtpServerController(smtp_host, smtp_port) as smtp_server:
            self._run_monitor(repo_root, config_path)
            self._wait_for_messages(smtp_server.messages, expected=1, timeout_seconds=5)
            self._find_message(smtp_server.messages, "Subject: CRL Health Report")

        # Run 2: Report should send AGAIN (no frequency guard active)
        with _SmtpServerController(smtp_host, smtp_port) as smtp_server:
            self._run_monitor(repo_root, config_path)
            self._wait_for_messages(smtp_server.messages, expected=1, timeout_seconds=5)
            self._find_message(smtp_server.messages, "Subject: CRL Health Report")

    def test_report_sends_with_clock_skew_future_timestamp(self) -> None:
        """Clock skew: future timestamp in state should trigger send (defensive)."""
        repo_root = Path(__file__).resolve().parents[1]
        crl_path = (repo_root / "examples" / "crls" / "DigiCertGlobalRootCA.crl").resolve()
        ca_path = (repo_root / "examples" / "CA-certs" / "DigiCertGlobalRootCA.crt").resolve()

        state_file = self.test_output_dir / "state.json"
        config_path = self.test_output_dir / "config.json"

        smtp_host = "127.0.0.1"
        smtp_port = _find_free_port()

        # Pre-populate state with FUTURE timestamp (5 hours ahead)
        from datetime import datetime, timedelta, timezone
        future_timestamp = (datetime.now(timezone.utc) + timedelta(hours=5)).isoformat()
        state_file.write_text(json.dumps({
            "last_fetch": {},
            "alert_cooldowns": {},
            "last_report_sent_utc": future_timestamp
        }), encoding="utf-8")

        config: dict[str, Any] = {
            "console_reports": False,
            "csv_reports": False,
            "csv_output_path": str(self.test_output_dir / "report.csv"),
            "csv_append_timestamp": False,
            "fetch_timeout_seconds": 30,
            "max_parallel_fetches": 1,
            "state_file_path": str(state_file),
            "smtp": {
                "host": smtp_host,
                "port": smtp_port,
                "username": "reporter@example.com",
                "password": "password!",
                "from": "CRL Monitor <monitor@example.com>",
                "enable_starttls": False,
            },
            "reports": {
                "enabled": True,
                "report_frequency_hours": 24,
                "recipients": ["recipient@example.com"],
                "subject": "CRL Health Report",
                "include_summary": True,
                "include_full_csv": False,
            },
            "alerts": {
                "enabled": False,
                "recipients": [],
                "statuses": [],
                "cooldown_hours": 0,
                "subject_prefix": "",
                "include_details": False
            },
            "uris": [
                {
                    "uri": crl_path.as_uri(),
                    "signature_validation_mode": "ca-cert",
                    "ca_certificate_path": str(ca_path),
                    "expiry_threshold": 0.8,
                }
            ],
        }
        config_path.write_text(json.dumps(config, indent=2), encoding="utf-8")

        # Run: Report should send anyway (negative elapsed = clock skew)
        with _SmtpServerController(smtp_host, smtp_port) as smtp_server:
            self._run_monitor(repo_root, config_path)
            self._wait_for_messages(smtp_server.messages, expected=1, timeout_seconds=5)
            report_message = self._find_message(smtp_server.messages, "Subject: CRL Health Report")

        self.assertEqual(report_message.recipients, ["recipient@example.com"])

    def test_report_sends_with_corrupt_state_file(self) -> None:
        """Corrupt state file should not block report (defensive fallback)."""
        repo_root = Path(__file__).resolve().parents[1]
        crl_path = (repo_root / "examples" / "crls" / "DigiCertGlobalRootCA.crl").resolve()
        ca_path = (repo_root / "examples" / "CA-certs" / "DigiCertGlobalRootCA.crt").resolve()

        state_file = self.test_output_dir / "state.json"
        config_path = self.test_output_dir / "config.json"

        smtp_host = "127.0.0.1"
        smtp_port = _find_free_port()

        # Create corrupt state file (invalid JSON)
        state_file.write_text("{this is not valid json!}", encoding="utf-8")

        config: dict[str, Any] = {
            "console_reports": False,
            "csv_reports": False,
            "csv_output_path": str(self.test_output_dir / "report.csv"),
            "csv_append_timestamp": False,
            "fetch_timeout_seconds": 30,
            "max_parallel_fetches": 1,
            "state_file_path": str(state_file),
            "smtp": {
                "host": smtp_host,
                "port": smtp_port,
                "username": "reporter@example.com",
                "password": "password!",
                "from": "CRL Monitor <monitor@example.com>",
                "enable_starttls": False,
            },
            "reports": {
                "enabled": True,
                "report_frequency_hours": 24,
                "recipients": ["recipient@example.com"],
                "subject": "CRL Health Report",
                "include_summary": True,
                "include_full_csv": False,
            },
            "alerts": {
                "enabled": False,
                "recipients": [],
                "statuses": [],
                "cooldown_hours": 0,
                "subject_prefix": "",
                "include_details": False
            },
            "uris": [
                {
                    "uri": crl_path.as_uri(),
                    "signature_validation_mode": "ca-cert",
                    "ca_certificate_path": str(ca_path),
                    "expiry_threshold": 0.8,
                }
            ],
        }
        config_path.write_text(json.dumps(config, indent=2), encoding="utf-8")

        # Run: Report should send anyway (corrupt state = treat as first run)
        with _SmtpServerController(smtp_host, smtp_port) as smtp_server:
            self._run_monitor(repo_root, config_path)
            self._wait_for_messages(smtp_server.messages, expected=1, timeout_seconds=5)
            report_message = self._find_message(smtp_server.messages, "Subject: CRL Health Report")

        self.assertEqual(report_message.recipients, ["recipient@example.com"])


def _find_free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="CRL Monitor SMTP integration test",
        epilog="Examples:\n  python test_smtp_report.py\n  python test_smtp_report.py -keep_test_output",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("-keep_test_output", action="store_true", help="keep generated config/state files")

    all_args = sys.argv[1:]
    non_flag_args = [arg for arg in all_args if not arg.startswith("-")]
    flag_args = [arg for arg in all_args if arg.startswith("-") and not arg.startswith("-v")]
    unknown_flags = [arg for arg in flag_args if arg not in ["-keep_test_output", "-h", "--help"]]

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
