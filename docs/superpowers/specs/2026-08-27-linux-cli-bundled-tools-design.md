# Linux CLI with Bundled Native Tools

## Overview

Add a Linux command-line version of StreamExtract before undertaking a cross-platform graphical UI rewrite. The first Linux release targets `linux-x64` as a portable archive and provides the same extraction capabilities currently available in the WinForms application.

The CLI will share a platform-neutral extraction core with the existing Windows GUI. Release packaging will download pinned Linux builds of MKVToolNix and GPAC, verify their checksums, set executable permissions, and include them in the archive. Native tool binaries will not be committed to the repository.

## Goals

- Provide a usable Linux `linux-x64` CLI release.
- Preserve the existing Windows WinForms application.
- Share models, plugins, command construction, process execution, path validation, and extraction behavior between GUI and CLI.
- Support all existing extraction features: media inspection, tracks, chapters, attachments, tags, CUE sheets, per-track CUE sheets, and timestamp files.
- Bundle verified Linux versions of `mkvmerge`, `mkvextract`, and `MP4Box` in release archives.
- Make CLI behavior scriptable through explicit arguments and meaningful exit codes.
- Run core and CLI tests on Linux without requiring WinForms or Windows shell commands.

## Non-goals

- Replacing the WinForms UI in this phase.
- Adding a Linux graphical UI.
- Supporting Linux ARM64 or distribution-specific packages in the first release.
- Committing third-party Linux binaries to Git.
- Building MKVToolNix or GPAC from source as part of the application build.
- Reproducing the GUI checkbox tree in the CLI.

## Project Boundaries

The solution will be split into three application projects and the existing test project:

```text
StreamExtract.Core/
  Models/
  Plugins/
  Services/
  StreamExtract.Core.csproj

StreamExtract.Cli/
  Program.cs
  Commands/
  Formatting/
  StreamExtract.Cli.csproj

stream-extract-winforms.csproj
tests/StreamExtract.Tests/
```

`StreamExtract.Core` targets `net10.0` and contains no WinForms or Windows-only references. It owns the models, extractor plugin contracts and implementations, process runner, output path guard, extraction request construction, reusable update types, and tool-resolution/integrity services.

The existing WinForms project remains targeted at `net10.0-windows`, retains the UI, and references `StreamExtract.Core`. WinForms startup, dialogs, controls, resource loading, and UI-specific exception reporting remain outside the core.

`StreamExtract.Cli` targets `net10.0`, references `StreamExtract.Core`, and contains command-line parsing, terminal output, cancellation wiring, and process exit-code policy. It must not reference WinForms or the Windows GUI project.

## Tool Resolution and Integrity

Tool lookup will be centralized behind a shared resolver. Callers use logical tool identifiers rather than hard-coded filenames:

| Logical tool | Linux filename | Windows filename |
| --- | --- | --- |
| MKV merge/analyze | `mkvmerge` | `mkvmerge.exe` |
| MKV extraction | `mkvextract` | `mkvextract.exe` |
| MP4 extraction/analyze | `MP4Box` | `mp4box.exe` |

The first Linux CLI release resolves tools relative to its installation directory and uses bundled tools by default. It does not require users to install tools separately or configure `PATH`. No alternate tool-directory option is included in the first release; development and diagnostics use the same archive layout as production.

Integrity data will be represented by a platform-specific manifest shipped with the release. Each entry records the logical tool name, filename, tool version, target platform/runtime, upstream artifact or release identifier, and SHA-256 checksum.

Startup validates that all required tools exist, match the manifest, and are executable. Missing, mismatched, or unusable tools produce a clear CLI error and a nonzero exit code. The Windows GUI continues to validate its Windows bundle using the same shared validation behavior after the core split.

## Release Bundle

The Linux publish directory and archive will have this layout:

```text
streamextract/
  streamextract
  tools/
    mkvmerge
    mkvextract
    MP4Box
    mkvtoolnix-runtime/   # mkvmerge, mkvextract, and their resolved library closure
  licenses/
    GPAC-LICENSE.txt
    MKVToolNix-LICENCE.txt
  tools-manifest.json     # every regular file under tools/ is listed
```

The release workflow will:

1. Restore and build the core and CLI.
2. Publish the CLI self-contained for `linux-x64`.
3. Download pinned Linux artifacts for MKVToolNix and GPAC.
4. Verify every downloaded artifact against a committed checksum or manifest source.
5. Extract only the required native tools and associated licenses.
6. Place them under `tools/` using the expected logical filenames.
7. Set executable permissions on the native tools and CLI launcher.
8. Build or verify a standalone static MP4Box binary with zero dynamic library dependencies.
9. Run archive smoke tests against the bundled tools and representative MKV and
   MP4 fixtures, including real MP4Box media-info operation.
10. Create a portable `.tar.gz` release archive and upload it to published
    GitHub releases while retaining a workflow artifact for manual runs.

The workflow must fail if a download, checksum verification, extraction step, permission step, or smoke test fails. Tool versions and URLs must be pinned rather than resolved from a mutable latest-release endpoint.

## CLI Interface

The initial command surface is:

```text
streamextract info <file>
streamextract extract <file> [options]
streamextract --help
streamextract --version
```

