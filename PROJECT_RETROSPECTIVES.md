# ToolBox Milestone Retrospectives

> This is the durable record of failures, root causes, verification evidence, and reusable lessons from major ToolBox milestones. Read it together with `PROJECT_CONTEXT.md` before starting a new major phase.

## Mandatory retrospective protocol

A retrospective is required immediately after every major milestone. A milestone is major when it changes a public release, Plugin SDK/API boundary, plugin lifecycle or isolation model, package/install format, persistent settings, primary UI architecture, CI/release pipeline, or completes physical hardware acceptance.

The milestone is not considered fully closed until its retrospective is written. Each retrospective must record:

1. The intended outcome and the final delivered state.
2. Every meaningful failure or unexpected result, including failures that appeared only in CI or on physical hardware.
3. The root cause rather than only the visible symptom.
4. Why existing local checks did not catch it earlier.
5. The implemented correction and the evidence that verified it.
6. Reusable lessons and concrete prevention measures for future milestones.
7. Remaining risks, deferred work, and the next acceptance boundary.

Retrospectives must not hide failures, delete valuable assertions to obtain a green build, or describe an unverified assumption as a confirmed cause. Update `PROJECT_CONTEXT.md` with the checkpoint summary and link to the detailed entry here.

## 2026-08-27 — ToolBox v0.1.0 release and B-scheme host refinement

### Outcome

- Rebuilt the WPF Host with the approved mist-silver B visual scheme and Module T icon family.
- Added persistent language, close behavior, plugin-open state, tray lifecycle, and three-layer plugin state: installed, opened/visible, and runtime enabled.
- Added the Phone Audio Relay product plugin and explicit restart-required lifecycle boundary.
- Fixed window chrome, maximize/restore behavior, plugin installation conflicts, and WinRT reload behavior.
- Published the public, non-prerelease GitHub `v0.1.0` Release from commit `8bf0ca0b24f8c0d826c709446b1445b2f240fa81`.
- Published four verified assets: the self-contained Windows x64 Host, KeyboardMouse package, PhoneAudioRelay package, and SHA-256 manifest.
- Final local verification passed three consecutive full Release test runs, each `68/68`; both the final main CI and tag-triggered Release workflow passed.

### Failure chain and root causes

#### 1. Plugin runtime sources existed locally but were absent from Git

The repository used the broad ignore rule `Plugins/`. It also matched the source directory `src/ToolBox.Core/Plugins`, so local builds succeeded from files present on disk while a clean GitHub checkout did not contain the runtime implementation.

Correction: scope runtime-data exclusion to the repository root with `/Plugins/`, then add and verify all plugin runtime sources.

Lesson: a successful local build does not prove that the repository is complete. Before CI or release, verify tracked inputs from a clean checkout or archive, and keep ignore rules root-scoped where possible.

#### 2. Legacy Plugin SDK compatibility fixture was nondeterministic

The compatibility test intentionally compiles a legacy plugin against `ToolBox.PluginSdk` version `0.0.1`. The fixture and current solution both produced an assembly named `ToolBox.PluginSdk`; MSBuild project graph and global `Version`/`Configuration` properties could overwrite the intended legacy version. Local incremental outputs masked this, while a clean GitHub runner produced different dependency metadata.

Correction: remove the legacy SDK fixture from the main solution build graph, isolate its build, strip propagated version properties, and keep the real assertion that the plugin was compiled against `0.0.1`.

Lesson: compatibility fixtures must be hermetic and independent of incremental output. Prefer a pinned binary or local package for historical compatibility baselines. Never weaken the compatibility assertion merely to make CI green.

#### 3. Release checksum output was not a portable checksum manifest

Piping `Get-FileHash` objects directly to `Set-Content` wrote PowerShell's formatted object representation rather than canonical `hash  filename` lines. The first workflow also assumed a release was always new, making retries awkward.

Correction: explicitly format lowercase SHA-256 lines, include every package, create the Release only when absent, and upload assets with overwrite support.

Lesson: verify artifact contents, not only file existence. Release creation must be idempotent because a failed run can leave partial remote state.

