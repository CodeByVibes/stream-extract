#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$(dirname "$SCRIPT_DIR")")"
DIST_DIR="${DIST_DIR:-$ROOT_DIR/dist}"
ARCHIVE="${ARCHIVE_PATH:-$DIST_DIR/streamextract-linux-x64.tar.gz}"
APPIMAGE="${APPIMAGE_PATH:-$DIST_DIR/StreamExtract-x86_64.AppImage}"
TEST_DIR="$(mktemp -d)"
trap 'rm -rf "$TEST_DIR"' EXIT

test -f "$ARCHIVE"
test -x "$APPIMAGE"
tar -tzf "$ARCHIVE" > "$TEST_DIR/archive.list"
grep -Fx 'streamextract/' "$TEST_DIR/archive.list"
for path in streamextract/streamextract streamextract/tools/mkvmerge streamextract/tools/mkvextract \
    streamextract/tools/MP4Box streamextract/tools/mkvtoolnix.AppImage \
    streamextract/licenses/GPAC-LICENSE.txt streamextract/licenses/MKVToolNix-LICENCE.txt \
    streamextract/tools-manifest.json streamextract/desktop/tools/mkvmerge \
    streamextract/desktop/tools/mkvextract streamextract/desktop/tools/MP4Box \
    streamextract/desktop/tools-manifest.json; do
    grep -Fx "$path" "$TEST_DIR/archive.list"
done
if grep -Ev '^streamextract(/|$)' "$TEST_DIR/archive.list"; then
    echo "Archive contains files outside streamextract/" >&2
    exit 1
fi

tar -xzf "$ARCHIVE" -C "$TEST_DIR"
ROOT="$TEST_DIR/streamextract"
for path in streamextract tools/mkvmerge tools/mkvextract tools/MP4Box \
    desktop/StreamExtract.Desktop desktop/tools/mkvmerge desktop/tools/mkvextract desktop/tools/MP4Box; do
    test -x "$ROOT/$path" || { echo "Not executable: $path" >&2; exit 1; }
done
if find "$ROOT/tools" "$ROOT/desktop/tools" -type l -print -quit | grep -q .; then
    echo "Archive contains symlinks" >&2
    exit 1
fi

PATH="/usr/bin:/bin"
export PATH
"$ROOT/tools/mkvmerge" --version
"$ROOT/tools/mkvextract" --version
"$ROOT/tools/MP4Box" -version
"$ROOT/desktop/tools/mkvmerge" --version
"$ROOT/desktop/tools/mkvextract" --version
"$ROOT/desktop/tools/MP4Box" -version
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
if readelf -d "$ROOT/tools/MP4Box" 2>/dev/null | grep -q 'DYNAMIC'; then
    echo "Expected MP4Box to be a static binary, but found dynamic section" >&2
    exit 1
fi
check_ldd "$ROOT/tools/mkvtoolnix-runtime/usr/lib" "$ROOT/tools/mkvtoolnix-runtime/usr/bin/mkvmerge"
check_ldd "$ROOT/tools/mkvtoolnix-runtime/usr/lib" "$ROOT/tools/mkvtoolnix-runtime/usr/bin/mkvextract"
source "$SCRIPT_DIR/tool-versions.env"
for library in $MKVTOOLNIX_REQUIRED_LIBRARIES; do
    test -f "$ROOT/tools/mkvtoolnix-runtime/usr/lib/$library" || {
        echo "Missing required library: $library" >&2
        exit 1
    }
done
# The bundle carries the CLI closure only: no GUI binaries, no Qt plugin
# directories, no usr/share tree, and none of the libraries only the GUI loads.
for library in libwayland-client.so.0 libwayland-cursor.so.0 \
    libsystemd.so.0 libblkid.so.1 libmount.so.1; do
    test -e "$ROOT/tools/mkvtoolnix-runtime/usr/lib/$library" && {
        echo "Unexpected bundled library: $library" >&2
        exit 1
    }
done
for path in usr/bin/mkvtoolnix-gui usr/bin/mkvinfo usr/bin/mkvpropedit \
    usr/bin/platforms usr/bin/imageformats usr/bin/multimedia \
    usr/bin/iconengines usr/share AppRun .DirIcon mkvtoolnix-gui.desktop \
    mkvtoolnix-gui.png; do
    test -e "$ROOT/tools/mkvtoolnix-runtime/$path" && {
        echo "Unexpected bundled payload: $path" >&2
        exit 1
    }
done
bundled="$(find "$ROOT/tools/mkvtoolnix-runtime" -type f | wc -l)"
test "$bundled" -le 40 || {
    echo "Runtime tree grew to $bundled files; pruning regressed" >&2
    exit 1
}
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
has_rich=false
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
    if [[ "$(basename "$fixture")" == "sample-rich.mkv" ]]; then
        # Attachment, chapter, and tag extraction: these modes need a fixture
        # that actually carries an attachment and chapters.
        has_rich=true
        rich_output="$TEST_DIR/rich-$fixture_count"
        mkdir "$rich_output"
        "$ROOT/streamextract" extract "$fixture" \
            --attachments --chapters --tags --output "$rich_output"
        for mode in chapters tags attachment; do
            find "$rich_output" -type f -name "*$mode*" -size +0c -print -quit |
                grep -q . || {
                echo "No non-empty $mode output for $fixture" >&2
                exit 1
            }
        done
    fi
done
[[ "$has_mkv" == true ]] || { echo "Smoke test requires at least one MKV fixture" >&2; exit 1; }
[[ "$has_mp4" == true ]] || { echo "Smoke test requires at least one MP4 fixture" >&2; exit 1; }
[[ "$has_rich" == true ]] || echo "Note: sample-rich.mkv not supplied; attachment and chapter modes not exercised" >&2
APPIMAGE_DIR="$TEST_DIR/appimage"
mkdir "$APPIMAGE_DIR"
(
    cd "$APPIMAGE_DIR"
    APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGE" --appimage-extract >/dev/null
)
APPROOT="$APPIMAGE_DIR/squashfs-root"
for path in AppRun usr/bin/StreamExtract.Desktop usr/bin/tools/mkvmerge usr/bin/tools/mkvextract usr/bin/tools/MP4Box usr/bin/tools-manifest.json; do
    test -e "$APPROOT/$path" || { echo "Missing AppImage path: $path" >&2; exit 1; }
done
for path in AppRun usr/bin/StreamExtract.Desktop usr/bin/tools/mkvmerge usr/bin/tools/mkvextract usr/bin/tools/MP4Box; do
    test -x "$APPROOT/$path" || { echo "Not executable in AppImage: $path" >&2; exit 1; }
done
"$APPROOT/usr/bin/tools/mkvmerge" --version
"$APPROOT/usr/bin/tools/mkvextract" --version
"$APPROOT/usr/bin/tools/MP4Box" -version

if [[ -d "$ROOT/tools/lib" ]]; then
    for elf in "$ROOT/tools/lib"/*; do
        [[ -f "$elf" ]] || continue
        readelf -h "$elf" 2>/dev/null | grep -q 'ELF' || continue
        check_ldd "$ROOT/tools/lib" "$elf"
    done
fi
echo "Smoke test passed."
