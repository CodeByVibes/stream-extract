# Linux CLI with Bundled Native Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a full-featured, self-contained `linux-x64` StreamExtract CLI that shares a platform-neutral core with the existing Windows WinForms application and bundles verified native tools at release time.

**Architecture:** Extract models, plugins, process execution, path guarding, request construction, and tool validation into `StreamExtract.Core` targeting `net10.0`. Keep the WinForms project as a Windows-only UI shell referencing the core, and add a thin `StreamExtract.Cli` project for argument parsing, terminal output, cancellation, and exit codes. Release automation downloads pinned Linux MKVToolNix and GPAC artifacts, verifies checksums, and packages them beside the published CLI.

**Tech Stack:** .NET 10, C#, `System.Diagnostics.Process`, a small internal CLI parser with no new parser dependency, xUnit, Linux `linux-x64`, MKVToolNix, GPAC/MP4Box, and GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-08-27-linux-cli-bundled-tools-design.md`

## Global Constraints

- The first Linux release targets `linux-x64` as a portable archive.
- The first Linux release uses bundled tools by default and does not include an alternate tool-directory option.
- The Linux archive contains `mkvmerge`, `mkvextract`, and `MP4Box` under `tools/`.
- Native tool binaries are downloaded during release packaging and are not committed to Git.
- The archive is self-contained and does not require a separately installed .NET runtime, MKVToolNix, or GPAC.
- The existing Windows GUI remains targeted at `net10.0-windows` and must remain buildable.
- The shared core targets `net10.0` and contains no WinForms or Windows-only references.
- Production media-tool execution must not invoke a shell and must pass arguments through `ProcessStartInfo.ArgumentList`.
- Every generated native-tool output path must pass through `OutputPathGuard`.
- Tool downloads use pinned versions and immutable URLs or release identifiers with SHA-256 verification.
- Missing, modified, non-executable, or wrong-platform bundled tools fail before media processing.
- Cancellation is distinct from extraction failure and terminates the active native process tree.
- Core and CLI tests must run on Linux without WinForms or Windows shell commands.

---

### Task 1: Establish the Platform-Neutral Core

**Files:**
- Create: `StreamExtract.Core/StreamExtract.Core.csproj`
- Create: `StreamExtract.Core/GlobalUsings.cs` only when the core build demonstrates that an explicit global-using file is needed
- Modify: `stream-extract-winforms.csproj`
- Modify: `stream-extract-winforms.slnx`
- Move: `Models/*.cs`, `Plugins/*.cs`, and platform-neutral `Services/*.cs` into `StreamExtract.Core/`
- Modify: `Form1.cs`, `Program.cs`, and other WinForms files only for namespace/reference changes
- Test: `tests/StreamExtract.Tests/StreamExtract.Tests.csproj`

**Interfaces:**
- Consumes: Existing model, plugin, and service types and their current public/internal APIs.
- Produces: A `StreamExtract.Core` assembly targeting `net10.0`; the WinForms application references it and remains the only consumer of WinForms types.

- [x] **Step 1: Add the core project and solution entry**

Create a `Microsoft.NET.Sdk` project with `TargetFramework` `net10.0`, nullable and implicit usings enabled, and no `UseWindowsForms` or Windows target framework settings. Add it to `stream-extract-winforms.slnx`.

- [x] **Step 2: Move only platform-neutral source into the core**

Move `Models/*.cs`, `Plugins/*.cs`, `Services/ExternalToolException.cs`, `Services/ExtractionRequestBuilder.cs`, `Services/IProcessRunner.cs`, `Services/OutputPathGuard.cs`, `Services/ProcessRunner.cs`, and `Services/UpdateChecker.cs` into the core project. Keep `BrowserLauncher.cs` in the WinForms shell initially if it remains UI-only. Preserve namespaces so consumers do not need broad renames.

- [x] **Step 3: Reference the core from the WinForms project**

