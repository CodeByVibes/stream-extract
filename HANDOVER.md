# StreamExtract Linux CLI Handover

<!-- markdownlint-disable MD013 -->

**Date:** 2026-08-28
**Repository:** `stream-extract`
**Branch:** `main`
**HEAD at handover start:** `900dbd7 build: package Linux CLI with verified tools`
**Worktree:** Clean before the current uncommitted Task 5 corrections; the corrections are intentionally not committed.

## Executive summary

StreamExtract now has a platform-neutral extraction core, a Linux-capable CLI, cross-platform process tests, and Linux packaging automation in progress. The core and CLI implementation is substantially complete. The remaining work is to finish and independently accept the Linux release packaging changes, then perform final cross-platform verification.

The current uncommitted changes are focused on Task 5 packaging hardening. They add deterministic fixture generation, recursive native dependency handling, exact runtime artifact manifests, symlink/reparse protection, stricter smoke tests, and release-asset upload configuration. They have been exercised locally, but the final independent acceptance review was interrupted and has not yet been completed.

Do not treat the current archive or Task 5 scripts as release-ready until the remaining review is completed and the packaging changes are committed.

## Committed milestones

The following work is committed on `main`:

- `48c5769 refactor: extract platform-neutral core`
  - Added `StreamExtract.Core` targeting `net10.0`.
  - Moved models, plugins, process services, output-path guarding, request construction, and update-checking code into the core.
  - Kept the WinForms application targeting `net10.0-windows`.
  - Split Windows-only tests into `tests/StreamExtract.WinForms.Tests`.
  - Moved pure `ProgressMath` logic into the core.

- `2d09b5c feat: validate platform-specific native tools`
  - Added logical native-tool identifiers and platform-aware resolution.
  - Added `tools-manifest.json` loading and SHA-256 validation.
  - Added path traversal, duplicate, metadata, RID, executable-bit, and symlink/reparse validation.
  - Updated plugins to consume resolver-provided executable paths.

- `c5bf072 test: run process coverage across platforms`
  - Added `TestProcessHost` under `tests/StreamExtract.Tests/TestProcessHost`.
  - Replaced Windows `cmd.exe` process tests with cross-platform .NET process tests.
  - Added active cancellation, timeout, descendant termination, stdout/stderr, exit-code, and argument-boundary coverage.

- `a0631f3 feat: implement full-parity Linux CLI`
  - Added `StreamExtract.Cli` targeting `net10.0`.
  - Added `info`, `extract`, `--help`, and `--version` commands.
  - Added track, chapter, attachment, tag, CUE, selected-track CUE, timestamp, `--all`, output, and verbose options.
  - Added usage, extraction failure, and cancellation exit codes.
  - Added CLI parser, formatter, integration, selection, unsupported-mode, input-validation, and progress tests.

- `900dbd7 build: package Linux CLI with verified tools`
  - Added the initial Linux packaging workflow and scripts.
  - This commit is the baseline that the current uncommitted Task 5 corrections improve.

The authoritative design and implementation documents are:

- `docs/superpowers/specs/2026-08-27-linux-cli-bundled-tools-design.md`
- `docs/superpowers/plans/2026-08-27-linux-cli-bundled-tools-implementation.md`

## Current uncommitted changes

`git status --short` currently reports these intended Task 5 files:

- `.github/workflows/linux-cli.yml`
- `README.md`
- `StreamExtract.Cli/tools-manifest.json`
- `StreamExtract.Core/Services/NativeToolManifest.cs`
- `StreamExtract.Core/Services/NativeToolValidator.cs`
- `build/linux/fetch-native-tools.sh`
- `build/linux/generate-fixtures.sh`
- `build/linux/smoke-test.sh`
- `build/linux/tool-versions.env`
- `docs/superpowers/specs/2026-08-27-linux-cli-bundled-tools-design.md`
- `tests/StreamExtract.Tests/NativeToolValidatorTests.cs`
- `tests/fixtures/smoke.srt`

These changes have not been staged or committed. Generated archives, extracted AppImage contents, `.deb` files, and publish output must remain outside Git.

## Architecture

```text
StreamExtract.Core
  Models
  Plugins
  Services

StreamExtract.Cli -> StreamExtract.Core
stream-extract-winforms -> StreamExtract.Core

StreamExtract.Tests -> StreamExtract.Core
StreamExtract.WinForms.Tests -> stream-extract-winforms
```

