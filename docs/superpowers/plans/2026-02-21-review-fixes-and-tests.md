# Code Review Fixes and Test Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct CUE output, harden output-directory containment, make update checking reliable, improve unsupported-file feedback, and complete regression coverage for the reviewed behavior.

**Architecture:** Keep the existing plugin/service boundaries. Add one reusable guarded-output helper for every generated destination, use unique temporary chapter files for cue generation, and keep update response size enforcement in `UpdateChecker` while making reading complete and bounded. Tests remain headless xUnit tests against pure helpers and test doubles; no bundled native tools are invoked.

**Tech Stack:** .NET 10, WinForms, C#, xUnit 2.9, `HttpMessageHandler` test doubles, Windows filesystem APIs.

**Spec:** Code-review findings and recommendations from the StreamExtract project review in this conversation.

## Global Constraints

- Preserve `net10.0-windows` and the existing xUnit test project.
- Do not invoke a shell for production extraction; continue using `ProcessStartInfo.ArgumentList`.
- Every native-tool output destination must pass through `OutputPathGuard`.
- CUE timestamps must use standard `MM:SS:FF` format with 75 frames per second.
- Update responses must remain bounded at 64 KiB and must be read completely before JSON parsing.
- Temporary files must be cleaned up without hiding the original extraction failure.
- Run `dotnet test` and `dotnet build` before declaring completion.

---

## Task 1: Fix standard CUE timestamp generation

**Files:**
- Modify: `Plugins/MkvExtractorPlugin.cs: FormatCueTime`
- Test: `tests/StreamExtract.Tests/MkvExtractorPluginTests.cs` (create if absent, otherwise extend)

**Interfaces:**
- Consumes: existing `TimeSpan` input to `MkvExtractorPlugin.FormatCueTime`.
- Produces: the same `string FormatCueTime(TimeSpan start)` signature, now returning standard `MM:SS:FF` output.

- [ ] **Step 1: Add failing format tests**

Add theory cases such as:

```csharp
[Theory]
[InlineData(0, 0, 0, "00:00:00")]
[InlineData(1, 23, 0, "01:23:00")]
[InlineData(1, 23, 500, "01:23:38")]
[InlineData(60, 0, 0, "60:00:00")]
public void FormatCueTime_UsesCueMinutesSecondsFrames(
    int minutes, int seconds, int milliseconds, string expected)
{
    var actual = MkvExtractorPlugin.FormatCueTime(
        new TimeSpan(0, 0, minutes, seconds, milliseconds));

    Assert.Equal(expected, actual);
}
```

Add a rollover test for a fractional value rounding to frame 75, and a negative-input test if the chosen implementation clamps negative chapter times to zero.

- [ ] **Step 2: Run the focused tests and verify failure**

Run:

```text
dotnet test tests/StreamExtract.Tests/StreamExtract.Tests.csproj --filter FullyQualifiedName~FormatCueTime
```

Expected: the existing implementation returns `HH:MM:SS` and fails the new cases.

- [x] **Step 3: Implement `MM:SS:FF` conversion**

Compute total seconds, derive whole minutes and seconds, and convert the fractional second to `0..74` frames using 75 frames per second. Handle frame and second rollover. Keep minute width at least two digits but allow values above 59 minutes.

- [ ] **Step 4: Run focused tests and inspect generated cue text**

Run the focused test command again. Add or update a `BuildPerTrackCue` assertion so an emitted line looks like:

```text
INDEX 01 01:23:38
```

- [ ] **Step 5: Commit**

```text
git add Plugins/MkvExtractorPlugin.cs tests/StreamExtract.Tests/MkvExtractorPluginTests.cs
git commit -m "fix: emit standard cue timestamps"
```

---

## Task 2: Centralize and harden generated output paths

**Files:**
- Modify: `Services/OutputPathGuard.cs`
- Modify: `Plugins/MkvExtractorPlugin.cs`
- Modify: `Plugins/Mp4ExtractorPlugin.cs`
- Test: `tests/StreamExtract.Tests/OutputPathGuardTests.cs`
- Test: `tests/StreamExtract.Tests/MkvExtractorPluginTests.cs`
- Test: `tests/StreamExtract.Tests/Mp4ExtractorPluginTests.cs`

