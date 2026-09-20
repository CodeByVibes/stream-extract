#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$(dirname "$SCRIPT_DIR")")"
DIST_DIR="${DIST_DIR:-$ROOT_DIR/dist}"
PUBLISH_ROOT="${PUBLISH_ROOT:-$DIST_DIR/publish}"
STAGE_DIR="$PUBLISH_ROOT/streamextract"
ARCHIVE_PATH="${ARCHIVE_PATH:-$DIST_DIR/streamextract-linux-x64.tar.gz}"
APPIMAGE_PATH="${APPIMAGE_PATH:-$DIST_DIR/StreamExtract-x86_64.AppImage}"

source "$SCRIPT_DIR/tool-versions.env"

for command in curl sha256sum dotnet tar find python3 unsquashfs file gcc make strip ldd; do
    command -v "$command" >/dev/null || { echo "Required utility is missing: $command" >&2; exit 1; }
done

rm -rf "$PUBLISH_ROOT" "$ARCHIVE_PATH" "$APPIMAGE_PATH"
mkdir -p "$STAGE_DIR/tools" "$STAGE_DIR/licenses"

dotnet publish "$ROOT_DIR/StreamExtract.Cli/StreamExtract.Cli.csproj" \
    -c Release -r linux-x64 --self-contained true -o "$STAGE_DIR"
DESKTOP_STAGE_DIR="$STAGE_DIR/desktop"
dotnet publish "$ROOT_DIR/StreamExtract.Desktop/StreamExtract.Desktop.csproj" \
    -c Release -r linux-x64 --self-contained true -o "$DESKTOP_STAGE_DIR"

WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

download_verified() {
    local url="$1" expected="$2" destination="$3"
    curl --fail --location --silent --show-error --retry 3 "$url" -o "$destination"
    printf '%s  %s\n' "$expected" "$destination" | sha256sum --check --status
}

download_verified "$MKVTOOLNIX_URL" "$MKVTOOLNIX_SHA256" "$WORKDIR/mkvtoolnix.AppImage"
chmod 0755 "$WORKDIR/mkvtoolnix.AppImage"
(
    cd "$WORKDIR"
    ./mkvtoolnix.AppImage --appimage-extract >/dev/null
)
test -x "$WORKDIR/squashfs-root/usr/bin/mkvmerge"
test -x "$WORKDIR/squashfs-root/usr/bin/mkvextract"
MKVTOOLNIX_ROOT="$WORKDIR/squashfs-root"
MKVTOOLNIX_LIBS="$MKVTOOLNIX_ROOT/usr/lib"
MKVTOOLNIX_RUNTIME="$STAGE_DIR/tools/mkvtoolnix-runtime"

# Emit one absolute path per dependency that resolves inside the extracted
# tree. A dependency that resolves elsewhere is a host library and is skipped
# deliberately, never bundled. The surrounding `set -o pipefail` is what lets a
# per-tool `ldd` failure survive the trailing `| sort -u`.
resolve_bundled_closure() {
    local tool output dependency status
    for tool in "$@"; do
        status=0
        output="$(LD_LIBRARY_PATH="$MKVTOOLNIX_LIBS" ldd "$tool" 2>&1)" ||
            status=$?
        if (( status != 0 )); then
            printf 'ldd failed for %s\n' "$tool" >&2
            return 1
        fi
        if [[ "$output" == *"not found"* ]]; then
            printf 'Unresolved dependency for %s\n' "$tool" >&2
            return 1
        fi
        while IFS= read -r dependency; do
            [[ "$dependency" == "$MKVTOOLNIX_LIBS/"* ]] || continue
            printf '%s\n' "$dependency"
        done < <(printf '%s\n' "$output" |
            awk '/=>/ { print $3 } /^\t\// { print $1 }')
    done | sort -u
}

# Bundle only the two CLI tools and the libraries they load, instead of the
# whole extracted AppDir (GUI, Qt plugin directories, translations, icons).
mkdir -p "$MKVTOOLNIX_RUNTIME/usr/bin" "$MKVTOOLNIX_RUNTIME/usr/lib"
cp --preserve=mode \
    "$MKVTOOLNIX_ROOT/usr/bin/mkvmerge" \
    "$MKVTOOLNIX_ROOT/usr/bin/mkvextract" \
    "$MKVTOOLNIX_RUNTIME/usr/bin/"
if ! closure_output="$(resolve_bundled_closure \
        "$MKVTOOLNIX_ROOT/usr/bin/mkvmerge" \
        "$MKVTOOLNIX_ROOT/usr/bin/mkvextract")"; then
    printf 'Failed to resolve the MKVToolNix library closure\n' >&2
    exit 1
fi
mapfile -t closure < <(printf '%s\n' "$closure_output" | sed '/^$/d')
for library in "${closure[@]}"; do
    cp -L --preserve=mode "$library" "$MKVTOOLNIX_RUNTIME/usr/lib/"