Add a project reference from `stream-extract-winforms.csproj` to `StreamExtract.Core`. Remove moved source files from the WinForms project’s compile items if SDK default inclusion would otherwise compile both copies. Keep WinForms source, designer files, resources, `Program.cs`, and `SmoothProgressBar.cs` in the Windows project.

- [x] **Step 4: Retarget tests and preserve internals access**

Retarget `tests/StreamExtract.Tests/StreamExtract.Tests.csproj` to `net10.0`, remove `EnableWindowsTargeting`, and reference `StreamExtract.Core` rather than the WinForms project. Add `InternalsVisibleTo` for `StreamExtract.Tests` to the core project. Keep any explicitly UI-dependent tests Windows-only rather than making the core reference Windows-targeted.

- [x] **Step 5: Build the core and Windows shell separately**

Run:

```bash
dotnet build StreamExtract.Core/StreamExtract.Core.csproj -p:BaseIntermediateOutputPath=/tmp/stream-extract-core-obj/ -p:OutputPath=/tmp/stream-extract-core-bin/
dotnet build stream-extract-winforms.csproj -p:BaseIntermediateOutputPath=/tmp/stream-extract-winforms-obj/ -p:OutputPath=/tmp/stream-extract-winforms-bin/
```

Expected: both projects build without errors; the core builds on Linux and the WinForms shell remains Windows-targeted.

- [x] **Step 6: Commit the core split**

```bash
git add StreamExtract.Core stream-extract-winforms.csproj stream-extract-winforms.slnx tests/StreamExtract.Tests/StreamExtract.Tests.csproj Models Plugins Services Form1.cs Program.cs
git commit -m "refactor: extract platform-neutral core"
```

### Task 2: Add Platform-Aware Bundled Tool Resolution

**Files:**
- Create: `StreamExtract.Core/Services/NativeTool.cs`
- Create: `StreamExtract.Core/Services/NativeToolManifest.cs`
- Create: `StreamExtract.Core/Services/NativeToolResolver.cs`
- Create: `StreamExtract.Core/Services/NativeToolValidator.cs`
- Modify: `Plugins/MkvExtractorPlugin.cs`
- Modify: `Plugins/Mp4ExtractorPlugin.cs`
- Modify: `Program.cs`
- Modify: `stream-extract-winforms.csproj`
- Test: `tests/StreamExtract.Tests/NativeToolResolverTests.cs`
- Test: `tests/StreamExtract.Tests/NativeToolValidatorTests.cs`

**Interfaces:**
- Consumes: A base directory containing `tools/` and `tools-manifest.json`.
- Produces: `NativeToolResolver.Resolve(NativeToolId)` returning an executable path; plugins no longer hard-code `.exe` names; validator rejects missing, mismatched, non-executable, and invalid manifest tools.

- [x] **Step 1: Define logical tool identifiers and manifest records**

Define an enum or equivalent closed set containing `MkvMerge`, `MkvExtract`, and `Mp4Box`. Define manifest records containing logical name, filename, version, RID, source identifier, and SHA-256. Use Linux names `mkvmerge`, `mkvextract`, and `MP4Box`; use Windows names `mkvmerge.exe`, `mkvextract.exe`, and `mp4box.exe`.

- [x] **Step 2: Write resolver and validator tests first**

Test that the resolver selects the expected filename for the current OS, resolves paths relative to an explicit application base directory, rejects an unknown logical tool, and does not fall back to `PATH`. Test validator outcomes for valid files, missing files, wrong hashes, malformed manifests, and Linux files without executable permission.

- [x] **Step 3: Implement bundled resolver and manifest validation**

Load `tools-manifest.json` from the application base directory, resolve only the expected `tools/` child path, calculate SHA-256 using a streaming file read, and on Linux verify `UnixFileMode.UserExecute` or equivalent executable permission. Return typed validation failures that the CLI and GUI can format without exposing implementation details.

