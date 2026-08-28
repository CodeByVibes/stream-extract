#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$(dirname "$SCRIPT_DIR")")"
PUBLISH_ROOT="${PUBLISH_ROOT:-/tmp/streamextract-publish}"
STAGE_DIR="$PUBLISH_ROOT/streamextract"
ARCHIVE_PATH="${ARCHIVE_PATH:-/tmp/streamextract-linux-x64.tar.gz}"

source "$SCRIPT_DIR/tool-versions.env"

for command in curl sha256sum dotnet ar tar zstd find ldd readelf python3 unsquashfs file; do
    command -v "$command" >/dev/null || { echo "Required utility is missing: $command" >&2; exit 1; }
done

rm -rf "$PUBLISH_ROOT" "$ARCHIVE_PATH"
mkdir -p "$STAGE_DIR/tools/lib" "$STAGE_DIR/licenses"

dotnet publish "$ROOT_DIR/StreamExtract.Cli/StreamExtract.Cli.csproj" \
    -c Release -r linux-x64 --self-contained true -o "$STAGE_DIR"

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
# Dereference AppImage links so the archive has no filesystem dependencies outside itself.
cp -rL --preserve=mode,timestamps "$WORKDIR/squashfs-root" "$STAGE_DIR/tools/mkvtoolnix-runtime"
cp "$WORKDIR/mkvtoolnix.AppImage" "$STAGE_DIR/tools/mkvtoolnix.AppImage"
printf '%s\n' '#!/usr/bin/env bash' 'set -euo pipefail' \
    'DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"' \
    'export LD_LIBRARY_PATH="$DIR/mkvtoolnix-runtime/usr/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"' \
    'exec "$DIR/mkvtoolnix-runtime/usr/bin/mkvmerge" "$@"' > "$STAGE_DIR/tools/mkvmerge"
printf '%s\n' '#!/usr/bin/env bash' 'set -euo pipefail' \
    'DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"' \
    'export LD_LIBRARY_PATH="$DIR/mkvtoolnix-runtime/usr/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"' \
    'exec "$DIR/mkvtoolnix-runtime/usr/bin/mkvextract" "$@"' > "$STAGE_DIR/tools/mkvextract"
cp "$ROOT_DIR/licenses/MKVToolNix-LICENCE.txt" "$STAGE_DIR/licenses/"

download_verified "$GPAC_URL" "$GPAC_SHA256" "$WORKDIR/gpac.deb"
download_verified "$LIBGPAC_URL" "$LIBGPAC_SHA256" "$WORKDIR/libgpac.deb"
mkdir -p "$WORKDIR/gpac" "$WORKDIR/libgpac"
(
    cd "$WORKDIR/gpac"
    ar -x "$WORKDIR/gpac.deb"
)
(
    cd "$WORKDIR/libgpac"
    ar -x "$WORKDIR/libgpac.deb"
)
tar --zstd -xf "$WORKDIR/gpac/data.tar.zst" -C "$WORKDIR/gpac" ./usr/bin/MP4Box
tar --zstd -xf "$WORKDIR/libgpac/data.tar.zst" -C "$WORKDIR/libgpac" ./usr/lib
test -x "$WORKDIR/gpac/usr/bin/MP4Box"
cp "$WORKDIR/gpac/usr/bin/MP4Box" "$STAGE_DIR/tools/MP4Box.bin"
mapfile -t packaged_gpac_libraries < <(find "$WORKDIR/libgpac/usr/lib" -type f -name 'libgpac.so*' -print | sort)
test "${#packaged_gpac_libraries[@]}" -gt 0
for library in "${packaged_gpac_libraries[@]}"; do
    cp -L "$library" "$STAGE_DIR/tools/lib/$(basename "$library")"
done
gpac_library="${packaged_gpac_libraries[0]}"
soname="$(readelf -d "$STAGE_DIR/tools/lib/$(basename "$gpac_library")" | awk -F'[][]' '/SONAME/ { print $2; exit }')"
test -n "$soname"
if [[ "$soname" != "$(basename "$gpac_library")" ]]; then
    cp -L "$STAGE_DIR/tools/lib/$(basename "$gpac_library")" "$STAGE_DIR/tools/lib/$soname"
fi

resolve_elf_dependencies() {
    local pending=("$1") index=0 elf dependency resolved destination ldd_output ldd_status
    declare -A visited=()
    while (( index < ${#pending[@]} )); do
        elf="${pending[index++]}"
        [[ -n "${visited[$elf]:-}" ]] && continue
        visited["$elf"]=1
        if ldd_output="$(LD_LIBRARY_PATH="$STAGE_DIR/tools/lib" ldd "$elf" 2>&1)"; then
            ldd_status=0
        else
            ldd_status=$?
        fi
        if (( ldd_status != 0 )); then
            echo "ldd failed for $elf (status $ldd_status): $ldd_output" >&2
            return 1
        fi
        while IFS= read -r dependency; do
            [[ -n "$dependency" ]] || continue
            if [[ "$dependency" == *"not found"* ]]; then
                echo "Unresolved MP4Box dependency for $elf: $dependency" >&2
                return 1
            fi
            resolved="${dependency##*=> }"
            resolved="${resolved%% (*}"
            [[ "$resolved" == /* && -f "$resolved" ]] || continue
            if [[ "$resolved" == /lib/* || "$resolved" == /usr/lib/* ||
                  "$resolved" == /lib64/* || "$resolved" == /usr/lib64/* ]]; then
                continue
            fi
            destination="$STAGE_DIR/tools/lib/$(basename "$resolved")"
            [[ -f "$destination" ]] || cp -L "$resolved" "$destination"
            pending+=("$destination")
        done <<< "$ldd_output"
    done
}
resolve_elf_dependencies "$STAGE_DIR/tools/MP4Box.bin"
printf '%s\n' '#!/usr/bin/env bash' 'set -euo pipefail' \
    'DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"' \
    'export LD_LIBRARY_PATH="$DIR/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"' \
    'exec "$DIR/MP4Box.bin" "$@"' > "$STAGE_DIR/tools/MP4Box"
cp "$ROOT_DIR/licenses/GPAC-LICENSE.txt" "$STAGE_DIR/licenses/"

chmod 0755 "$STAGE_DIR/streamextract" "$STAGE_DIR/tools/mkvmerge" "$STAGE_DIR/tools/mkvextract" \
    "$STAGE_DIR/tools/MP4Box" "$STAGE_DIR/tools/MP4Box.bin" "$STAGE_DIR/tools/mkvtoolnix.AppImage"
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
    "$GPAC_VERSION" "$GPAC_URL" "$mp4box_hash" <<'PY'
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

tar -C "$PUBLISH_ROOT" --sort=name --mtime='UTC 1970-01-01' --owner=0 --group=0 --numeric-owner \
    -czf "$ARCHIVE_PATH" streamextract
echo "Archive created at $ARCHIVE_PATH"