The core uses `System.Diagnostics.Process` and `ProcessStartInfo.ArgumentList`; media tools are never launched through a shell. Plugins receive an `INativeToolResolver` and use resolved absolute paths. The normal production path loads and validates a packaged manifest before constructing plugins.

The Windows GUI remains WinForms-only. The Linux CLI has no WinForms dependency.

## CLI behavior

The intended first-release interface is:

```bash
streamextract info <file>
streamextract extract <file...> [options]
streamextract --help
streamextract --version
```

Supported extraction options include:

- `--tracks <id[,id...]>`
- `--chapters`
- `--attachments`
- `--tags`
- `--cue-sheets`
- `--cues-for-selected-tracks`
- `--timestamps`
- `--all`
- `--output <directory>`
- `--verbose`

Exit-code policy:

- `0`: successful command
- `1`: tool or extraction failure
- `2`: usage/input/unsupported-mode/output-path failure
- `130`: cancellation

Explicit track IDs are validated against analyzed media. Unsupported modes are rejected before extraction. Missing or non-file inputs are usage errors. Output directories are checked before media analysis.

## Packaging design

The target release is a portable `linux-x64` archive with a top-level directory:

```text
streamextract/
  streamextract
  tools/
    mkvmerge
    mkvextract
    MP4Box
    MP4Box.bin
    mkvtoolnix.AppImage
    mkvtoolnix-runtime/
    lib/
  licenses/
    GPAC-LICENSE.txt
    MKVToolNix-LICENCE.txt
  tools-manifest.json
```

The current packaging flow:

1. Publish `StreamExtract.Cli` self-contained for `linux-x64`.
2. Download pinned MKVToolNix, GPAC, and `libgpac` artifacts.
3. Verify each downloaded artifact with SHA-256 before extraction.
4. Extract MKVToolNix runtime files and create launchers for `mkvmerge` and `mkvextract`.
5. Extract GPAC `MP4Box` and its shared-library dependency closure.
6. Create an `MP4Box` launcher with a package-local `LD_LIBRARY_PATH`.
7. Copy license files.
8. Generate the runtime artifact manifest with hashes for all regular files under `tools/`.
9. Validate exact staged-file/manifest coverage.
10. Archive under the `streamextract/` prefix using normalized tar metadata.

The package necessarily still depends on the host Linux kernel and dynamic loader. It is not a universal binary for every Linux distribution. It is intended for the tested `linux-x64` glibc environment.

## Verification already performed

The following results are recorded from the current implementation work:

- `bash -n build/linux/fetch-native-tools.sh build/linux/smoke-test.sh build/linux/generate-fixtures.sh`: passed.
- Portable tests: currently reported as `195 passed, 0 failed` after the latest validator additions. Rerun before committing.
- Solution release build: reported as `0 warnings, 0 errors` after the latest changes. Rerun before committing.
- `build/linux/fetch-native-tools.sh`: successfully downloaded pinned artifacts and created `/tmp/streamextract-linux-x64.tar.gz`.
- `build/linux/generate-fixtures.sh`: successfully generated `/tmp/streamextract-fixtures/sample.mp4` and `/tmp/streamextract-fixtures/sample.mkv` from tracked `tests/fixtures/smoke.srt`.
- Smoke test with both generated fixtures: passed.
- Smoke test exercises bundled `mkvmerge`, `mkvextract`, `MP4Box`, CLI help/version, missing-file exit code `2`, `info` for MP4 and MKV, and extraction with non-empty output.
- Generated manifest audit was reported as exact: `294` manifest artifact paths and `294` packaged regular tool files, with no missing entries, extras, symlinks, or non-regular entries. Rerun independently.
- No generated Linux binaries or archives should be tracked.

The aggregate `dotnet test` command cannot execute the Windows test assembly on this Linux host because `Microsoft.WindowsDesktop.App 10.0` is unavailable. The portable test project must be run directly on Linux. The Windows test project must be run on Windows.

## Remaining work

### 1. Complete Task 5 acceptance

The current uncommitted packaging changes still need a final independent review. Confirm:

- `ldd` failure status cannot be hidden by command substitutions, pipelines, or process substitutions.
- Recursive dependency checking covers `MP4Box.bin` and every bundled ELF library.
- The package fails if a required file or dependency is absent.
- The generated manifest is populated, valid, and covers every regular file under `tools/`.
- The validator rejects unlisted files, duplicate logical/artifact paths, special files, symlinks, and symlinked parent directories.
- The package has no archive symlinks.
- The package’s `MP4Box` and MKV launchers work after extraction.
- The package does not rely on system-installed GPAC, MKVToolNix, or .NET.
- Smoke testing uses both MP4 and MKV fixtures and fails when either format is missing.
- README commands match the actual archive layout and Linux test invocation.
- The GitHub workflow works on a clean checkout.
- The release workflow uploads the archive as a release asset for published releases and remains useful for manual dispatch.

