#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
BOOTSTRAPPER_FILE="${REPO_ROOT}/Licensing/LicenseBootstrapper.cs"
DEFAULT_KEY_FILE="${HOME}/.CrlMonitorLicenseKeys/crl-monitor-license-keys.txt"

KEY_FILE="${1:-${DEFAULT_KEY_FILE}}"

if [[ ! -f "${KEY_FILE}" ]]; then
  echo "Key file not found at ${KEY_FILE}." >&2
  echo "Run ./Licensing/scripts/generate-license-keys.sh first or pass the key file path as an argument." >&2
  exit 1
fi

if [[ ! -f "${BOOTSTRAPPER_FILE}" ]]; then
  echo "Cannot find Licensing/LicenseBootstrapper.cs at ${BOOTSTRAPPER_FILE}." >&2
  exit 1
fi

PUBLIC_KEY="$(sed -n '2p' "${KEY_FILE}" | tr -d '\r')"
if [[ -z "${PUBLIC_KEY}" ]]; then
  echo "Public key (line 2) in ${KEY_FILE} is empty." >&2
  exit 1
fi

TEMP_DER="$(mktemp)"
trap 'rm -f "${TEMP_DER}"' EXIT

if ! printf '%s' "${PUBLIC_KEY}" | openssl enc -d -base64 -A > "${TEMP_DER}" 2>/dev/null; then
  echo "Failed to decode base64 public key from ${KEY_FILE}. Make sure line 2 contains the public key." >&2
  exit 1
fi

if ! openssl ec -inform DER -pubin -in "${TEMP_DER}" -text -noout >/dev/null 2>&1; then
  echo "Decoded key is not a valid ECDSA public key. Aborting." >&2
  exit 1
fi

echo "Validated ECDSA public key from ${KEY_FILE}"

export PUBLIC_KEY
export BOOTSTRAPPER_FILE

python - <<'PY'
import os
import re
import sys

bootstrapper = os.environ["BOOTSTRAPPER_FILE"]
public_key = os.environ["PUBLIC_KEY"]

with open(bootstrapper, "r", encoding="utf-8") as handle:
    original = handle.read()

pattern = r'(private const string PublicKey =\s*\n\s*)".*?";'
replacement = r'\1"{}";'.format(public_key.replace('\\', '\\\\').replace('"', '\\"'))
updated, count = re.subn(pattern, replacement, original, flags=re.S)

if count == 0:
    sys.stderr.write("Could not locate PublicKey constant to update in {}.\n".format(bootstrapper))
    sys.exit(1)

with open(bootstrapper, "w", encoding="utf-8") as handle:
    handle.write(updated)

print("Updated PublicKey constant in {}".format(bootstrapper))
PY
