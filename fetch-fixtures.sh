#!/usr/bin/env bash
# fetch-fixtures.sh — Download EELS test fixtures for Scrutor
# Source: ethereum/execution-specs  Release: tests@v20.0.1

set -euo pipefail

FIXTURES_URL="https://github.com/ethereum/execution-specs/releases/download/tests%40v20.0.1/fixtures.tar.gz"
FIXTURES_DIR="$(cd "$(dirname "$0")" && pwd)/fixtures"
TARBALL="$FIXTURES_DIR/fixtures.tar.gz"

echo "==> Fetching EELS fixtures tests@v20.0.1"
echo "    Repo: ethereum/execution-specs"
echo "    Dest: $FIXTURES_DIR"
echo ""

mkdir -p "$FIXTURES_DIR"

curl -L --retry 3 --progress-bar "$FIXTURES_URL" -o "$TARBALL"

echo ""
echo "==> Extracting..."
tar -xzf "$TARBALL" -C "$FIXTURES_DIR"
rm "$TARBALL"

echo ""
echo "==> Done. Contents:"
ls -1 "$FIXTURES_DIR"
