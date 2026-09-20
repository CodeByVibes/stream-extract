# StreamExtract

> A desktop and CLI tool for extracting tracks, chapters, attachments, tags,
> cue sheets, and timestamps from MKV and MP4 files.

StreamExtract provides a Windows desktop GUI (WinForms), a cross-platform
Avalonia desktop app, and a command-line interface built with .NET 10. It wraps
[MKVToolNix](https://mkvtoolnix.download/) (`mkvmerge`, `mkvextract`) and
[GPAC](https://wiki.gpac.io/) (`mp4box`) to extract individual streams and
metadata from media containers. The application starts the bundled native tools
directly without shell interpretation; the Linux archive uses local launchers to
set each tool's bundled library path.

## Features

- Extract audio, video, and subtitle tracks from MKV/MKA and MP4/M4V/M4A/M4B
  files
- Extract chapters from MKV and MP4 containers, and attachments, tags, cue
  sheets, timestamp files, and per-track cue sheets from MKV containers
- Drag-and-drop multiple files or pick them with the file dialog
- Per-file track and feature selection with a checkbox tree
- Sequential, progress-tracked extraction
- Output paths are validated and contained — untrusted attachment names cannot
  escape the output directory
- Startup validation that fails closed when a bundled tool is missing or does
  not match its expected SHA-256 hash
- Checks for updates on startup and shows a button linking to the download
  page when a newer version is available

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build
  from source
- Windows (for the WinForms GUI; it targets `net10.0-windows`)
- Linux (`linux-x64` for the CLI and desktop app; requires no separate .NET
  or native-tool installation)

The native tools are bundled (Windows tools are in `tools/`, Linux tools are in
the release archive) and required at runtime:

- `mkvextract` / `mkvmerge` — from [MKVToolNix](https://mkvtoolnix.download/)
- `MP4Box` — from [GPAC](https://wiki.gpac.io/)

## Installation

### Windows GUI from source

No installer is provided. To run from source:

```bash
git clone https://github.com/CodeByVibes/stream-extract.git
cd stream-extract
dotnet run --project stream-extract-winforms.csproj
```

To build a release:

```bash
dotnet build -c Release
```

The build copies `tools/` and `licenses/` into the output directory. Launch the
app from there, or run the produced `stream-extract-winforms.exe`.

### Linux desktop AppImage

Download `StreamExtract-x86_64.AppImage`, make it executable, and launch it:

```bash
chmod +x StreamExtract-x86_64.AppImage
./StreamExtract-x86_64.AppImage
```

The AppImage is self-contained and includes the desktop application, bundled
native tools, and their runtime libraries. To build it yourself, see
[Linux Packaging & Smoke Tests](#linux-packaging--smoke-tests); the result is
written to `dist/StreamExtract-x86_64.AppImage`.

### Linux CLI and portable desktop bundle

Download the `linux-x64` release archive
(`streamextract-linux-x64.tar.gz`) and extract it:

```bash
tar -xzf streamextract-linux-x64.tar.gz
cd streamextract
```

The Linux release archive is self-contained for the supported `linux-x64`
environment. It includes the CLI, verified `mkvmerge`, `mkvextract`, and
`MP4Box` Linux binaries, and the MKVToolNix library closure those two tools
load. You do not need to install `.NET` or the native tools separately; the host
still supplies the Linux kernel and dynamic loader.
The release packaging process securely downloads pinned native tool versions,
verifies their SHA-256 checksums, and bundles them.

Packaging builds a fully static `MP4Box` from the pinned GPAC source release, so
the bundled binary has no dynamic library dependencies of its own. Only the
MKVToolNix CLI library closure is bundled (`tools/mkvtoolnix-runtime/`); the
MKVToolNix GUI and its Qt plugins are not. There is no `tools/lib/` directory.
The archive still depends on the host Linux kernel and dynamic loader;
portability is limited by the loader and kernel ABI supported by the build
environment.

`tools-manifest.json` records and validates every bundled file — including each
MKVToolNix runtime closure file — before the CLI or desktop app processes media.

The Linux archive includes the self-contained Avalonia desktop app under
`desktop/`. Launch `desktop/StreamExtract.Desktop`; its `tools/` directory is
copied beside it so importing media works without relying on the current
working directory or system-installed MKVToolNix. Releases contain both this
portable archive and the one-file AppImage.

> [!NOTE]
> If you delete or relocate a bundled tool — or replace it with one whose
> SHA-256 hash does not match — the app refuses to start and reports the
> problem. Keep the `tools/` folder next to the executable and leave the
> bundled executables untouched.

## Usage

### Windows GUI

1. **Add files** — drag media files onto the tree, or click **Open files**.
2. **Select output folder** — choose a folder, or keep **Use source** checked
   to write next to the first imported file.
3. **Select what to extract** — check the tracks and features you want under
   each file. Checking a file node checks all of its children.
4. **Extract** — click **Extract**. Progress is shown on the progress bar and
   in the log pane.

The tree is a working set: select a top-level file entry and press
<kbd>Delete</kbd> to remove it from the list. Only the list entry is removed —
the media file on disk is never touched.

### Linux desktop

The Avalonia desktop app follows the same workflow as the Windows GUI:

1. **Open files** — drag media files onto the file tree, or click **Open files**.
2. **Select output folder** — clear **Use source directory** and pick a folder,
   or keep it checked to write next to the first imported file.
3. **Select what to extract** — check the tracks and features you want under
   each file.
4. **Extract** — click **Extract**; **Cancel** stops an in-progress run.

The file tree is a working set: select a top-level file entry and press
<kbd>Delete</kbd> to remove it from the list. Only the list entry is removed —
the media file on disk is never touched.

### Linux CLI

The CLI provides full feature parity with the GUI:

```bash
# Print media information; the listed IDs are the values --tracks accepts
./streamextract info movie.mkv

# Extract specific tracks and chapters
./streamextract extract movie.mkv --tracks 1,2 --chapters --output ./out

# Extract every feature from MKV files (see --all below for the MP4 caveat)
./streamextract extract movie1.mkv movie2.mkv --all --output ./out

# Extract cue sheets, per-track cue sheets, and timestamps
./streamextract extract concert.mkv --cue-sheets \
  --cues-for-selected-tracks --timestamps --output ./out
```

#### Options

- `--tracks <ids>` — Comma-separated track IDs to extract, as reported by
  `info`.
- `--chapters` — Extract chapters.
- `--attachments` — Extract attachments (MKV only).
- `--tags` — Extract tags (MKV only).
- `--cue-sheets` — Extract the container-level cue sheet (MKV only).
- `--cues-for-selected-tracks` — Write one cue sheet per selected track (MKV
  only). Selects every track when `--tracks` is omitted.
- `--timestamps` — Write a timestamp file per selected track (MKV only).
  Selects every track when `--tracks` is omitted.
- `--all` — Extract every feature above. Cannot be combined with another
  extraction option, and fails with a usage error on containers that do not
  support all of them (MP4 supports tracks and chapters only).
- `--output <dir>` — Write output to `dir`, creating it when missing. Defaults
  to the current directory.
- `--verbose` — Print per-file progress even when stdout is redirected.
- `--help`, `-h` — Print usage.
- `--version` — Print the CLI version.

Unknown track IDs, unsupported extraction modes, and malformed input are
reported as usage errors (exit code `2`).

#### Exit Codes

| Code | Meaning | Description |
| :---: | :--- | :--- |
| `0` | Success | Command completed successfully. |
| `1` | Extraction Error | Native tool failure or extraction error occurred. |
| `2` | Usage Error | Invalid syntax, missing input file, or invalid track ID. |
| `130` | Cancelled | Process was interrupted/cancelled (e.g. via `Ctrl+C`). |

Supported extraction options per file type:

| Option | MKV/MKA | MP4/M4V/M4A/M4B |
| --- | :---: | :---: |
| Tracks (audio/video/subtitles) | yes | yes |
| Chapters | yes | yes |
| Attachments | yes | no |
| Tags | yes | no |
| Cue sheets | yes | no |
| Cues for selected tracks | yes | no |
| Timestamps | yes | no |

Output naming follows the source file name: `movie.mkv` yields
`movie_Track1.h264` for a video track, `movie_chapters.xml` for chapters,
`movie_tags.xml` for tags, `movie_cuesheet.cue` for the container cue sheet,
`movie_Track1_timestamps.txt` for track timestamps, and
`movie_Track1_cues.cue` for per-track cue sheets. The number in `_Track<n>` is
the track ID plus one (`Track1` is track ID `0`), matching the track labels the
GUIs show. Attachments keep their original names.

## Development

### Building

```bash
dotnet build
```

The build is warning-free.

### Testing

`dotnet test` runs the whole solution, which includes the Windows-only WinForms
test project and therefore requires Windows. On Linux, run the portable test
project directly:

```bash
dotnet test tests/StreamExtract.Tests/StreamExtract.Tests.csproj
```

The portable tests are headless xUnit tests targeting the pure helpers — path
containment, request building, cue sheet generation, progress math, plugin
command builders, CLI parsing and exit codes, terminal formatting, file drop
parsing, native tool resolution and integrity verification, update parsing, and
process failure/cancellation contracts. The bundled native tools are never
invoked during tests; the process contracts are exercised against the
cross-platform .NET `TestProcessHost` fixture. The checkbox-tree selection
snapshot (`BuildFileSelection`) is covered by the Windows-only
`StreamExtract.WinForms.Tests` project.

### Linux Packaging & Smoke Tests

To build the Linux AppImage and portable release archive and run the smoke tests
locally, run these from the repository root:

```bash
# Publish the CLI and desktop app with pinned native tools, then package the
# portable archive and AppImage into dist/
build/linux/fetch-native-tools.sh

# Generate test fixtures (sample.mp4, sample.mkv, sample-rich.mkv) and run the
# bundle smoke tests
build/linux/generate-fixtures.sh
SMOKE_FIXTURES="dist/fixtures/sample.mp4
dist/fixtures/sample.mkv
dist/fixtures/sample-rich.mkv" build/linux/smoke-test.sh
```

All output lands in the git-ignored `dist/` directory:

| Path | Contents |
| --- | --- |
| `dist/publish/streamextract/` | Staged CLI and `desktop/` bundle |
| `dist/streamextract-linux-x64.tar.gz` | Portable release archive |
| `dist/StreamExtract-x86_64.AppImage` | One-file desktop AppImage |
| `dist/fixtures/` | Generated smoke-test media |

Set `DIST_DIR` to relocate everything at once, or `PUBLISH_ROOT`,
`ARCHIVE_PATH`, `APPIMAGE_PATH`, and `FIXTURE_DIR` to relocate individual
paths.

`fetch-native-tools.sh` downloads the pinned MKVToolNix AppImage and GPAC source
release, verifies their SHA-256 checksums, builds a static `MP4Box`, and stages
the bundle. The smoke test requires at least one MKV and one MP4 fixture;
`sample-rich.mkv` carries an attachment and chapters, so include it to exercise
the attachment, chapter, and tag modes. Set `SMOKE_FIXTURES` to a
newline-delimited list of fixtures, or `SMOKE_MKV_FIXTURE` / `SMOKE_MP4_FIXTURE`
for a single file each.

### Windows Packaging

`build/windows/publish.ps1` publishes the Avalonia desktop app for Windows and
stages the native tool bundle next to it. It runs on Windows with PowerShell 7
and, for cross-publishing, on Linux or macOS with `pwsh`:

```powershell
# Self-contained folder bundle -> dist/publish-win-x64/StreamExtract
pwsh -File build/windows/publish.ps1

# One executable, with everything but tools/ and licenses/ bundled inside
pwsh -File build/windows/publish.ps1 -SingleFile
```

The Avalonia project does not copy the native tools itself, so the script stages
`tools-manifest.json`, `tools/`, and `licenses/` next to the executable. The app
verifies those tools against the manifest at startup; without them it launches,
but every extraction fails. The tools are not downloaded — the ones committed in
`tools/` are verified against the SHA-256 hashes in `tools-manifest.json` first,
so a modified binary fails the build.

| Path | Contents |
| --- | --- |
| `dist/publish-win-x64/StreamExtract/` | Self-contained folder bundle |
| `dist/StreamExtract-win-x64.zip` | Archive of the folder bundle |
| `dist/publish-win-x64-singlefile/StreamExtract/` | Single-executable bundle |
| `dist/StreamExtract-win-x64-singlefile.zip` | Archive of the single-executable bundle |

The single-file flavor folds the runtime, the managed assemblies, and the native
libraries into one `.exe` and drops the symbol files that come with the native
packages. It extracts those native libraries into a cache directory on first
launch (`%TEMP%\.net\...`, overridable with `DOTNET_BUNDLE_EXTRACT_BASE_DIR`), so
its target needs a writable temporary directory; the folder bundle extracts
nothing. Both flavors default to a self-contained `win-x64` release; pass
`-Configuration`, `-RuntimeIdentifier`, `-OutputDirectory`, `-FrameworkDependent`
or `-SkipArchive` to change that, and `Get-Help build/windows/publish.ps1 -Full`
for the details.

## Architecture

The codebase is organized into four production projects, with the tests in
`tests/`:

- **`StreamExtract.Core`** (`net10.0`) — Platform-neutral core containing media
  container models, extractor plugins (`MkvExtractorPlugin`, `Mp4ExtractorPlugin`),
  process runners (`ProcessRunner` with process-tree cancellation), output path
  validation (`OutputPathGuard`), native tool resolution & integrity verification
  (`NativeToolValidator`, `NativeToolResolver`), request builders
  (`ExtractionRequestBuilder`), and update checking.
- **`StreamExtract.Cli`** (`net10.0`) — Standalone, self-contained cross-platform
  command-line interface handling argument parsing, terminal formatting, real-time
  extraction progress reporting, signal cancellation (`SIGINT`), and standardized
  exit codes.
- **`stream-extract-winforms`** (`net10.0-windows`) — Windows desktop WinForms GUI
  application for visual file selection, checkbox tree configuration, and progress
  logging.
- **`StreamExtract.Desktop`** (`net10.0`) — Cross-platform Avalonia desktop GUI
  (MVVM via CommunityToolkit.Mvvm) that shares `StreamExtract.Core` with the other
  front ends. Packaged for Linux as the AppImage and the archive's `desktop/`
  bundle; also configured for `win-x64`.

Key design decisions:

- A non-zero exit code from a native tool is captured as a per-mode failure in the
  returned `ExtractOutcome`; the UI logs each failure and keeps going with the
  remaining modes instead of a false "Done". Cancellation still aborts everything.
- Attachment output paths pass through `OutputPathGuard.ResolveContainedPath`,
  so a malicious file name inside an MKV cannot escape the selected output
  directory.
- One `mkvextract` invocation per extraction mode keeps failures attributable
  and matches the tool's command syntax. The exception is per-track cue sheets,
  which are generated from the extracted chapter XML instead of calling
  `mkvextract`'s cuesheet mode (that mode has no per-track option).
- At startup the app verifies the SHA-256 hash of every bundled tool and
  refuses to start if one is missing, modified, or reached through a symlink or
  reparse-point path beneath the application directory. Bundled artifact files
  are checked the same way.

## Contributing

This project uses [Conventional Commits](https://www.conventionalcommits.org/)
for commit messages. Keep the build warning-free and run the tests before
submitting a pull request: `dotnet test` on Windows, or
`dotnet test tests/StreamExtract.Tests/StreamExtract.Tests.csproj` on Linux.

## License

This project is licensed under the [MIT License](LICENSE). The bundled native
tools are the property of their respective authors and are distributed under
their original licenses — see `licenses/GPAC-LICENSE.txt` and
`licenses/MKVToolNix-LICENCE.txt`.