- [x] **Step 4: Update plugins to consume resolved paths**

Change plugin construction to receive an `INativeToolResolver` or resolved tool set. Preserve the existing `IProcessRunner` abstraction and command arguments. Each plugin should pass the resolved executable path or logical filename through the runner without appending `.exe` itself.

- [x] **Step 5: Adapt Windows startup validation**

Replace the hard-coded Windows-only hash dictionary in `Program.cs` with the shared validator and the existing Windows bundle manifest or equivalent Windows-specific validation data. Keep the current fail-closed message-box behavior in the WinForms shell.

- [x] **Step 6: Run focused tests and commit**

```bash
dotnet test tests/StreamExtract.Tests/StreamExtract.Tests.csproj --no-restore --filter FullyQualifiedName~NativeTool
git add StreamExtract.Core Plugins Program.cs stream-extract-winforms.csproj tests/StreamExtract.Tests
git commit -m "feat: resolve and validate bundled native tools"
```

### Task 3: Make Core Tests Linux-Compatible

**Files:**
- Create: `tests/StreamExtract.Tests/TestProcessHost/` fixture project or equivalent cross-platform helper
- Modify: `tests/StreamExtract.Tests/ProcessRunnerTests.cs`
- Modify: `tests/StreamExtract.Tests/FakeProcessRunner.cs`
- Modify: `tests/StreamExtract.Tests/*.csproj`
- Modify: `Services/ProcessRunner.cs` only when the cross-platform tests demonstrate a production portability defect

**Interfaces:**
- Consumes: `ProcessRunner` and `IProcessRunner` from `StreamExtract.Core`.
- Produces: Linux-runnable tests for stdout/stderr capture, exit codes, progress parsing, timeout, and process-tree cancellation.

- [x] **Step 1: Add a cross-platform process fixture**

Create a tiny test helper executable or fixture script abstraction that can emit supplied stdout/stderr, exit with a supplied code, and sleep for cancellation tests. Prefer invoking the current test assembly or `dotnet` with explicit arguments; do not use `cmd.exe`, `/c`, `exit /b`, `ping -n`, or `nul` in production-portable tests.

- [x] **Step 2: Replace Windows shell tests**

Rewrite each `ProcessRunnerTests` case to call the helper through `ProcessStartInfo.ArgumentList`. Preserve assertions for progress parsing, stdout fallback, nonzero exit code, pre-cancellation, timeout, and process termination.

- [x] **Step 3: Strengthen fake-runner command assertions**

Record filename, argument list, working directory, and cancellation token in `FakeProcessRunner`. Add assertions in plugin tests that generated paths and tool invocations are passed as separate arguments.

- [x] **Step 4: Run all tests on Linux**

```bash
dotnet test tests/StreamExtract.Tests/StreamExtract.Tests.csproj --no-restore -p:BaseIntermediateOutputPath=/tmp/stream-extract-tests-obj/ -p:OutputPath=/tmp/stream-extract-tests-bin/
```

Expected: all core tests pass without WinForms or Windows shell dependencies.

- [x] **Step 5: Commit the portable test suite**

```bash
git add tests/StreamExtract.Tests Services/ProcessRunner.cs
git commit -m "test: run process coverage across platforms"
```

### Task 4: Implement the Full-Parity CLI

**Files:**
- Create: `StreamExtract.Cli/StreamExtract.Cli.csproj`
- Create: `StreamExtract.Cli/Program.cs`
- Create: `StreamExtract.Cli/Commands/CliOptions.cs`
- Create: `StreamExtract.Cli/Commands/CliParser.cs`
- Create: `StreamExtract.Cli/Formatting/TerminalFormatter.cs`
- Create: `StreamExtract.Cli/Extraction/CommandRunner.cs`
- Modify: `stream-extract-winforms.slnx`
- Test: `tests/StreamExtract.Tests/CliParserTests.cs`
- Test: `tests/StreamExtract.Tests/TerminalFormatterTests.cs`
- Test: `tests/StreamExtract.Tests/CliIntegrationTests.cs`