**Interfaces:**
- Consumes: output directory and generated base names from both plugins.
- Produces: all generated output arguments use `OutputPathGuard.ResolveContainedPath`; output-directory reparse-point policy is explicit and tested.

- [x] **Step 1: Add tests proving every generated destination is guarded**

Build requests with source names containing normal and boundary-safe names, then assert command arguments contain the resolved output path. Cover tracks, chapters, tags, cue sheets, timestamps, MP4 raw tracks, and MP4 chapters.

Add a Windows-specific test for a directory junction or symlink under a temporary root. The test should establish the project’s chosen policy: reject an output root or destination containing a reparse point, rather than claiming lexical containment is sufficient.

- [ ] **Step 2: Run the focused tests and verify missing protection**

Run:

```text
dotnet test tests/StreamExtract.Tests/StreamExtract.Tests.csproj --filter FullyQualifiedName~OutputPathGuard|FullyQualifiedName~Build
```

Expected: tests for unguarded plugin destinations or reparse points fail before implementation.

- [x] **Step 3: Add one guarded output helper and apply it everywhere**

Use a helper with this shape inside each plugin or a shared service:

```csharp
private static string OutputPath(ExtractRequest req, string fileName)
    => OutputPathGuard.ResolveContainedPath(req.OutputDirectory, fileName);
```

Replace manual string concatenation for all generated files. Keep native argument ordering unchanged.

- [x] **Step 4: Implement reparse-point protection**

Choose and document the conservative policy: reject an output directory or existing path component that is a reparse point before extraction. Ensure the check handles the output root and existing destination parent/components without following a symlink outside the root. Preserve normal nonexistent output paths.

- [ ] **Step 5: Run all path/plugin tests**

Run:

```text
dotnet test tests/StreamExtract.Tests/StreamExtract.Tests.csproj --filter FullyQualifiedName~OutputPathGuard|FullyQualifiedName~MkvExtractorPlugin|FullyQualifiedName~Mp4ExtractorPlugin
```

- [ ] **Step 6: Commit**

```text
git add Services/OutputPathGuard.cs Plugins/MkvExtractorPlugin.cs Plugins/Mp4ExtractorPlugin.cs tests/StreamExtract.Tests
git commit -m "fix: guard all extraction output paths"
```

---

## Task 3: Prevent stale chapter files during per-track cue extraction

**Files:**
- Modify: `Plugins/MkvExtractorPlugin.cs`
- Test: `tests/StreamExtract.Tests/MkvExtractorPluginTests.cs`

**Interfaces:**
- Consumes: existing `ExtractCuesForSelectedTracksAsync` workflow and fake `IProcessRunner`.
- Produces: cue generation reads only chapter XML created successfully by the current operation; temporary files are removed in cleanup.

- [ ] **Step 1: Add a failing stale-file regression test**

Create a temporary output directory containing an old `<ChapterAtom>` XML file. Configure a fake process runner to fail the chapter extraction command. Assert extraction returns a failure and does not generate cue files from the old XML.

Add a success test asserting the chapter command writes/uses a unique temporary path and that the final cue output contains only the current chapter data.

- [ ] **Step 2: Run the focused test and verify failure**

Run:

```text
dotnet test tests/StreamExtract.Tests/StreamExtract.Tests.csproj --filter FullyQualifiedName~CuesForSelectedTracks
```

Expected: the current implementation can read the stale fixed-name XML, so the regression test fails.

- [x] **Step 3: Change chapter extraction to a unique temporary path**

Add an overload or parameterized builder:

```csharp
internal static IEnumerable<string> BuildChaptersCommand(
    ExtractRequest req, string outputPath)
```

For cue-only extraction, create a unique guarded temporary filename in the output directory, run extraction into it, read it only after successful completion, then delete it in `finally`.

When chapters and cues are both selected, either reuse the successful chapter output only after the chapter mode succeeds or independently generate/read a unique temporary file. Never trust a pre-existing final chapter file after a failed command.