#### 4. Shutdown timeout and external cancellation were conflated

Plugin shutdown used related cancellation signals without a single explicit timeout classification. Boundary timing could report a shutdown deadline as ordinary cancellation.

Correction: add a distinct `IsTimedOut` decision and preserve truthful failure states such as `PLUGIN_SHUTDOWN_TIMEOUT` and `RestartRequired`.

Lesson: cancellation source is part of lifecycle semantics. Timeout, caller cancellation, plugin failure, and successful disable must remain distinguishable in code, logs, UI, and tests.

#### 5. WinRT audio dependencies were incorrectly treated as fully unloadable plugin-local state

`AudioPlaybackConnection` depends on WinRT infrastructure with process-global behavior. Reloading private copies through collectible `AssemblyLoadContext` instances attempted to register `ComWrappers` globally more than once. Path-based assembly loading also left short-lived locks on `AudioRelay.dll` during cleanup.

Correction: share signed, version-checked `WinRT.Runtime` and `Microsoft.Windows.SDK.NET` assemblies through the default process context; load WinRT-dependent plugin assemblies from memory streams to avoid source-file locks; preserve path loading for ordinary plugins so existing `Assembly.Location` behavior remains compatible. Add a real load/start/unload-twice test.

Lesson: collectible managed load contexts do not guarantee that COM, WinRT, native libraries, static callbacks, or process-global registration can be unloaded. Platform dependencies require an explicit process-lifetime design and a user-visible restart boundary when safe release fails.

#### 6. Windows temporary-directory cleanup handled only one transient exception

The final main CI passed, but the independent Release test execution encountered a transient `UnauthorizedAccessException` while deleting `LegacyPlugin.dll`. The helper retried `IOException` only. Windows runtime finalization, antivirus, indexing, or filesystem timing can briefly deny deletion after an unload assertion has succeeded.

Correction: retry both `IOException` and `UnauthorizedAccessException` with bounded garbage collection/finalizer waits. Verify the complete suite three times locally and again in both remote workflows.

Lesson: cleanup is part of test reliability. Windows file cleanup needs bounded retries for known transient errors, while still failing after the retry limit so real leaks remain visible.

### Why the failures appeared one after another

This was the first end-to-end exercise of repository checkout, clean compilation, legacy compatibility, WPF/plugin lifecycle, WinRT reload, packaging, checksums, tag automation, and Release asset upload. Each repaired layer allowed the workflow to reach the next previously unexecuted boundary. The repeated failures were therefore not one ignored defect; they were multiple latent defects exposed sequentially by a progressively more complete pipeline.

However, the process was still too reactive. We trusted local incremental state too early and tested release-only behavior too late. Repeatedly force-moving a public version tag worked technically but is not the desired release discipline.

### Prevention checklist for future releases

1. Verify `git status`, `git ls-files`, ignored paths, and a clean checkout before declaring source complete.
2. Run clean restore/build/test, not only incremental build/test.
3. Repeat lifecycle, unload, process, and filesystem-sensitive suites at least three times before a release candidate.
4. Run a Release dry-run on `main` that creates and validates all EXE/TPK/checksum artifacts without publishing them.
5. Validate package structure, manifest/version identity, expected asset count, file names, nonzero sizes, and SHA-256 contents automatically.
6. Use a release-candidate tag such as `v0.2.0-rc.1` for end-to-end publication rehearsal when appropriate.
7. Treat a public stable tag as immutable. After publication, prefer a patch version over force-moving the tag.
8. Reuse one verified build artifact between CI and Release where practical, rather than rebuilding and retesting probabilistic platform behavior independently.
9. Preserve structured failure states and diagnostic logs; do not replace a failing assertion with a weaker one unless the product contract itself intentionally changes.
10. Close the milestone only after updating both `PROJECT_CONTEXT.md` and this retrospective record.

### Remaining boundary

The software and packages are released, but physical Android A2DP-source acceptance is still a separate milestone. It must verify real phone audio playback, coexistence with computer audio, stop/disable behavior, tray exit cleanup, and any device-specific restart requirement. Its results require a new retrospective entry regardless of whether the test passes on the first attempt.

