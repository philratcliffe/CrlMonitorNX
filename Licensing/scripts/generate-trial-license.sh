#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
Usage: generate-trial-license.sh [config-path] [output-path]

Generates a 12-month trial license using the shared LicenseGenerator.
- config-path: Optional. Defaults to Licensing/scripts/licensegenerator.config.json
- output-path: Optional. Defaults to Licensing/generated_licenses/trial/license.lic

You must set LICENSE_PASSPHRASE (or enter it interactively) and ensure
../RedKestrel.Licensing is checked out alongside this repo.
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

default_config="$SCRIPT_DIR/licensegenerator.config.json"
CONFIG_PATH="${1:-$default_config}"
CONFIG_PATH="${CONFIG_PATH/#\~/$HOME}"

if [[ ! -f "$CONFIG_PATH" ]]; then
  echo "Config file not found: $CONFIG_PATH" >&2
  exit 1
fi

GENERATOR_PROJECT="$REPO_ROOT/../RedKestrel.Licensing/LicenseGenerator/LicenseGenerator.csproj"
if [[ ! -f "$GENERATOR_PROJECT" ]]; then
  echo "LicenseGenerator project not found at $GENERATOR_PROJECT" >&2
  echo "Ensure RedKestrel.Licensing is checked out next to this repo." >&2
  exit 1
fi

PASSPHRASE="${LICENSE_PASSPHRASE:-}"
if [[ -z "$PASSPHRASE" ]]; then
  read -rsp "Enter license passphrase: " PASSPHRASE
  echo
fi

resolve_keys_path() {
  if [[ -n "${LICENSE_KEYS_PATH:-}" ]]; then
    echo "${LICENSE_KEYS_PATH/#\~/$HOME}"
    return
  fi

  python3 - <<'PY' "$CONFIG_PATH" || true
import json, sys, os
path = None
with open(sys.argv[1], "r", encoding="utf-8") as fh:
    data = json.load(fh)
    path = data.get("keysPath")
if path:
    if path.startswith("~"):
        path = os.path.expanduser(path)
    print(path)
PY
}

KEY_PATH="$(resolve_keys_path)"
if [[ -z "$KEY_PATH" ]]; then
  echo "Unable to determine keys path. Set LICENSE_KEYS_PATH or populate keysPath in the config." >&2
  exit 1
fi

if [[ ! -f "$KEY_PATH" ]]; then
  echo "Key file not found: $KEY_PATH" >&2
  exit 1
fi

calc_expiry_date() {
python3 - "$CONFIG_PATH" <<'PY'
import json, sys, datetime, calendar
with open(sys.argv[1], "r", encoding="utf-8") as fh:
    cfg = json.load(fh)
months = cfg.get("defaultExpiryMonths")
if not isinstance(months, int) or months <= 0:
    months = 12
now = datetime.datetime.now(datetime.timezone.utc)
total_months = (now.month - 1) + months
year = now.year + (total_months // 12)
month = (total_months % 12) + 1
day = min(now.day, calendar.monthrange(year, month)[1])
date_part = f"{year:04d}-{month:02d}-{day:02d}"
iso_expiry = f"{date_part}T23:59:59Z"
print(f"{date_part}|{iso_expiry}")
PY
}

CONFIG_EXPIRY_RAW=$(python3 - "$CONFIG_PATH" <<'PY'
import json, sys
with open(sys.argv[1], "r", encoding="utf-8") as fh:
    data = json.load(fh)
print(data.get("expiry_date_utc", ""))
PY
)

if [[ -n "$CONFIG_EXPIRY_RAW" ]]; then
  if [[ "$CONFIG_EXPIRY_RAW" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]]; then
    EXPIRY_DATE="$CONFIG_EXPIRY_RAW"
    EXPIRY_ISO="${CONFIG_EXPIRY_RAW}T23:59:59Z"
  else
    PARSED=$(python3 - "$CONFIG_EXPIRY_RAW" <<'PY'
import sys, datetime
value = sys.argv[1]
try:
    text = value
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    dt = datetime.datetime.fromisoformat(text)
    dt = dt.astimezone(datetime.timezone.utc)
    iso = dt.strftime("%Y-%m-%dT%H:%M:%SZ")
    date_part = dt.strftime("%Y-%m-%d")
    print(f"{iso}|{date_part}")
except ValueError:
    sys.exit(1)
PY
)
    if [[ $? -eq 0 ]]; then
      IFS='|' read -r EXPIRY_ISO EXPIRY_DATE <<< "$PARSED"
    else
      echo "warning: expiry_date_utc must be yyyy-MM-dd or ISO 8601 UTC (e.g. 2025-11-10T12:30:00Z). Ignoring config value." >&2
      CONFIG_EXPIRY_RAW=""
    fi
  fi
fi

if [[ -z "$CONFIG_EXPIRY_RAW" ]]; then
  IFS='|' read -r EXPIRY_DATE EXPIRY_ISO <<< "$(calc_expiry_date)"
fi

default_output_dir="$REPO_ROOT/Licensing/generated_licenses/trial"
mkdir -p "$default_output_dir"
default_output="${default_output_dir}/${EXPIRY_DATE}-license.lic"
OUTPUT_PATH="${2:-$default_output}"
OUTPUT_PATH="${OUTPUT_PATH/#\~/$HOME}"
OUTPUT_DIR="$(dirname "$OUTPUT_PATH")"
mkdir -p "$OUTPUT_DIR"

CONFIG_SUMMARY=$(python3 - "$CONFIG_PATH" <<'PY'
import json, sys
fields = [
    "keysPath",
    "companyName",
    "productName",
    "defaultUserName",
    "defaultUserEmail",
    "expiry_date_utc",
    "defaultExpiryMonths",
    "outputRoot"
]
with open(sys.argv[1], "r", encoding='utf-8') as fh:
    data = json.load(fh)
for field in fields:
    value = data.get(field)
    if value in (None, ""):
        value = "<empty>"
    print(f"{field}:{value}")
PY
)

echo
echo "Using the following fields from the config file:"
while IFS=':' read -r key value; do
  printf "  %-20s %s\n" "$key" "$value"
done <<< "$CONFIG_SUMMARY"
echo

COMMAND_TEXT=$(cat <<EOF
dotnet run --project "$GENERATOR_PROJECT" -- \\
  generate-license \\
  --config "$CONFIG_PATH" \\
  --keys "$KEY_PATH" \\
  --expires "$EXPIRY_ISO" \\
  --type trial \\
  --output "$OUTPUT_PATH"
EOF
)

echo
echo "Invoking LicenseGenerator with:"
printf '%s\n' "$COMMAND_TEXT"
echo

echo "Generating trial licence..."
env LICENSE_PASSPHRASE="$PASSPHRASE" \
  dotnet run --project "$GENERATOR_PROJECT" -- \
  generate-license \
  --config "$CONFIG_PATH" \
  --keys "$KEY_PATH" \
  --expires "$EXPIRY_ISO" \
  --type trial \
  --output "$OUTPUT_PATH" | sed '/^License created:/d'
echo "Trial licence written to $OUTPUT_PATH"
