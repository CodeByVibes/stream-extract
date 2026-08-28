#!/usr/bin/env bash
set -euo pipefail

ARCHIVE="${ARCHIVE_PATH:-/tmp/streamextract-linux-x64.tar.gz}"
TEST_DIR="$(mktemp -d)"
trap 'rm -rf "$TEST_DIR"' EXIT

test -f "$ARCHIVE"
tar -tzf "$ARCHIVE" > "$TEST_DIR/archive.list"
grep -Fx 'streamextract/' "$TEST_DIR/archive.list"
for path in streamextract/streamextract streamextract/tools/mkvmerge streamextract/tools/mkvextract \
    streamextract/tools/MP4Box streamextract/tools/MP4Box.bin streamextract/tools/mkvtoolnix.AppImage \
    streamextract/licenses/GPAC-LICENSE.txt streamextract/licenses/MKVToolNix-LICENCE.txt \
    streamextract/tools-manifest.json; do
    grep -Fx "$path" "$TEST_DIR/archive.list"
done
if grep -Ev '^streamextract(/|$)' "$TEST_DIR/archive.list"; then
    echo "Archive contains files outside streamextract/" >&2
    exit 1
fi

tar -xzf "$ARCHIVE" -C "$TEST_DIR"
ROOT="$TEST_DIR/streamextract"
for path in streamextract tools/mkvmerge tools/mkvextract tools/MP4Box; do
    test -x "$ROOT/$path" || { echo "Not executable: $path" >&2; exit 1; }
done
if find "$ROOT/tools" -type l -print -quit | grep -q .; then
    echo "Archive contains symlinks" >&2
    exit 1
fi

PATH="/usr/bin:/bin"
export PATH
"$ROOT/tools/mkvmerge" --version
"$ROOT/tools/mkvextract" --version
"$ROOT/tools/MP4Box" -version
check_ldd() {
    local library_path="$1" elf="$2" output status
    if output="$(LD_LIBRARY_PATH="$library_path" ldd "$elf" 2>&1)"; then
        status=0
    else
        status=$?
    fi
    (( status == 0 )) || { echo "ldd failed for $elf (status $status): $output" >&2; exit 1; }
    if [[ "$output" == *"not found"* ]]; then
        echo "Unresolved dependency for $elf: $output" >&2
        exit 1
    fi
}
check_ldd "$ROOT/tools/lib" "$ROOT/tools/MP4Box.bin"
check_ldd "$ROOT/tools/mkvtoolnix-runtime/usr/lib" "$ROOT/tools/mkvtoolnix-runtime/usr/bin/mkvmerge"
check_ldd "$ROOT/tools/mkvtoolnix-runtime/usr/lib" "$ROOT/tools/mkvtoolnix-runtime/usr/bin/mkvextract"
"$ROOT/streamextract" --help
"$ROOT/streamextract" --version

set +e
"$ROOT/streamextract" info "$TEST_DIR/missing.mp4"
status=$?
set -e
test "$status" -eq 2

fixtures=()
if [[ -n "${SMOKE_FIXTURES:-}" ]]; then
    while IFS= read -r fixture; do
        [[ -n "$fixture" ]] && fixtures+=("$fixture")
    done <<< "$SMOKE_FIXTURES"
fi
[[ -n "${SMOKE_MKV_FIXTURE:-}" ]] && fixtures+=("$SMOKE_MKV_FIXTURE")
[[ -n "${SMOKE_MP4_FIXTURE:-}" ]] && fixtures+=("$SMOKE_MP4_FIXTURE")
if [[ "${#fixtures[@]}" -eq 0 ]]; then
    echo "Set SMOKE_FIXTURES (newline-delimited), SMOKE_MKV_FIXTURE, or SMOKE_MP4_FIXTURE" >&2
    exit 1
fi
fixture_count=0
has_mkv=false
has_mp4=false
for fixture in "${fixtures[@]}"; do
    fixture="$(realpath "$fixture")"
    [[ -f "$fixture" ]] || { echo "Missing smoke fixture: $fixture" >&2; exit 1; }
    case "$fixture" in
        *.mkv) has_mkv=true ;;
        *.mp4) has_mp4=true ;;
        *) echo "Unsupported smoke fixture: $fixture" >&2; exit 1 ;;
    esac
    fixture_count=$((fixture_count + 1))
    "$ROOT/streamextract" info "$fixture"
    [[ "$fixture" == *.mp4 ]] && "$ROOT/tools/MP4Box" -info "$fixture" >/dev/null
    output="$TEST_DIR/output-$fixture_count"
    mkdir "$output"
    track_id=1
    [[ "$fixture" == *.mkv ]] && track_id=0
    "$ROOT/streamextract" extract "$fixture" --tracks "$track_id" --output "$output"
    find "$output" -type f -size +0c -print -quit | grep -q . || {
        echo "Extraction produced no non-empty output for $fixture" >&2
        exit 1
    }
done
[[ "$has_mkv" == true ]] || { echo "Smoke test requires at least one MKV fixture" >&2; exit 1; }
[[ "$has_mp4" == true ]] || { echo "Smoke test requires at least one MP4 fixture" >&2; exit 1; }
for elf in "$ROOT/tools/MP4Box.bin" "$ROOT/tools/lib"/*; do
    [[ -f "$elf" ]] || continue
    readelf -h "$elf" 2>/dev/null | grep -q 'ELF' || continue
    check_ldd "$ROOT/tools/lib" "$elf"
done
echo "Smoke test passed."
