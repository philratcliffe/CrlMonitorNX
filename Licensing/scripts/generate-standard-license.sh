#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
Usage: generate-standard-license.sh [OPTIONS]

Generates a standard machine-bound license using the shared LicenseGenerator.

OPTIONS:
  -c, --config PATH         Config path (default: Licensing/scripts/licensegenerator.config.json)
  -o, --output PATH         Output path (default: Licensing/generated_licenses/standard/<date>-<customer>.lic)
  -r, --request-code CODE   Machine request code (required)
  -n, --customer-name NAME  Customer/organization name (overrides config defaultUserName)
  -e, --email EMAIL         Customer email (overrides config defaultUserEmail)
  -x, --expires DATE        Expiry date yyyy-MM-dd or ISO 8601 UTC (overrides config)
  -h, --help                Show this help

ENVIRONMENT:
  LICENSE_PASSPHRASE        Private key passphrase (or enter interactively)
  LICENSE_KEYS_PATH         Override keys path from config

NOTES:
  - CrlMonitor licenses are machine-bound, not user-bound
  - Customer name is stored in the license "Name" field for reference
  - Request code binds the license to a specific machine
  - RedKestrel.Licensing must be checked out alongside this repo

EXAMPLES:
  # Generate license for Acme Corp expiring in 12 months
  ./generate-standard-license.sh \
    --request-code "ABC123..." \
    --customer-name "Acme Corporation" \
    --email "admin@acme.com"

  # Generate license expiring on specific date
  ./generate-standard-license.sh \
    --request-code "ABC123..." \
    --customer-name "Acme Corp" \
    --email "admin@acme.com" \
    --expires "2026-12-31"
EOF
}

REQUEST_CODE=""
CUSTOMER_NAME=""
CUSTOMER_EMAIL=""
CONFIG_PATH=""
OUTPUT_PATH=""
EXPIRY_OVERRIDE=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    -c|--config)
      CONFIG_PATH="$2"
      shift 2
      ;;
    -o|--output)
      OUTPUT_PATH="$2"
      shift 2
      ;;
    -r|--request-code)
      REQUEST_CODE="$2"
      shift 2
      ;;
    -n|--customer-name)
      CUSTOMER_NAME="$2"
      shift 2
      ;;
    -e|--email)
      CUSTOMER_EMAIL="$2"
      shift 2
      ;;
    -x|--expires)
      EXPIRY_OVERRIDE="$2"
      shift 2
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage
      exit 1
      ;;
  esac
done

if [[ -z "$REQUEST_CODE" ]]; then
  echo "Error: --request-code is required" >&2
  echo "Run CrlMonitor on the target machine to obtain the request code." >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

default_config="$SCRIPT_DIR/licensegenerator.config.json"
CONFIG_PATH="${CONFIG_PATH:-$default_config}"
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

# Get customer details from config if not provided
if [[ -z "$CUSTOMER_NAME" ]]; then
  CUSTOMER_NAME=$(python3 - "$CONFIG_PATH" <<'PY'
import json, sys
with open(sys.argv[1], "r", encoding="utf-8") as fh:
    data = json.load(fh)
print(data.get("defaultUserName", ""))
PY
)
  if [[ -z "$CUSTOMER_NAME" ]]; then
    echo "Error: --customer-name is required (or set defaultUserName in config)" >&2
    exit 1
  fi
fi

if [[ -z "$CUSTOMER_EMAIL" ]]; then
  CUSTOMER_EMAIL=$(python3 - "$CONFIG_PATH" <<'PY'
import json, sys
with open(sys.argv[1], "r", encoding="utf-8") as fh:
    data = json.load(fh)
print(data.get("defaultUserEmail", ""))
PY
)
  if [[ -z "$CUSTOMER_EMAIL" ]]; then
    echo "Error: --email is required (or set defaultUserEmail in config)" >&2
    exit 1
  fi
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

# Handle expiry date
CONFIG_EXPIRY_RAW="$EXPIRY_OVERRIDE"
if [[ -z "$CONFIG_EXPIRY_RAW" ]]; then
  CONFIG_EXPIRY_RAW=$(python3 - "$CONFIG_PATH" <<'PY'
import json, sys
with open(sys.argv[1], "r", encoding="utf-8") as fh:
    data = json.load(fh)
print(data.get("expiry_date_utc", ""))
PY
)
fi

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
      echo "Warning: expiry_date_utc must be yyyy-MM-dd or ISO 8601 UTC. Ignoring config value." >&2
      CONFIG_EXPIRY_RAW=""
    fi
  fi
fi

if [[ -z "$CONFIG_EXPIRY_RAW" ]]; then
  IFS='|' read -r EXPIRY_DATE EXPIRY_ISO <<< "$(calc_expiry_date)"
fi

# Sanitize customer name for filename
CUSTOMER_SLUG=$(echo "$CUSTOMER_NAME" | tr '[:upper:]' '[:lower:]' | sed 's/[^a-z0-9]/-/g' | sed 's/--*/-/g' | sed 's/^-//;s/-$//')

default_output_dir="$REPO_ROOT/Licensing/generated_licenses/standard"
mkdir -p "$default_output_dir"
default_output="${default_output_dir}/${EXPIRY_DATE}-${CUSTOMER_SLUG}.lic"
OUTPUT_PATH="${OUTPUT_PATH:-$default_output}"
OUTPUT_PATH="${OUTPUT_PATH/#\~/$HOME}"
OUTPUT_DIR="$(dirname "$OUTPUT_PATH")"
mkdir -p "$OUTPUT_DIR"

echo
echo "Generating standard license with:"
echo "  Customer:     $CUSTOMER_NAME"
echo "  Email:        $CUSTOMER_EMAIL"
echo "  Request code: ${REQUEST_CODE:0:20}..."
echo "  Expires:      $EXPIRY_ISO"
echo "  Output:       $OUTPUT_PATH"
echo

COMMAND_TEXT=$(cat <<EOF
dotnet run --project "$GENERATOR_PROJECT" -- \\
  generate-license \\
  --config "$CONFIG_PATH" \\
  --keys "$KEY_PATH" \\
  --expires "$EXPIRY_ISO" \\
  --type standard \\
  --request-code "$REQUEST_CODE" \\
  --user-name "$CUSTOMER_NAME" \\
  --user-email "$CUSTOMER_EMAIL" \\
  --output "$OUTPUT_PATH"
EOF
)

echo "Invoking LicenseGenerator with:"
printf '%s\n' "$COMMAND_TEXT"
echo

echo "Generating standard license..."
env LICENSE_PASSPHRASE="$PASSPHRASE" \
  dotnet run --project "$GENERATOR_PROJECT" -- \
  generate-license \
  --config "$CONFIG_PATH" \
  --keys "$KEY_PATH" \
  --expires "$EXPIRY_ISO" \
  --type standard \
  --request-code "$REQUEST_CODE" \
  --user-name "$CUSTOMER_NAME" \
  --user-email "$CUSTOMER_EMAIL" \
  --output "$OUTPUT_PATH" | sed '/^License created:/d'

if [[ $? -eq 0 ]]; then
  echo
  echo "✓ Standard license written to:"
  echo "  $OUTPUT_PATH"
  echo
  echo "Send this license file to:"
  echo "  Customer: $CUSTOMER_NAME"
  echo "  Email:    $CUSTOMER_EMAIL"
else
  echo "✗ License generation failed" >&2
  exit 1
fi
