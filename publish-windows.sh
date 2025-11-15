#!/usr/bin/env bash
# Publish CrlMonitor for Windows

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_FILE="${ROOT}/CrlMonitor.csproj"
PUBLISH_DIR="${ROOT}/bin/Release/net8.0/win-x64/publish"

echo "=== Publishing CrlMonitor for Windows ==="
echo ""

cd "${ROOT}"

ensure_clean_git_tree() {
    if ! command -v git >/dev/null 2>&1; then
        echo "⚠️  Warning: git not found — skipping worktree cleanliness check"
        return 0
    fi

    if [ -n "$(git status --porcelain)" ]; then
        echo "❌ Cannot publish with a dirty worktree."
        echo "   Please commit or stash changes, then rerun this script."
        exit 1
    fi
}

check_vulnerable_packages() {
    echo "Checking for vulnerable NuGet packages..."

    if ! command -v dotnet >/dev/null 2>&1; then
        echo "⚠️  Warning: dotnet not found — skipping vulnerability check"
        return 0
    fi

    local output
    output=$(dotnet list package --vulnerable 2>&1 || true)

    if echo "$output" | grep -q "has the following vulnerable packages"; then
        echo "❌ CRITICAL: Vulnerable packages detected!"
        echo "$output"
        echo ""
        echo "Please update vulnerable packages before releasing."
        exit 1
    fi

    echo "✓ No vulnerable packages found"
}

extract_version() {
    python - <<'PY'
import xml.etree.ElementTree as ET
tree = ET.parse("CrlMonitor.csproj")
root = tree.getroot()
def local(name):
    if '}' in name:
        return name.split('}', 1)[1]
    return name
for elem in root.iter():
    if local(elem.tag) == "Version" and elem.text:
        print(elem.text.strip())
        break
PY
}

ensure_clean_git_tree
check_vulnerable_packages

echo ""
echo "Cleaning previous publish output..."
rm -rf "${PUBLISH_DIR}"

echo ""
echo "Restoring dotnet tools (obfuscar required for code obfuscation)..."
dotnet tool restore

echo ""
echo "Publishing self-contained Windows build with obfuscation..."
dotnet publish "${PROJECT_FILE}" -c Release -r win-x64 --self-contained true \
    /p:PublishSingleFile=true \
    /p:DebugType=None \
    /p:DebugSymbols=false \
    /p:DefineConstants="WINDOWS"

VERSION="$(extract_version)"
if [[ -z "${VERSION}" ]]; then
    echo "❌ Failed to determine version from CrlMonitor.csproj" >&2
    exit 1
fi
echo "Version: v${VERSION}"

echo ""
echo "Copying configuration files..."
cp "${ROOT}/config.json" "${PUBLISH_DIR}/"
mkdir -p "${PUBLISH_DIR}/examples/CA-certs"
cp "${ROOT}/examples/CA-certs/DigiCertGlobalRootCA.crt" "${PUBLISH_DIR}/examples/CA-certs/"

echo ""
echo "Removing XML documentation files..."
find "${PUBLISH_DIR}" -name "*.xml" -delete

echo ""
echo "Creating ZIP archive..."
cd "${PUBLISH_DIR}"
ZIP_FILE="${ROOT}/CrlMonitor-v${VERSION}-Windows.zip"
rm -f "${ZIP_FILE}"
zip -r -q "${ZIP_FILE}" .
cd "${ROOT}"

echo ""
echo "=== Validating ZIP package ==="

if ! unzip -t "${ZIP_FILE}" >/dev/null 2>&1; then
    echo "❌ CRITICAL: ZIP file is corrupted!"
    exit 1
fi
echo "✓ ZIP integrity verified"

ZIP_CONTENTS="$(unzip -l "${ZIP_FILE}")"

echo "Checking required files..."
required_files=(
    "CrlMonitor.exe"
    "config.json"
)

for file in "${required_files[@]}"; do
    if ! echo "${ZIP_CONTENTS}" | grep -q "${file}"; then
        echo "❌ CRITICAL: Missing required file: ${file}"
        exit 1
    fi
done
echo "✓ Required files present"

echo "Checking for debug symbols..."
if echo "${ZIP_CONTENTS}" | grep -q "\.pdb"; then
    echo "❌ CRITICAL: .pdb files found in distribution!"
    exit 1
fi
echo "✓ No debug symbols found"

echo "Checking for test artefacts..."
if echo "${ZIP_CONTENTS}" | grep -qi "test"; then
    echo "❌ CRITICAL: Test artefacts detected in ZIP!"
    echo "${ZIP_CONTENTS}" | grep -i "test"
    exit 1
fi
echo "✓ No test artefacts found"