done
(( ${#closure[@]} >= 15 )) || {
    printf 'Closure resolved only %s libraries; expected at least 15\n' \
        "${#closure[@]}" >&2
    exit 1
}
for canary in $MKVTOOLNIX_REQUIRED_LIBRARIES; do
    test -f "$MKVTOOLNIX_RUNTIME/usr/lib/$canary" || {
        printf 'Closure is missing %s\n' "$canary" >&2
        exit 1
    }
done
duplicates="$(printf '%s\n' "${closure[@]}" |
    xargs -n1 basename | sort | uniq -d)"
[[ -z "$duplicates" ]] || {
    printf 'Ambiguous library basenames: %s\n' "$duplicates" >&2
    exit 1
}
printf '%s\n' '#!/usr/bin/env bash' 'set -euo pipefail' \
    'DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"' \
    'export LD_LIBRARY_PATH="$DIR/mkvtoolnix-runtime/usr/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"' \
    'exec "$DIR/mkvtoolnix-runtime/usr/bin/mkvmerge" "$@"' > "$STAGE_DIR/tools/mkvmerge"
printf '%s\n' '#!/usr/bin/env bash' 'set -euo pipefail' \
    'DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"' \
    'export LD_LIBRARY_PATH="$DIR/mkvtoolnix-runtime/usr/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"' \
    'exec "$DIR/mkvtoolnix-runtime/usr/bin/mkvextract" "$@"' > "$STAGE_DIR/tools/mkvextract"
cp "$ROOT_DIR/licenses/MKVToolNix-LICENCE.txt" "$STAGE_DIR/licenses/"

CACHE_DIR="${XDG_CACHE_HOME:-$HOME/.cache}/streamextract"
CACHED_MP4BOX="$CACHE_DIR/MP4Box-$GPAC_VERSION-$GPAC_SOURCE_SHA256"

if [[ -x "${MP4BOX_OVERRIDE:-}" ]]; then
    echo "Using MP4Box from MP4BOX_OVERRIDE: $MP4BOX_OVERRIDE"
    cp "$MP4BOX_OVERRIDE" "$STAGE_DIR/tools/MP4Box"
elif [[ -x "$CACHED_MP4BOX" ]]; then
    echo "Using cached static MP4Box: $CACHED_MP4BOX"
    cp "$CACHED_MP4BOX" "$STAGE_DIR/tools/MP4Box"
else
    echo "Building static MP4Box from $GPAC_SOURCE_URL..."
    download_verified "$GPAC_SOURCE_URL" "$GPAC_SOURCE_SHA256" "$WORKDIR/gpac.tar.gz"
    mkdir -p "$WORKDIR/gpac"
    tar -xzf "$WORKDIR/gpac.tar.gz" -C "$WORKDIR/gpac" --strip-components=1
    (
        cd "$WORKDIR/gpac"
        ./configure --static-bin \
            --use-zlib=no --use-freetype=no --use-mad=no --use-a52=no --use-nghttp2=no \
            --use-jpeg=no --use-png=no --use-faad=no --use-xvid=no --use-ffmpeg=no \
            --use-vorbis=no --use-theora=no --use-ssl=no
        make -j"$(nproc)"
        strip --strip-all bin/gcc/MP4Box
    )
    mkdir -p "$CACHE_DIR"
    cp "$WORKDIR/gpac/bin/gcc/MP4Box" "$CACHED_MP4BOX"
    cp "$CACHED_MP4BOX" "$STAGE_DIR/tools/MP4Box"
fi
chmod 0755 "$STAGE_DIR/tools/MP4Box"
cp "$ROOT_DIR/licenses/GPAC-LICENSE.txt" "$STAGE_DIR/licenses/"

chmod 0755 "$STAGE_DIR/streamextract" "$STAGE_DIR/tools/mkvmerge" "$STAGE_DIR/tools/mkvextract" \
    "$STAGE_DIR/tools/MP4Box"
if find "$STAGE_DIR/tools" -type l -print -quit | grep -q .; then
    echo "Packaging produced a symlink; expected regular files only" >&2
    exit 1
fi

artifacts_file="$WORKDIR/artifacts.json"
printf '[' > "$artifacts_file"
artifact_first=true
while IFS= read -r file; do
    relative="${file#"$STAGE_DIR/"}"
    executable=false
    [[ -x "$file" ]] && executable=true
    hash="$(sha256sum "$file" | awk '{print $1}')"
    case "/$relative/" in
        "/tools/mkvmerge/"|"/tools/mkvextract/"|"/tools/MP4Box/") continue ;;
    esac
    test -f "$file" && test ! -L "$file" || { echo "Non-regular staged tool: $file" >&2; exit 1; }
    if [[ "$artifact_first" == false ]]; then printf ',' >> "$artifacts_file"; fi
    printf '{"path":"%s","version":"native","rid":"linux-x64","source":"packaged pinned artifact","sha256":"%s","executable":%s}' \
        "$relative" "$hash" "$executable" >> "$artifacts_file"
    artifact_first=false