### 2. Decide whether the dependency strategy is acceptable

The current strategy bundles GPAC shared-library dependencies discovered from an Ubuntu package and preserves extracted MKVToolNix runtime libraries. This is more portable than the original implementation but still has a distribution/ABI scope.

Before release, decide whether to:

- Keep the current Ubuntu/glibc `linux-x64` scope and document it clearly.
- Replace GPAC with a genuinely static or AppImage distribution.
- Build GPAC from source for a controlled dependency closure.
- Add a container-based clean-environment smoke test for the supported Ubuntu base.

Do not claim universal Linux support unless this decision is resolved.

### 3. Run final Task 6 verification

On Linux:

```bash
dotnet test tests/StreamExtract.Tests/StreamExtract.Tests.csproj --no-restore
bash -n build/linux/fetch-native-tools.sh build/linux/smoke-test.sh build/linux/generate-fixtures.sh
rm -rf /tmp/streamextract-publish /tmp/streamextract-linux-x64.tar.gz /tmp/streamextract-fixtures
build/linux/fetch-native-tools.sh
build/linux/generate-fixtures.sh
SMOKE_FIXTURES="/tmp/streamextract-fixtures/sample.mp4
/tmp/streamextract-fixtures/sample.mkv" build/linux/smoke-test.sh
```

On Windows, run:

```bash
dotnet test tests/StreamExtract.WinForms.Tests/StreamExtract.WinForms.Tests.csproj
```

Also build the Windows GUI on a Windows machine or CI runner with the Windows Desktop runtime installed.

### 4. Review and commit only intended changes

Before committing:

```bash
git status --short
git diff --check
git diff --stat
git diff -- .github/workflows/linux-cli.yml build/linux StreamExtract.Cli/tools-manifest.json StreamExtract.Core/Services/NativeToolManifest.cs StreamExtract.Core/Services/NativeToolValidator.cs tests/StreamExtract.Tests/NativeToolValidatorTests.cs README.md
```

Do not stage `bin/`, `obj/`, extracted AppImage trees, `.deb` files, archives, logs, or ignored local fixtures.

After acceptance, use a Conventional Commit such as:

```text
build: harden self-contained Linux packaging
```

Do not amend earlier commits.

## Known risks and limitations

- The current Linux package depends on the host kernel and dynamic loader.
- The GPAC package reports that optional module directories are absent. The tested `MP4Box -version`, `MP4Box -info`, and MP4 extraction paths succeed, but optional GPAC filters are not guaranteed.
- CI currently creates its own MP4 and MKV fixtures from the tracked subtitle source. This is intentional because `*.mp4` is ignored and should not be committed.
- The Windows test project cannot execute on this Linux host. A successful Linux portable test run is not evidence that WinForms tests passed.
- The current design and implementation plan documents have uncommitted updates describing the dependency closure and host limitation. Review those doc diffs before committing.
- The package should not be described as distribution-independent until clean-environment testing confirms the supported ABI scope.

## Useful paths

- Design: `docs/superpowers/specs/2026-08-27-linux-cli-bundled-tools-design.md`
- Plan: `docs/superpowers/plans/2026-08-27-linux-cli-bundled-tools-implementation.md`
- Core project: `StreamExtract.Core/StreamExtract.Core.csproj`
- CLI project: `StreamExtract.Cli/StreamExtract.Cli.csproj`
- Tool manifest template: `StreamExtract.Cli/tools-manifest.json`
- Tool resolver: `StreamExtract.Core/Services/NativeToolResolver.cs`
- Tool validator: `StreamExtract.Core/Services/NativeToolValidator.cs`
- Packaging fetch: `build/linux/fetch-native-tools.sh`
- Fixture generation: `build/linux/generate-fixtures.sh`
- Packaging smoke test: `build/linux/smoke-test.sh`
- Linux workflow: `.github/workflows/linux-cli.yml`
- Portable tests: `tests/StreamExtract.Tests/StreamExtract.Tests.csproj`
- Windows tests: `tests/StreamExtract.WinForms.Tests/StreamExtract.WinForms.Tests.csproj`
