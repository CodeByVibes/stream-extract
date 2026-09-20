#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$(dirname "$SCRIPT_DIR")")"
DIST_DIR="${DIST_DIR:-$ROOT_DIR/dist}"
PUBLISH_ROOT="${PUBLISH_ROOT:-$DIST_DIR/publish}"
FIXTURE_DIR="${FIXTURE_DIR:-$DIST_DIR/fixtures}"
MP4BOX="${MP4BOX:-$PUBLISH_ROOT/streamextract/tools/MP4Box}"
MKVMERGE="${MKVMERGE:-$PUBLISH_ROOT/streamextract/tools/mkvmerge}"
SOURCE_SUBTITLE="$ROOT_DIR/tests/fixtures/smoke.srt"
MP4_FIXTURE="$FIXTURE_DIR/sample.mp4"
MKV_FIXTURE="$FIXTURE_DIR/sample.mkv"
RICH_FIXTURE="$FIXTURE_DIR/sample-rich.mkv"

test -x "$MP4BOX" || { echo "Missing executable MP4Box: $MP4BOX" >&2; exit 1; }
test -x "$MKVMERGE" || { echo "Missing executable mkvmerge: $MKVMERGE" >&2; exit 1; }
test -f "$SOURCE_SUBTITLE" || { echo "Missing tracked fixture source: $SOURCE_SUBTITLE" >&2; exit 1; }

rm -rf "$FIXTURE_DIR"
mkdir -p "$FIXTURE_DIR"

"$MP4BOX" -add "$SOURCE_SUBTITLE" -new "$MP4_FIXTURE" >&2
"$MKVMERGE" --output "$MKV_FIXTURE" "$MP4_FIXTURE" >&2

# The plain MKV has neither attachments nor chapters, so the attachment and
# chapter extraction modes need a richer fixture.
printf 'fixture attachment\n' > "$FIXTURE_DIR/attachment.txt"
printf 'CHAPTER01=00:00:00.000\nCHAPTER01NAME=Intro\n' \
    > "$FIXTURE_DIR/chapters.txt"
"$MKVMERGE" --output "$RICH_FIXTURE" \
    --attach-file "$FIXTURE_DIR/attachment.txt" \
    --chapters "$FIXTURE_DIR/chapters.txt" \
    "$MKV_FIXTURE" >&2

test -s "$MP4_FIXTURE"
test -s "$MKV_FIXTURE"
test -s "$RICH_FIXTURE"
printf '%s\n%s\n%s\n' "$MP4_FIXTURE" "$MKV_FIXTURE" "$RICH_FIXTURE"
