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

echo "Publishing self-contained Windows build..."
dotnet publish "${PROJECT_FILE}" -c Release -r win-x64 --self-contained true \
    /p:PublishSingleFile=true \
    /p:DebugType=None \
    /p:DebugSymbols=false

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
echo "=== Windows build created successfully ==="
echo "Output: ${ZIP_FILE}"
echo "Size: ${ZIP_SIZE_MB} MB"
echo "Next: extract on a Windows host and run CrlMonitor.exe"