- [ ] **Step 4: Verify cleanup and failure behavior**

Test that temporary files are deleted after success and after a runner exception. Ensure cleanup exceptions do not replace the original extraction failure.

- [ ] **Step 5: Run plugin tests**

Run the focused plugin tests and then the complete test project.

- [ ] **Step 6: Commit**

```text
git add Plugins/MkvExtractorPlugin.cs tests/StreamExtract.Tests/MkvExtractorPluginTests.cs
git commit -m "fix: avoid stale chapter data in cue extraction"
```

---

## Task 4: Make update checking complete, bounded, and HTTP-aware

**Files:**
- Modify: `Services/UpdateChecker.cs`
- Modify: `Form1.cs` only if parser/update behavior needs adjusted validation
- Test: `tests/StreamExtract.Tests/UpdateCheckerTests.cs`

**Interfaces:**
- Consumes: existing `UpdateChecker.CheckAsync` and custom parser API.
- Produces: complete response-body parsing, 64 KiB limit, non-success response rejection, cancellation propagation, and injectable HTTP transport for deterministic tests.

- [ ] **Step 1: Add test seams and failing tests**

Refactor the static `HttpClient` dependency behind an injectable `HttpMessageHandler` or client factory without changing production behavior. Add tests for:

- a response delivered in multiple chunks that contains valid JSON and returns an update;
- a non-success HTTP status returning `null`;
- a body exactly at the allowed size;
- a body exceeding the limit with no `Content-Length` returning `null`;
- cancellation propagating as `OperationCanceledException`;
- malformed JSON returning `null`.

- [ ] **Step 2: Run focused update tests and verify failure**

Run:

```text
dotnet test tests/StreamExtract.Tests/StreamExtract.Tests.csproj --filter FullyQualifiedName~UpdateChecker
```

Expected: the multi-read response test exposes the current single-read bug.

- [x] **Step 3: Implement bounded full-body reading**

Read until EOF into a bounded `StringBuilder`/buffer. Stop and return `null` once the character/byte budget exceeds 64 KiB. Keep cancellation unwrapped and rethrow it. Call `EnsureSuccessStatusCode()` before parsing.

Because the limit is specified in bytes, use a byte-counting bounded stream or read response bytes first, then decode UTF-8; do not treat a UTF-8 multibyte character as one byte accidentally.

- [ ] **Step 4: Improve parser validation**

Validate required version and URL fields. Preserve the existing version comparison. Keep URL opening separately protected by `BrowserLauncher`’s HTTPS host allowlist.

- [ ] **Step 5: Run update tests and the full suite**

Run the focused command, then:

```text
dotnet test
```

- [ ] **Step 6: Commit**

```text
git add Services/UpdateChecker.cs Form1.cs tests/StreamExtract.Tests/UpdateCheckerTests.cs
git commit -m "fix: read update responses reliably"
```

---

## Task 5: Report unsupported imports to users

**Files:**
- Modify: `Form1.cs: StartImportAsync`
- Test: `tests/StreamExtract.Tests/FormSelectionTests.cs` or a new import-focused test where UI-free seams permit

**Interfaces:**
- Consumes: `PluginRegistry.GetPlugin` result.
- Produces: an explicit debug-log message for unsupported files; supported import behavior remains unchanged.

- [ ] **Step 1: Add a UI-free test seam if needed**

Extract a small internal formatter/helper that accepts a path and nullable plugin, returning either the plugin or the message. Test that an unsupported extension produces `Unsupported file type: <name>` and that supported extensions produce no warning.

- [x] **Step 2: Implement the log message**

Replace the silent `continue` with a `DebugLog` call containing the filename, then continue processing remaining files.

- [ ] **Step 3: Run the focused and full tests**

Run:

```text
dotnet test
```

- [ ] **Step 4: Commit**

```text
git add Form1.cs tests/StreamExtract.Tests
git commit -m "fix: report unsupported input files"
```

---

## Task 6: Complete regression and integration coverage