done < <(find "$STAGE_DIR/tools" -type f -print | sort)
printf ']' >> "$artifacts_file"

merge_hash="$(sha256sum "$STAGE_DIR/tools/mkvmerge" | awk '{print $1}')"
extract_hash="$(sha256sum "$STAGE_DIR/tools/mkvextract" | awk '{print $1}')"
mp4box_hash="$(sha256sum "$STAGE_DIR/tools/MP4Box" | awk '{print $1}')"
python3 - "$ROOT_DIR/StreamExtract.Cli/tools-manifest.json" "$artifacts_file" "$STAGE_DIR/tools-manifest.json" \
    "$MKVTOOLNIX_VERSION" "$MKVTOOLNIX_URL" "$merge_hash" "$extract_hash" \
    "$GPAC_VERSION" "$GPAC_SOURCE_URL" "$mp4box_hash" <<'PY'
import json
import sys

template, artifacts_path, output, *values = sys.argv[1:]
manifest = json.load(open(template, encoding="utf-8"))
keys = ("__MKVTOOLNIX_VERSION__", "__MKVTOOLNIX_URL__", "__MKVMERGE_SHA256__",
        "__MKVEXTRACT_SHA256__", "__GPAC_VERSION__", "__GPAC_URL__", "__MP4BOX_SHA256__")
replacements = dict(zip(keys, values))
for tool in manifest["tools"]:
    for key, value in tool.items():
        if isinstance(value, str):
            tool[key] = replacements.get(value, value)
manifest["artifacts"] = json.load(open(artifacts_path, encoding="utf-8"))
if not manifest["artifacts"]:
    raise SystemExit("artifact manifest is empty")
logical = {"tools/" + tool["filename"] for tool in manifest["tools"] if tool["rid"] == "linux-x64"}
artifact_paths = {artifact["path"] for artifact in manifest["artifacts"]}
staged = set()
import os
for root, dirs, files in os.walk(os.path.join(os.path.dirname(output), "tools")):
    staged.update(os.path.relpath(os.path.join(root, name), os.path.dirname(output)).replace(os.sep, "/") for name in files)
if staged != logical | artifact_paths:
    raise SystemExit(f"manifest coverage mismatch: staged={sorted(staged)}, manifest={sorted(logical | artifact_paths)}")
json.dump(manifest, open(output, "w", encoding="utf-8"), indent=2)
open(output, "a", encoding="utf-8").write("\n")
PY

# The desktop app is published separately, so give it the same complete,
# verified native-tool bundle relative to its own AppContext.BaseDirectory.
cp "$STAGE_DIR/tools-manifest.json" "$DESKTOP_STAGE_DIR/tools-manifest.json"
cp -rL --preserve=mode,timestamps "$STAGE_DIR/tools" "$DESKTOP_STAGE_DIR/tools"
cp -rL --preserve=mode,timestamps "$STAGE_DIR/licenses" "$DESKTOP_STAGE_DIR/licenses"
chmod 0755 "$DESKTOP_STAGE_DIR/StreamExtract.Desktop"

tar -C "$PUBLISH_ROOT" --sort=name --mtime='UTC 1970-01-01' --owner=0 --group=0 --numeric-owner \
    -czf "$ARCHIVE_PATH" streamextract

APPDIR="$WORKDIR/StreamExtract.AppDir"
mkdir -p "$APPDIR/usr/bin"
cp "$ROOT_DIR/build/linux/AppRun" "$APPDIR/AppRun"
cp "$ROOT_DIR/build/linux/StreamExtract.desktop" "$APPDIR/StreamExtract.desktop"
cp "$ROOT_DIR/StreamExtract.Desktop/Assets/app_logo.png" "$APPDIR/StreamExtract.png"
cp -rL --preserve=mode,timestamps "$DESKTOP_STAGE_DIR"/* "$APPDIR/usr/bin/"
chmod 0755 "$APPDIR/AppRun" "$APPDIR/usr/bin/StreamExtract.Desktop"
download_verified "$APPIMAGETOOL_URL" "$APPIMAGETOOL_SHA256" "$WORKDIR/appimagetool.AppImage"
chmod 0755 "$WORKDIR/appimagetool.AppImage"
APPIMAGE_EXTRACT_AND_RUN=1 "$WORKDIR/appimagetool.AppImage" "$APPDIR" "$APPIMAGE_PATH"
chmod 0755 "$APPIMAGE_PATH"

echo "Archive created at $ARCHIVE_PATH"
echo "AppImage created at $APPIMAGE_PATH"