## 2026-08-27 — Unified release-validation module

### Outcome

- Added `tools/Invoke-ReleaseValidation.ps1` as the single local, CI, and Tag Release entry point.
- The module performs clean restore/build/test, warnings-as-errors enforcement, self-contained Host publish, both product package builds, exact four-asset validation, package identity/version/entry/hash verification, and release SHA-256 reverse validation.
- CI and Release workflow adapters no longer duplicate the build, test, publish, package, and checksum implementation.
- Added deterministic ZIP construction shared by both product package adapters.
- Two consecutive final dry-runs passed all `68/68` tests and produced byte-identical hashes for all four candidate assets.
- Pull request #1 reran the same entry point on GitHub's Windows runner. The corrected run `33085645418` passed at commit `afe46a752843f8591c95e61c63b415294ee22eca` after the first run exposed a CI-only analyzer warning.
- Pull request #1 was merged as `036b78dfe0d692fea8bd60427b7b9a412cc0b10e`; the resulting `main` run `33129661198` passed the same validation pipeline. A fresh physical-test candidate was then built from that merge commit and passed all `68/68` tests.

### Failure discovered during the milestone

The first two successful dry-runs produced an identical Host executable but different `.tpk` hashes. Payload hashes inside both packages were identical; only ZIP entry timestamps changed on each build. The packages were semantically valid but not reproducible byte-for-byte, which would make independent release verification and artifact comparison noisy.

The first pull-request CI run then failed the new warnings-as-errors gate on `CA1859` in the ProtocolMismatchWorker fixture. The local SDK and CI SDK reported the same version, but the analyzer diagnostic appeared only on the fresh Windows Server 2025 runner. The previous workflow allowed warnings and the local dry-run did not reproduce this analyzer difference, so the stricter gate correctly exposed an environment-sensitive warning before merge.

### Root cause and correction

`ZipFile.CreateFromDirectory` preserved staging-file timestamps. Manifest and package metadata files were recreated during each run, and freshly rebuilt plugin files also received new timestamps. The package scripts therefore encoded wall-clock state even when every payload byte was unchanged.

The correction introduced `ToolBox.PackageTools.psm1`, which sorts entries and writes a fixed ZIP timestamp before copying payload bytes. Both package adapters now use that implementation. A second pair of full dry-runs confirmed identical EXE, TPK, and checksum hashes.

The analyzer finding was corrected without suppression by narrowing the fixture helper parameter from `IReadOnlyList<string>` to the actual `string[]` input and using `Length`. The PR workflow was rerun from a fresh commit after local validation.

### Reusable lessons

1. A valid package is not necessarily a reproducible package; compare complete artifact hashes across consecutive clean runs.
2. ZIP timestamps, entry ordering, host SDK patch level, and generated metadata are all release inputs even when source code is unchanged.
3. CI and Release should remain thin adapters over one locally executable validation module.
4. Artifact validation must parse and recalculate package contents; checking file existence or nonzero size is insufficient.
5. Temporary release directories need bounded Windows cleanup retries for both `IOException` and `UnauthorizedAccessException`.
6. Warnings-as-errors must run on the same fresh runner used for merge; matching SDK version alone does not guarantee identical analyzer results across operating-system images.

### Remaining boundary

The implementation is merged into `main`, locally verified, and verified by both pull-request and post-merge CI on fresh GitHub Windows runners. No Release update was performed. Physical Android audio acceptance is owned by the user and remains the next product milestone; it must not be marked complete until the user reports real phone results.

## 2026-08-28 — Physical Android phone audio acceptance

### Outcome

- The user reported that the physical-test candidate built from merge commit `036b78dfe0d692fea8bd60427b7b9a412cc0b10e` passed real-device testing.
- This closes the physical Android A2DP-source acceptance boundary for the tested phone and computer combination.
- Automated evidence remains separate: the same candidate passed a warnings-as-errors Release build, all `68/68` tests, package validation, and SHA-256 verification before delivery.

### Failure discovered during the milestone

