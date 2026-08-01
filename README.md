# StreamExtract

> A Windows desktop app for extracting tracks, chapters, attachments, tags,
> cue sheets, and timestamps from MKV and MP4 files.

StreamExtract is a WinForms application built with .NET 10. It wraps
[MKVToolNix](https://mkvtoolnix.download/) (`mkvmerge`, `mkvextract`) and
[GPAC](https://wiki.gpac.io/) (`mp4box`) to extract individual streams and
metadata from media containers. All extraction runs the bundled native tools
directly — no shell, no scripting.

## Features

- Extract audio, video, and subtitle tracks from MKV/MKA and MP4/M4V/M4A/M4B
  files
- Extract chapters, attachments, tags, cue sheets, and timestamp files from
  MKV containers
- Drag-and-drop multiple files or pick them with the file dialog
- Per-file track and feature selection with a checkbox tree
- Sequential, progress-tracked extraction (one native tool invocation per
  mode)
- Output paths are validated and contained — untrusted attachment names cannot
  escape the output directory
- Startup validation that fails closed when a bundled tool is missing

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build
  from source
- Windows (the app is `net10.0-windows` and uses WinForms)

The three native tools are committed in `tools/` and are required at runtime:

- `tools/mkvextract.exe` and `tools/mkvmerge.exe` — from
  [MKVToolNix](https://mkvtoolnix.download/)
- `tools/mp4box.exe` — from [GPAC](https://wiki.gpac.io/)

## Installation

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

> [!NOTE]
> If you delete or relocate a bundled tool, the app refuses to start and lists
> the missing executables with their expected paths. Keep the `tools/` folder
> next to the executable.

## Usage

1. **Add files** — drag media files onto the tree, or click **Open files**.
2. **Select output folder** — choose a folder, or keep **Use source** checked
   to write next to the first imported file.
3. **Select what to extract** — check the tracks and features you want under
   each file. Checking a file node checks all of its children.
4. **Extract** — click **Extract**. Progress is shown on the progress bar and
   in the log pane.

Supported extraction options per file type:

| Option | MKV/MKA | MP4/M4V/M4A/M4B |
| --- | :---: | :---: |
| Tracks (audio/video/subtitles) | yes | yes |
| Chapters | yes | yes |
| Attachments | yes | no |
| Tags | yes | no |
| Cue sheets | yes | no |
| Timestamps | yes | no |

Output naming follows the source file name. For example, extracting a video
track from `movie.mkv` writes `movie_Track1.h264` into the output folder;
chapters write `movie_chapters.xml`; attachments keep their original names.

## Development

### Building

```bash
dotnet build
```

The build is warning-free.

### Testing

```bash
dotnet test
```

Tests are headless xUnit tests targeting the pure helpers — path containment,
request building, plugin command builders, and the process failure/cancellation
contracts. No native tools are invoked during tests.

## Architecture

The solution is split into three layers:

- **`Form1`** — WinForms UI. Import and extraction run as async methods on the
  UI thread (WinForms `SynchronizationContext`). Selection is snapshotted from
  `TreeNode.Tag` values into immutable `ImportedFile`/`FileSelection` records.
- **`Plugins/`** — `IExtractorPlugin` implementations (`MkvExtractorPlugin`,
  `Mp4ExtractorPlugin`) that analyze files and build per-mode native-tool
  commands.
- **`Services/`** — `IProcessRunner`/`ProcessRunner` (process execution with
  kill-on-cancel and throw-on-non-zero-exit), `OutputPathGuard` (path
  containment), `ExtractionRequestBuilder` (selection to request mapping), and
  `UpdateChecker`/`BrowserLauncher`.

Key design decisions:

- A non-zero exit code from a native tool throws `ExternalToolException`; the
  UI reports partial failures instead of a false "Done".
- Attachment output paths pass through `OutputPathGuard.ResolveContainedPath`,
  so a malicious file name inside an MKV cannot escape the selected output
  directory.
- One `mkvextract` invocation per extraction mode keeps failures attributable
  and matches the tool's command syntax.

## Contributing

This project uses [Conventional Commits](https://www.conventionalcommits.org/)
for commit messages. Please keep the build warning-free and run `dotnet test`
before submitting a pull request.

## License

This project is licensed under the [MIT License](LICENSE). The bundled native
tools are the property of their respective authors and are distributed under
their original licenses — see `licenses/GPAC-LICENSE.txt` and
`licenses/MKVToolNix-LICENCE.txt`.
