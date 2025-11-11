#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
LICENSE_GENERATOR_PROJECT="${REPO_ROOT}/../RedKestrel.Licensing/LicenseGenerator/LicenseGenerator.csproj"
OUTPUT_DIR="${HOME}/.CrlMonitorLicenseKeys"
OUTPUT_FILE="${OUTPUT_DIR}/crl-monitor-license-keys.txt"

if [[ -e "${OUTPUT_FILE}" ]]; then
  echo "Refusing to overwrite existing key file at ${OUTPUT_FILE}." >&2
  echo "Move/rename the current file or supply a different OUTPUT_DIR/OUTPUT_FILE before running this script." >&2
  exit 1
fi

if [[ ! -f "${LICENSE_GENERATOR_PROJECT}" ]]; then
  echo "Could not find LicenseGenerator project at ${LICENSE_GENERATOR_PROJECT}." >&2
  echo "Please ensure the RedKestrel.Licensing repo is checked out alongside CrlMonitorNX." >&2
  exit 1
fi

PASSPHRASE="${LICENSE_PASSPHRASE:-}"
if [[ -z "${PASSPHRASE}" ]]; then
  echo "LICENSE_PASSPHRASE is not set."
  echo "Export it before running this script, e.g.:"
  echo "  export LICENSE_PASSPHRASE='choose-a-strong-secret'"
  echo "  ./Licensing/scripts/generate-license-keys.sh"
  echo
  read -rsp "Enter passphrase to encrypt the key pair: " PASSPHRASE
  echo
  if [[ -z "${PASSPHRASE}" ]]; then
    echo "Passphrase cannot be empty." >&2
    exit 1
  fi
fi

mkdir -p "${OUTPUT_DIR}"

echo "Generating key pair with LicenseGenerator..."
dotnet run --project "${LICENSE_GENERATOR_PROJECT}" -- generate-keys \
  --passphrase "${PASSPHRASE}" \
  --output "${OUTPUT_FILE}"

echo "Key pair saved to ${OUTPUT_FILE}"
echo "Keep the private key secure and do not commit it to source control."