No failure was reported during this physical acceptance pass. That does not prove compatibility with every Android vendor, Bluetooth adapter, Windows audio driver, or reconnect sequence.

### Reusable lessons

1. Record physical acceptance as user-supplied evidence and identify the exact tested commit; do not present it as CI evidence or as universal hardware compatibility.
2. Deliver hardware candidates from the merged commit rather than from a pre-merge branch so field results map to repository history.
3. Keep phone audio, computer audio coexistence, stop/disable cleanup, tray exit, and restart-required behavior in the regression checklist for future audio changes.
4. A passing hardware test closes the current acceptance boundary but does not remove the explicit restart-required safety state.

### Remaining boundary

Host lifecycle deepening is the next architecture milestone. Release `v0.1.0` remains unchanged; a future public release requires a separately versioned release decision and full validation.

## 2026-08-28 — Host lifecycle deepening

### Outcome

- Replaced the four App-level shutdown/restart Boolean and path fields with `HostLifetimeState`, which accepts one explicit shutdown or restart intent and produces one immutable exit plan.
- Added `HostShutdownCoordinator` and the default shutdown pipeline. Plugin view models, tray resources, diagnostics, logger, package installer, and optional replacement process now run in a locked order; a failure in one operation is reported without skipping the remaining cleanup operations.
- Added `IHostApplicationCommands` so `MainWindow` no longer casts `Application.Current` to the concrete `App` when closing, hiding to tray, or requesting audio recovery restart.
- Extracted `HostRestartService` so restart executable validation and launch parameters are testable without starting a replacement process.
- Host lifecycle tests increased from 8 to 23; the full solution now passes `83/83` tests. The unified Release validation also passed Host publish, both deterministic product packages, exact asset validation, and SHA-256 verification.

### Failure discovered during the milestone

The previous `App.OnExit` wrapped plugin, tray, diagnostics, and logging cleanup in one `try` block. Any early exception could skip every later resource even though the outer handler recorded only one generic shutdown failure. Restart state was spread across separate Boolean and path fields, and `MainWindow` reached the concrete global WPF application directly, which made the actual exit contract difficult to test.

A UI smoke run confirmed startup and close-to-tray behavior, but the system tray did not expose a targetable automation window. The test process had been launched with elevated permissions, so the first non-elevated cleanup attempt was denied; it was then terminated by exact PID at the matching permission level. This forced termination is not counted as graceful-exit evidence, and physical/UI acceptance remains user-owned.

Repository-wide `dotnet format --verify-no-changes` also exposed an existing baseline of mixed BOM/line-ending conventions, historical whitespace/import findings, and a Debug-only LegacyPlugin fixture load warning. The Release compiler/analyzer gate remains clean. Unrelated files were intentionally not rewritten during this lifecycle change.

### Root cause and correction

Shutdown ordering existed only as incidental statement order inside the WPF `Application`, while request idempotence depended on multiple mutable fields. Window code depended on the global concrete application because no narrow command boundary existed. The correction makes the exit intent, resource order, failure isolation, window commands, and restart process boundary explicit types with focused tests.

### Reusable lessons

1. Cleanup order is product behavior and must be represented by one tested pipeline rather than by a broad `try` block.
2. Idempotent shutdown needs one state transition and one immutable exit plan; independent flags allow contradictory combinations.
3. Failure reporting must itself be isolated so it cannot prevent later cleanup.
4. A WPF window should depend on narrow application commands, not discover and cast the global `Application.Current` at each event.
5. Restart validation and process launch belong behind an injectable boundary; tests must prove that `dotnet.exe`, relative paths, missing files, and non-EXE hosts are rejected.
6. A close-to-tray smoke test proves only hiding/background behavior. Do not report graceful tray exit unless that path was actually exercised.
7. Do not mix a repository-wide formatting cleanup into a lifecycle refactor; establish a separate formatting baseline first.

### Remaining boundary

Remote pull-request CI and user physical/UI regression remain acceptance boundaries for this branch. The next architecture milestone is to replace hard-coded Host plugin navigation and page composition with a plugin-neutral workspace model without changing Plugin API v1 or the package format.
