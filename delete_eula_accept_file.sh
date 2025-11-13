#!/bin/bash
# Delete EULA acceptance file to force re-acceptance on next run

EULA_FILE="bin/Debug/net8.0/eula-acceptance.json"

if [ -f "$EULA_FILE" ]; then
    rm "$EULA_FILE"
    echo "Deleted: $EULA_FILE"
else
    echo "File not found: $EULA_FILE"
fi