**Interfaces:**
- Consumes: Core plugin registry, `NativeToolResolver`, `ExtractionRequestBuilder`, `ExtractRequest`, `MediaFileInfo`, and `ExtractOutcome`.
- Produces: `streamextract info <file>`, `streamextract extract <file...> [options]`, `--help`, and `--version`; exit codes `0` success, `2` usage error, `1` extraction/tool failure, and `130` cancellation.

- [x] **Step 1: Define CLI option records and exact syntax**

Implement options for `--tracks <id[,id...]>`, `--chapters`, `--attachments`, `--tags`, `--cue-sheets`, `--cues-for-selected-tracks`, `--timestamps`, `--all`, `--output <directory>`, and `--verbose`. Reject duplicate/conflicting values, malformed IDs, unknown options, missing values, no selected modes, and unsupported files with usage exit code `2`.

- [x] **Step 2: Write parser and formatter tests**

Test the two documented command forms, every extraction flag, multiple input files, `--all`, invalid track lists, missing output values, help/version, and stable readable formatting for tracks, chapters, attachments, and tags. Assert that no parser test requires native tools.

- [x] **Step 3: Implement `info`**

Resolve and validate tools before analysis, select the plugin by extension, call `AnalyzeFileAsync`, and format all returned media information. Report errors to stderr and return the defined nonzero status without showing GUI dialogs or attempting update checks.

- [x] **Step 4: Implement `extract` for one or more files**

For each input, analyze the file, map CLI selections to `FileSelection`, validate/create the output directory, call `ExtractionRequestBuilder.TryBuild`, and invoke the plugin. Continue independent files and modes according to the existing `ExtractOutcome` contract. Print per-file and per-mode failures and return `1` if any extraction failed.

- [x] **Step 5: Wire progress and cancellation**

Register a `Console.CancelKeyPress` handler that cancels a shared `CancellationTokenSource`, set `e.Cancel = true` on the first signal, and allow the process runner to terminate the active native process. Return `130` for cancellation. Keep progress output correct when redirected to a non-interactive terminal.

- [x] **Step 6: Add the CLI project and publish smoke test**

Add `StreamExtract.Cli` to the solution, reference only `StreamExtract.Core`, publish self-contained for `linux-x64`, and verify `--help` and `--version` from the published executable.

- [x] **Step 7: Run CLI tests and commit**

```bash
dotnet test tests/StreamExtract.Tests/StreamExtract.Tests.csproj --no-restore --filter FullyQualifiedName~Cli
dotnet publish StreamExtract.Cli/StreamExtract.Cli.csproj -c Release -r linux-x64 --self-contained true -o /tmp/streamextract-cli-publish
git add StreamExtract.Cli stream-extract-winforms.slnx tests/StreamExtract.Tests
git commit -m "feat: add full-featured Linux CLI"
```

### Task 5: Add Linux Release Packaging and Bundle Smoke Tests

**Files:**
- Create: `.github/workflows/linux-cli.yml` or the repository’s CI-equivalent workflow
- Create: `build/linux/fetch-native-tools.sh`
- Create: `build/linux/tool-versions.env`
- Create: `build/linux/smoke-test.sh`
- Create: `StreamExtract.Cli/tools-manifest.json` as the packaging manifest template
- Modify: `README.md`
- Modify: `StreamExtract.Cli/StreamExtract.Cli.csproj`
- Test: `tests/StreamExtract.Tests/BundleSmokeTests.cs` if archive testing can run in CI

**Interfaces:**
- Consumes: A self-contained CLI publish directory and pinned upstream artifact metadata.
- Produces: `streamextract-linux-x64.tar.gz` containing the CLI, `tools/mkvmerge`, `tools/mkvextract`, `tools/MP4Box`, licenses, and `tools-manifest.json`.

