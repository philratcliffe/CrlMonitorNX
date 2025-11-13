#!/bin/bash
# Clear trial storage to reset 30-day trial period

# Hash is uppercase 8 chars: D3A9A070
find ~ -name ".data_D3A9A070" -type f -exec rm -v {} \; 2>/dev/null
echo "Trial storage cleared. Next run will show 30 days."
