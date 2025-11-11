#!/usr/bin/env bash
set -euo pipefail

echo "Removing all eula-acceptance.json files..."
find . -name "eula-acceptance.json" -delete
echo "Done. Run 'dotnet run' again to see the EULA."
