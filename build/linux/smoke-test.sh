#!/bin/bash
set -euo pipefail
ARCHIVE="/tmp/streamextract-linux-x64.tar.gz"
TEST_DIR=$(mktemp -d)
trap 'rm -rf "$TEST_DIR"' EXIT

echo "Extracting archive to $TEST_DIR"
tar -xzf "$ARCHIVE" -C "$TEST_DIR"

echo "Verifying archive contents..."
for f in "streamextract" "tools/mkvmerge" "tools/mkvextract" "tools/MP4Box" "licenses/GPAC-LICENSE.txt" "licenses/MKVToolNix-LICENCE.txt" "tools-manifest.json"; do
    if [ ! -f "$TEST_DIR/$f" ]; then
        echo "Missing expected file: $f"
        exit 1
    fi
done

for f in "streamextract" "tools/mkvmerge" "tools/mkvextract" "tools/MP4Box"; do
    if [ ! -x "$TEST_DIR/$f" ]; then
        echo "File is not executable: $f"
        exit 1
    fi
done

cd "$TEST_DIR"

echo "Testing bundled tools native execution..."
PATH="/bin:/usr/bin" ./tools/mkvmerge --version
PATH="/bin:/usr/bin" ./tools/mkvextract --version
PATH="/bin:/usr/bin" ./tools/MP4Box -version

echo "Testing CLI --help..."
PATH="/bin:/usr/bin" ./streamextract --help

echo "Testing CLI --version..."
PATH="/bin:/usr/bin" ./streamextract --version

echo "Testing CLI info missing file..."
# Missing file should exit with 2 (usage error)
set +e
PATH="/bin:/usr/bin" ./streamextract info non_existent.mp4
INFO_EXIT=$?
set -e

if [ $INFO_EXIT -ne 2 ]; then
    echo "Expected exit code 2 for missing file, got $INFO_EXIT"
    exit 1
fi

echo "Smoke test passed."
