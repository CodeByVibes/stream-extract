# StreamExtract

> A desktop and CLI tool for extracting tracks, chapters, attachments, tags,
> cue sheets, and timestamps from MKV and MP4 files.

StreamExtract provides a Windows desktop GUI (WinForms) and a Linux command-line
interface built with .NET 10. It wraps [MKVToolNix](https://mkvtoolnix.download/)
(`mkvmerge`, `mkvextract`) and [GPAC](https://wiki.gpac.io/) (`mp4box`) to
extract individual streams and metadata from media containers. The application
starts the bundled native tools directly without shell interpretation; the Linux
archive uses local launchers to set each tool's bundled library path.

## Features

- Extract audio, video, and subtitle tracks from MKV/MKA and MP4/M4V/M4A/M4B
  files
- Extract chapters, attachments, tags, cue sheets, timestamp files, and
  per-track cue sheets from MKV containers
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
- Windows (the app is `net10.0-windows` and uses WinForms)
- Linux (`linux-x64` for the CLI and desktop app; requires no separate .NET or native-tool installation)

The native tools are bundled (Windows tools are in `tools/`, Linux tools are in the release archive) and required at runtime:

- `mkvextract` / `mkvmerge` — from [MKVToolNix](https://mkvtoolnix.download/)
- `MP4Box` — from [GPAC](https://wiki.gpac.io/)

## Installation

### Windows GUI

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


### Linux desktop

Download `StreamExtract-x86_64.AppImage`, make it executable, and launch it:

```bash
chmod +x StreamExtract-x86_64.AppImage
./StreamExtract-x86_64.AppImage
```

The AppImage is self-contained and includes the desktop application, bundled
native tools, and their runtime libraries.

### Linux CLI and portable desktop bundle

Download the `linux-x64` release archive (`streamextract-linux-x64.tar.gz`) and extract it:

```bash
tar -xzf streamextract-linux-x64.tar.gz
cd streamextract
```

The Linux release archive is self-contained for the supported `linux-x64`
environment. It includes the CLI, verified `mkvmerge`, `mkvextract`, and
`MP4Box` Linux binaries, and the runtime libraries required by those binaries.
You do not need to install `.NET` or the native tools separately; the host
still supplies the Linux kernel and dynamic loader.
The release packaging process securely downloads pinned native tool versions, verifies their SHA-256 checksums, and bundles them.

Packaging recursively verifies MP4Box's ELF dependency closure and includes only
non-system libraries needed by the bundled binary. The archive still depends on
the host Linux kernel and dynamic loader; portability is limited by the loader
and kernel ABI supported by the build environment.

The archive also contains `tools/mkvtoolnix-runtime/` and `tools/lib/`, which hold regular-file copies of the native runtime dependencies. `tools-manifest.json` records and validates every bundled file before the CLI or desktop app processes media.

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


### Linux CLI

The CLI provides full feature parity with the GUI:

```bash
# Print media information
./streamextract info <file>

# Extract specific tracks and chapters
./streamextract extract movie.mkv --tracks 1,2 --chapters --output ./out

# Extract all supported features from multiple files
./streamextract extract movie1.mkv movie2.mp4 --all --output ./out

# Extract cue sheets, per-track cue sheets, and timestamps
./streamextract extract concert.mkv --cue-sheets --cues-for-selected-tracks --timestamps --output ./out
```

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

Output naming follows the source file name. For example, extracting a video
track from `movie.mkv` writes `movie_Track1.h264` into the output folder;
chapters write `movie_chapters.xml`; timestamps write
`movie_Track1_timestamps.txt`; cue sheets for selected tracks write
`movie_Track1_cues.cue`; attachments keep their original names.

## Development

### Building

```bash
dotnet build
```

The build is warning-free.

### Testing

On Linux, run the portable test project directly:

```bash
dotnet test tests/StreamExtract.Tests/StreamExtract.Tests.csproj
```

The aggregate solution test command includes the Windows-only WinForms test
project and therefore requires Windows. The portable tests are headless xUnit
tests targeting the pure helpers — path containment, request building,
selection snapshotting, cue sheet generation, progress math, plugin command
builders, update parsing, and process failure/cancellation contracts. The
bundled native tools are never invoked during tests; the process contracts are
exercised against the cross-platform .NET `TestProcessHost` fixture.

### Linux Packaging & Smoke Tests

To package the self-contained Linux release archive and run the smoke tests locally:

```bash
# Package the CLI with pinned native tools
build/linux/fetch-native-tools.sh

# Generate test fixtures and run bundle smoke tests
build/linux/generate-fixtures.sh
SMOKE_FIXTURES="/tmp/streamextract-fixtures/sample.mp4
/tmp/streamextract-fixtures/sample.mkv" build/linux/smoke-test.sh
```

## Architecture

The codebase is organized into three projects:

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
for commit messages. Please keep the build warning-free and run `dotnet test`
before submitting a pull request.

## License

This project is licensed under the [MIT License](LICENSE). The bundled native
tools are the property of their respective authors and are distributed under
their original licenses — see `licenses/GPAC-LICENSE.txt` and
`licenses/MKVToolNix-LICENCE.txt`.