- [x] **Step 1: Pin tool versions and artifact metadata**

Record exact MKVToolNix and GPAC versions, Linux x64 artifact URLs or release identifiers, expected archive formats, required executable paths, and SHA-256 values in a reviewed build input file. Do not query a mutable latest endpoint during packaging.

- [x] **Step 2: Implement verified tool download and extraction**

Write a shell script with strict failure options that downloads each pinned artifact, verifies SHA-256 before extraction, extracts only the required executables and license files, renames them to the archive contract, and fails if any expected executable is missing.

- [x] **Step 3: Assemble archive and permissions**

Publish the CLI self-contained for `linux-x64`, copy tools and licenses into the specified layout, generate the manifest from the pinned metadata, run `chmod +x` on the CLI and native tools, and create a reproducible `.tar.gz` using normalized archive metadata.

- [x] **Step 4: Add archive smoke tests**

Run the bundled tools with harmless version/info commands, invoke the bundled CLI with `--help` and `--version`, verify all expected files and executable bits, and run `info` against representative MKV and MP4 fixtures. Ensure the smoke test does not use system-installed tool paths.

- [x] **Step 5: Document Linux installation and provenance**

Update `README.md` with Linux archive installation, execution, supported commands, bundled tool provenance, version/checksum verification, and the fact that no separate runtime or native-tool installation is required.

- [x] **Step 6: Run release validation and commit**

```bash
build/linux/fetch-native-tools.sh
build/linux/smoke-test.sh
tar -tzf /tmp/streamextract-linux-x64.tar.gz
git diff --check
git add .github build/linux README.md StreamExtract.Cli
git commit -m "build: package Linux CLI with verified tools"
```

### Task 6: Final Cross-Platform Verification

**Files:**
- Modify: `README.md` to reflect the final Linux CLI and archive commands
- Modify: `stream-extract-winforms.csproj` or publish settings when the verified cross-platform build requires a metadata correction
- Test: Existing core, CLI, and bundle test suites

**Interfaces:**
- Consumes: Completed core split, tool resolver, CLI, tests, and release archive.
- Produces: Evidence that Linux CLI functionality and existing Windows GUI build requirements are both satisfied.

- [x] **Step 1: Run Linux core and CLI tests**

```bash
dotnet test tests/StreamExtract.Tests/StreamExtract.Tests.csproj --no-restore -p:BaseIntermediateOutputPath=/tmp/stream-extract-final-tests-obj/ -p:OutputPath=/tmp/stream-extract-final-tests-bin/
```

- [x] **Step 2: Build the Linux CLI and Windows GUI**

```bash
dotnet publish StreamExtract.Cli/StreamExtract.Cli.csproj -c Release -r linux-x64 --self-contained true -o /tmp/streamextract-final-cli
dotnet build stream-extract-winforms.csproj -c Release -p:BaseIntermediateOutputPath=/tmp/stream-extract-final-winforms-obj/ -p:OutputPath=/tmp/streamextract-final-winforms-bin/
```

Expected: Linux CLI publish succeeds; Windows project remains buildable under the installed SDK with Windows targeting enabled.

- [x] **Step 3: Verify archive behavior on a clean Linux environment**

Extract the archive into a temporary directory with no reliance on `PATH` for native tools, invoke `--help`, `--version`, `info`, and representative extraction commands, then verify no child native process remains after cancellation.

- [x] **Step 4: Review security and repository state**

Confirm no third-party Linux binaries are tracked, all release URLs are pinned, manifests match packaged tools, generated outputs are ignored, and no unrelated files are staged.

- [x] **Step 5: Commit any final documentation-only corrections**

```bash
git status --short
```

If documentation or build metadata needs correction, commit only those intentional changes with a Conventional Commit message. Do not amend earlier task commits.