The `info` command analyzes a supported media file and prints container, track, chapter, attachment, and tag information in a readable terminal format.

The `extract` command supports one or more input files and explicit selection options. The first release uses the following option syntax:

```text
streamextract extract movie.mkv --tracks 1,2 --chapters --output ./out
streamextract extract movie.mkv --all --output ./out
```

The supported extraction options are track IDs, chapters, attachments, tags, CUE sheets, CUE sheets for selected tracks, timestamps, `--all`, output directory, and verbose diagnostics.

Input files with unsupported extensions, malformed selections, invalid output directories, or no selected extraction modes are argument errors and return a usage-related nonzero exit code. Extraction failures are reported per file and per mode; the CLI continues with remaining independent work where the existing core contract permits it, then returns a nonzero exit code if any work failed. Cancellation via `Ctrl+C` cancels active work, terminates the native process tree, and returns a distinct cancellation exit code.

Progress output should be concise and terminal-friendly. It must not depend on carriage-return behavior for correctness, and verbose mode should expose native-tool diagnostics without corrupting the primary result or exit status.

## Core Adaptations

The existing plugins already contain the extraction command logic and should be moved with minimal behavioral change. Their native tool calls will use logical tool identifiers resolved by the shared tool resolver.

`ProcessRunner` remains based on `System.Diagnostics.Process`, `ProcessStartInfo.ArgumentList`, redirected output, cancellation, and process-tree termination. Error messages should use platform-neutral terminology. The runner must continue to avoid shell invocation and pass each argument separately.

`OutputPathGuard` will be reviewed during the split for cross-platform behavior. It must continue to prevent attachment names and generated paths from escaping the selected output directory. Windows device-name and trailing-dot protections may remain as defense-in-depth, but path comparison and symlink/reparse-point handling must not assume that every filesystem is Windows or case-insensitive.

Update checking is not a CLI startup requirement. If retained in the shared layer, it must remain opt-in and must not cause an offline CLI invocation to fail. The existing GUI can continue to perform its update check independently.

## Testing

Tests will be reorganized so platform-neutral tests can execute on Linux:

- Core unit tests target `net10.0` and cover path containment, request construction, plugin command builders, parsing, progress math, update parsing, and failure contracts.
- CLI tests cover argument parsing, selection mapping, help/version output, invalid input, exit codes, output formatting, and cancellation behavior.
- Process integration tests use a cross-platform test helper rather than `cmd.exe`, Windows shell syntax, `nul`, or `Environment.SystemDirectory`.
- Linux package smoke tests invoke the bundled `mkvmerge`, `mkvextract`, and `MP4Box` from a published archive against small media fixtures where practical.
- Existing Windows GUI tests, if any remain UI-dependent, stay explicitly Windows-only.

The current `ProcessRunnerTests` must be replaced or abstracted because they directly invoke `cmd.exe`, use `/c`, `exit /b`, `ping -n`, and `nul`. The production process runner should be tested on Linux with equivalent behavior rather than equivalent shell syntax.

## Error and Security Requirements

- Never invoke a shell for media-tool execution.
- Pass input and output paths as separate process arguments.
- Reject missing, modified, non-executable, or wrong-platform bundled tools before processing input media.
- Preserve output-directory containment for every generated file and attachment.
- Do not allow an invalid input file or extraction mode to produce a success exit code.
- Keep native-tool diagnostic output bounded as in the current process runner.
- Treat cancellation separately from extraction failure.
- Do not expose mutable download URLs in the release workflow without checksum verification.

## Implementation Sequence

1. Create `StreamExtract.Core` and move platform-neutral source with minimal behavior changes.
2. Retarget the moved core and tests to `net10.0`; keep the WinForms shell Windows-targeted.
3. Introduce logical tool identifiers, platform-specific filenames, manifests, and shared validation.
4. Update the WinForms project to consume the core without changing its user-facing behavior.
5. Add `StreamExtract.Cli` with `info`, `extract`, help, version, progress, cancellation, and exit-code handling.
6. Replace Windows-only process tests with cross-platform test fixtures.
7. Add Linux tool download, checksum verification, permissions, publish, archive, and smoke-test automation.
8. Publish a `linux-x64` archive and verify it on a clean Linux environment.

## Acceptance Criteria

- The core and CLI build on Linux with the installed .NET 10 SDK.
- The existing Windows GUI remains buildable for `net10.0-windows`.
- A clean `linux-x64` archive contains a self-contained CLI, all three required executable native tools, licenses, and a tool manifest.
- The archive runs without separately installed MKVToolNix, GPAC, or .NET runtime dependencies.
- `info` successfully analyzes representative MKV and MP4 files.
- `extract` supports every existing extraction mode and reports partial failures accurately.
- `Ctrl+C` stops active extraction and does not leave the native process running.
- Modified or missing bundled tools are rejected before processing input media.
- Core, CLI, and Linux process tests pass without WinForms or Windows shell dependencies.
- No Linux native binaries are committed to the repository.
- Archive smoke tests accept newline-delimited fixture paths without unsafe
  whitespace splitting and reject symlink/reparse-point parents for bundled
  tools and artifacts.
- The archive's portability assumes a compatible host Linux kernel and dynamic
  loader; it does not bundle arbitrary system files.