**Files:**
- Modify/Create: `tests/StreamExtract.Tests/MkvExtractorPluginTests.cs`
- Modify/Create: `tests/StreamExtract.Tests/Mp4ExtractorPluginTests.cs`
- Modify/Create: `tests/StreamExtract.Tests/UpdateCheckerTests.cs`
- Modify: `tests/StreamExtract.Tests/OutputPathGuardTests.cs`
- Modify: `tests/StreamExtract.Tests/ProcessRunnerTests.cs`
- Modify/Create: request-builder and selection tests as needed

**Interfaces:**
- Consumes: all production fixes from Tasks 1–5.
- Produces: stable regression suite covering review findings and existing documented contracts.

- [ ] **Step 1: Inventory existing tests against README claims**

Map tests to path containment, request building, selection snapshotting, cue generation, progress math, plugin command builders, update parsing, process failure, cancellation, timeout, and unsupported imports. Record every uncovered claim as a test case before implementation.

- [ ] **Step 2: Add edge-case tests**

Complete coverage for:

- empty selections and invalid output directories;
- unknown track IDs and duplicate attachment destinations;
- chapter XML with missing/invalid timestamps;
- nine-digit chapter fractions and CUE frame rounding;
- extraction mode failures while later modes continue;
- stale chapter files and temporary cleanup;
- MP4 output path containment;
- update response chunking, byte limits, HTTP errors, malformed payloads, and cancellation;
- process stderr preference, stdout fallback, timeout kill, and cancellation kill;
- unsupported extensions.

- [ ] **Step 3: Add deterministic fake runner assertions**

Ensure fakes record executable name, argument list, working directory, and cancellation token. Assert exact native command arguments rather than only checking that a call occurred.

- [ ] **Step 4: Run the complete suite repeatedly**

Run:

```text
dotnet test --no-restore
```

Repeat the tests at least once for filesystem-sensitive cases. If Windows-only tests are used, mark them clearly and keep non-Windows-safe unit tests independent.

- [ ] **Step 5: Run build and inspect warnings**

Run:

```text
dotnet build -c Release --no-restore
```

Expected: successful build with no warnings, matching the README claim.

- [ ] **Step 6: Commit the completed test suite**

```text
git add tests
 git commit -m "test: cover reviewed extraction and update edge cases"
```

---

## Task 7: Documentation and final verification

**Files:**
- Modify: `README.md`
- Modify: relevant XML comments in `Plugins/MkvExtractorPlugin.cs` and `Services/OutputPathGuard.cs`

- [ ] **Step 1: Update documented behavior**

Document that generated outputs all use guarded paths, CUE files use standard `MM:SS:FF`, per-track cues use a temporary chapter file, and unsupported dropped files are reported in the log. If reparse points are rejected, state that explicitly.

- [ ] **Step 2: Review for stale claims**

Remove or revise any wording that claims only attachment paths are guarded or implies a single-read update response.

- [ ] **Step 3: Run final verification**

Run:

```text
dotnet test
dotnet build -c Release
```

Also inspect:

```text
git diff --check
git status --short
```

- [ ] **Step 4: Commit documentation**

```text
git add README.md Plugins/MkvExtractorPlugin.cs Services/OutputPathGuard.cs
git commit -m "docs: document extraction safety guarantees"
```

## Review Questions Resolved

- **CUE format:** Use standard `MM:SS:FF` with 75 frames per second; do not preserve the current `HH:MM:SS` behavior.
- **Existing output behavior:** Use unique temporary chapter files for cue generation and never consume stale files after an extraction failure. Generated final outputs may continue to overwrite according to native-tool behavior, but temporary intermediates must be operation-specific.
- **Update endpoint behavior:** Read the complete response with a strict 64 KiB byte limit, reject non-success HTTP statuses, and preserve cancellation.
- **Unsupported files:** Log an explicit unsupported-file message instead of silently ignoring them.

## Self-Review Checklist

- [x] Every review finding 1, 3, and 4 has an implementation task.
- [x] The requested test-completion work has a dedicated task and concrete cases.
- [x] The author questions are resolved with decisions and implementation consequences.
- [x] No task relies on an unspecified function or test seam.
- [x] Each task has files, tests, implementation steps, and verification commands.
