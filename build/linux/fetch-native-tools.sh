#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" &> /dev/null && pwd)"
ROOT_DIR="$(dirname "$(dirname "$SCRIPT_DIR")")"
echo "Root Dir: $ROOT_DIR"

source "${SCRIPT_DIR}/tool-versions.env"

# 1. Publish CLI
echo "Publishing CLI..."
cd "$ROOT_DIR"
dotnet publish StreamExtract.Cli/StreamExtract.Cli.csproj -c Release -r linux-x64 --self-contained true -o /tmp/streamextract-publish
PUBLISH_DIR="/tmp/streamextract-publish"

TOOLS_DIR="$PUBLISH_DIR/tools"
LICENSES_DIR="$PUBLISH_DIR/licenses"
mkdir -p "$TOOLS_DIR" "$LICENSES_DIR"

# 2. Download and Extract native tools
WORKDIR=$(mktemp -d)
trap 'rm -rf "$WORKDIR"' EXIT

echo "Downloading MKVToolNix..."
curl -sL --fail "$MKVTOOLNIX_URL" -o "$WORKDIR/mkvtoolnix.AppImage"
echo "$MKVTOOLNIX_SHA256  $WORKDIR/mkvtoolnix.AppImage" | sha256sum -c -

echo "Preparing MKVToolNix..."
chmod +x "$WORKDIR/mkvtoolnix.AppImage"
cp "$WORKDIR/mkvtoolnix.AppImage" "$TOOLS_DIR/mkvmerge"
cp "$WORKDIR/mkvtoolnix.AppImage" "$TOOLS_DIR/mkvextract"

# Grab license
cp "$ROOT_DIR/licenses/MKVToolNix-LICENCE.txt" "$LICENSES_DIR/"

echo "Downloading GPAC..."
curl -sL --fail "$GPAC_URL" -o "$WORKDIR/gpac.deb"
echo "$GPAC_SHA256  $WORKDIR/gpac.deb" | sha256sum -c -

echo "Downloading libgpac..."
curl -sL --fail "$LIBGPAC_URL" -o "$WORKDIR/libgpac.deb"
echo "$LIBGPAC_SHA256  $WORKDIR/libgpac.deb" | sha256sum -c -

echo "Extracting GPAC and libgpac..."
cd "$WORKDIR"
ar -x gpac.deb
tar --zstd -xf data.tar.zst ./usr/bin/MP4Box || true
mv usr/bin/MP4Box "$TOOLS_DIR/MP4Box.bin"

ar -x libgpac.deb
tar --zstd -xf data.tar.zst ./usr/lib || true
find usr/lib -name "libgpac.so*" -exec cp -P {} "$TOOLS_DIR/" \; || true

# Create wrapper script for MP4Box
cat << 'EOF' > "$TOOLS_DIR/MP4Box"
#!/bin/bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" &> /dev/null && pwd)"
export LD_LIBRARY_PATH="$DIR${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
exec "$DIR/MP4Box.bin" "$@"
EOF

# Grab license
cp "$ROOT_DIR/licenses/GPAC-LICENSE.txt" "$LICENSES_DIR/"

chmod +x "$TOOLS_DIR"/*
chmod +x "$PUBLISH_DIR/streamextract"

# Compute final SHAs
MKVM_SHA=$(sha256sum "$TOOLS_DIR/mkvmerge" | awk '{print $1}')
MKVE_SHA=$(sha256sum "$TOOLS_DIR/mkvextract" | awk '{print $1}')
MP4B_SHA=$(sha256sum "$TOOLS_DIR/MP4Box" | awk '{print $1}')

# 3. Process tools-manifest.json
cd "$ROOT_DIR"
sed -e "s|__MKVTOOLNIX_VERSION__|$MKVTOOLNIX_VERSION|g" \
    -e "s|__MKVTOOLNIX_URL__|$MKVTOOLNIX_URL|g" \
    -e "s|__MKVMERGE_SHA256__|$MKVM_SHA|g" \
    -e "s|__MKVEXTRACT_SHA256__|$MKVE_SHA|g" \
    -e "s|__GPAC_VERSION__|$GPAC_VERSION|g" \
    -e "s|__GPAC_URL__|$GPAC_URL|g" \
    -e "s|__MP4BOX_SHA256__|$MP4B_SHA|g" \
    "StreamExtract.Cli/tools-manifest.json" > "$PUBLISH_DIR/tools-manifest.json"

# 4. Create the archive
echo "Creating archive..."
cd "$PUBLISH_DIR"
ARCHIVE_PATH="/tmp/streamextract-linux-x64.tar.gz"
tar -czf "$ARCHIVE_PATH" *
echo "Archive created at $ARCHIVE_PATH"