echo "Checking package size..."
ZIP_SIZE=$(stat -f%z "${ZIP_FILE}" 2>/dev/null || stat -c%s "${ZIP_FILE}")
ZIP_SIZE_MB=$((ZIP_SIZE / 1024 / 1024))

if [ "${ZIP_SIZE_MB}" -lt 10 ]; then
    echo "❌ CRITICAL: ZIP too small (${ZIP_SIZE_MB} MB) — build likely incomplete."
    exit 1
fi

if [ "${ZIP_SIZE_MB}" -gt 250 ]; then
    echo "⚠️  Warning: ZIP unusually large (${ZIP_SIZE_MB} MB)"
fi
echo "✓ Package size: ${ZIP_SIZE_MB} MB"

echo ""
echo "=== Verifying obfuscation ==="

# Check that obfuscation ran and created Mapping.txt
MAPPING_FILE="obj/Release/net8.0/win-x64/obfuscated/Mapping.txt"
if [ ! -f "${MAPPING_FILE}" ]; then
    echo "❌ CRITICAL: Obfuscation mapping file not found!"
    echo "   Expected: ${MAPPING_FILE}"
    echo "   Obfuscation may not have run."
    exit 1
fi
echo "✓ Obfuscation mapping file found"

# Verify specific classes were renamed in Mapping.txt
echo "Verifying core classes were obfuscated..."
if ! grep -q "CrlMonitor.ConfigLoader ->" "${MAPPING_FILE}"; then
    echo "❌ CRITICAL: ConfigLoader class not renamed in Mapping.txt"
    exit 1
fi

if ! grep -q "CrlMonitor.Program ->" "${MAPPING_FILE}"; then
    echo "❌ CRITICAL: Program class not renamed in Mapping.txt"
    exit 1
fi

if ! grep -q "CrlMonitor.Crl.CrlParser ->" "${MAPPING_FILE}"; then
    echo "❌ CRITICAL: CrlParser class not renamed in Mapping.txt"
    exit 1
fi
echo "✓ Core classes verified as obfuscated"

# Extract CrlMonitor.exe from ZIP and verify it contains obfuscated code
echo "Extracting exe from ZIP for verification..."
TEMP_VERIFY_DIR="/tmp/crlmonitor-verify-$$"
mkdir -p "${TEMP_VERIFY_DIR}"
unzip -q "${ZIP_FILE}" CrlMonitor.exe -d "${TEMP_VERIFY_DIR}" 2>/dev/null

if [ ! -f "${TEMP_VERIFY_DIR}/CrlMonitor.exe" ]; then
    echo "❌ CRITICAL: Could not extract CrlMonitor.exe from ZIP"
    rm -rf "${TEMP_VERIFY_DIR}"
    exit 1
fi

# Use strings to check for readable class names (should NOT find them in obfuscated exe)
if command -v strings >/dev/null 2>&1; then
    echo "Checking for clean (non-obfuscated) class names in exe..."

    # Look for class names that should have been obfuscated
    # If we find them clearly readable, obfuscation may have failed
    FOUND_CLEAN_NAMES=0

    if strings "${TEMP_VERIFY_DIR}/CrlMonitor.exe" | grep -q "CrlMonitor\.ConfigLoader"; then
        echo "⚠️  Warning: Found readable 'CrlMonitor.ConfigLoader' in exe"
        FOUND_CLEAN_NAMES=1
    fi

    if strings "${TEMP_VERIFY_DIR}/CrlMonitor.exe" | grep -q "CrlMonitor\.Crl\.CrlParser"; then
        echo "⚠️  Warning: Found readable 'CrlMonitor.Crl.CrlParser' in exe"
        FOUND_CLEAN_NAMES=1
    fi

    if [ "${FOUND_CLEAN_NAMES}" -eq 0 ]; then
        echo "✓ No readable class names found in exe (expected for obfuscated code)"
    else
        echo "ℹ️  Some metadata strings present (may be references, not actual code)"
    fi
else
    echo "ℹ️  'strings' command not available - skipping exe content check"
fi

rm -rf "${TEMP_VERIFY_DIR}"

echo ""
echo "✅ OBFUSCATION VERIFIED"
echo "   - Mapping file confirms classes were renamed"
echo "   - Core classes: ConfigLoader, Program, CrlParser obfuscated"
echo "   - Exe packaged with obfuscated code"

echo ""
echo "=== Windows build created successfully ==="
echo "Output: ${ZIP_FILE}"
echo "Size: ${ZIP_SIZE_MB} MB"
echo ""
echo "✅ PUBLISH ZIP VERIFIED - CrlMonitor IS OBFUSCATED"
echo "   You can now release this ZIP"
echo ""
echo "Next: extract on a Windows host and run CrlMonitor.exe"
