# ToolBox Project Context

> This file is the durable handoff point for continuing ToolBox work across conversations. It records current project state; the architecture baseline remains in the approved implementation plan.

## Current checkpoint

```text
Checkpoint date: 2026-08-27
Current phase: Unified release-validation module merged after ToolBox v0.1.0
Next phase: User-owned physical Android phone connection acceptance, then Host lifecycle deepening
Plugin API: Frozen v1
Production updater: Deferred to v0.2
```

Detailed milestone failures and reusable lessons are recorded in [`PROJECT_RETROSPECTIVES.md`](PROJECT_RETROSPECTIVES.md). A major milestone is not complete until its retrospective has been added there.

## Verified state

- Public GitHub `v0.1.0` Release is complete at commit `8bf0ca0b24f8c0d826c709446b1445b2f240fa81`; it is neither draft nor prerelease.
- The final main CI and tag-triggered Release workflow both pass. Three consecutive local full Release runs each passed `68/68` tests.
- Release assets are complete and SHA-256-verified: self-contained Windows x64 Host, KeyboardMouse `.tpk`, PhoneAudioRelay `.tpk`, and checksum manifest.
- The WPF Host uses the approved B-scheme UI and Module T icon, persistent bilingual/close/plugin-open settings, tray lifecycle, and separate installed/opened/runtime-enabled plugin state.
- WinRT audio dependencies are process-shared with identity/version checks; WinRT-dependent plugin assemblies load without locking their installation files, and restart-required remains an explicit failure boundary.
- Local, CI, and Tag Release now call `tools/Invoke-ReleaseValidation.ps1` as the single release-validation entry point; it performs clean warnings-as-errors build, all 68 tests, Host publish, both package builds, exact asset checks, package identity/version/hash validation, and release checksum verification.
- Two consecutive post-change dry-runs produced byte-identical Host EXE, KeyboardMouse TPK, PhoneAudioRelay TPK, and checksum manifest. Deterministic package ZIP creation uses stable entry ordering and a fixed timestamp.
- Pull request [#1](https://github.com/OQTQO/ToolBox/pull/1) merged the unified pipeline into `main` at `036b78dfe0d692fea8bd60427b7b9a412cc0b10e`. After fixing the CI-only `CA1859` analyzer finding, both the corrected PR run `33085645418` and final `main` run `33129661198` passed on GitHub's Windows runner.

- .NET SDK `8.0.424` is installed.
- `ToolBox.sln` restores successfully.
- Release build passes with `0` warnings and `0` errors.
- Release test run passes: `59` passed, `0` failed, `0` skipped (`54` Core + `5` Audio Relay).
- WPF Host smoke test passes: startup, healthy state, graceful shutdown, and shutdown logging were all confirmed.
- Phase 4 WPF interaction pass: KeyboardTest enabled, mouse events observed, settings applied, then disabled and unloaded to `Disabled`.
- Keyboard & Mouse Test product packages contain only the product runtime payload and manifest metadata; no duplicate PluginSdk copy is shipped.
- Phase 5 Worker smoke pass: `ToolBox.PluginWorker.exe` is deployed with its runtime files, starts through a suspended `CreateProcess`, joins a kill-on-close Job Object before resume, completes Named Pipe handshake, and exits cleanly.
- Phase 5 isolation tests pass: Worker launch identity mismatch is rejected, Worker termination leaves the Host test process alive, and a Worker-spawned child process is removed by Job Object cleanup.
- Phase 6 fault-fixture pass: `CrashPlugin` preserves `Faulted` in InProcess and across the Worker boundary, `HangPlugin` cancellation preserves `RestartRequired`, and `UnloadLeakPlugin` produces a real `PLUGIN_ALC_UNLOAD_FAILED` before the test releases its deliberate GC handle.
- Phase 7 lifetime pass: `PluginLifetimeScope` is exposed through `IPluginContext`, tracks cleanup/background tasks, cancels before cleanup, and rejects new ownership after stopping.
- Phase 7 deadline pass: InProcess and OutOfProcess shutdown use one configurable deadline; remaining time is propagated to Worker requests, and Hang failures return structured `PLUGIN_SHUTDOWN_TIMEOUT` without claiming `Disabled`.
- Phase 7 policy pass: `DisableFailed → RestartRequired` and `Faulted → Quarantined` transitions are exercised, while Host/Worker sessions preserve failure states.
- Phase 8 resource pass: `ResourceManager` arbitrates Shared/Exclusive leases, reports the resource key and current holders on conflict, and binds leases to `PluginLifetimeScope` cleanup.
- Phase 8 service pass: `ServiceBroker` provides lazy service start, lease reuse, reference counting, configurable idle shutdown, and Scope-owned `ServiceLease` cleanup.
- Phase 8 KeyboardTest pass: KeyboardTest claims an exclusive `keyboard.test.surface` resource, releases it during unload, and exposes resource conflicts as `Faulted` instead of hiding them.
- Phase 9 package pass: `.tpk` packages use safe ZIP extraction with traversal, absolute-path, duplicate/case-collision, reparse-point, entry-count, size, and compression-ratio guards.
- Phase 9 install pass: staging, Manifest/API/platform validation, package metadata and SHA-256 validation, runtime assembly smoke validation, atomic activation, and failure cleanup preserve the previous version.
- Phase 9 version pass: plugin versions install side-by-side under `PluginId/versions/Version`; `state.json` records candidate/active/last-known-good versions, transaction phase, revision, and rollback capability.
- Phase 9 data pass: only Config/State are snapshotted under the separate plugin data root; Cache/UserData are not copied, and uninstall leaves user data intact.
- Phase 9 attack-fixture pass: BadZipPackage traversal/collision cases, BadManifestPackage, IncompatibleApiPlugin, and hash-mismatch cases all reject safely; Release build is `0` warnings / `0` errors and the full suite is `41` passed / `0` failed / `0` skipped.
- Phase 9 Host smoke pass: WPF Host starts, records healthy state, closes gracefully with exit code `0`, and records shutdown completion.
- Phase 10 API pass: the stable `ToolBox.PluginSdk` export set, interface member shapes, constants, enum values, Manifest JSON names, and API incompatibility code are locked by `PluginApiV1CompatibilityTests`; the experimental KeyboardTest namespace remains explicitly outside the stable freeze.
- Phase 10 compatibility pass: a LegacyPlugin DLL compiled against the pinned `ToolBox.PluginSdk` 0.0.1 reference loads through the current shared SDK, completes Start/Stop, and unloads its collectible ALC without shipping a private SDK copy.
- Phase 10 verification pass: Release build is `0` warnings / `0` errors, the full suite is `53` passed / `0` failed / `0` skipped, and WPF Host smoke remains healthy with graceful exit code `0`.
- Post-freeze scope pass: `PRODUCT_KEYBOARD_MOUSE_SCOPE.md` defines the first formal product plugin boundary, smallest user path, package activation requirement, resource/lifecycle acceptance, and explicit non-goals without changing API v1.
- Productization pass: `PluginPackageInstaller.GetActiveVersionDirectory` resolves the committed active version; Host uses that path only and shows an explicit not-installed state when no package is active.
- Product acceptance pass: a real `.tpk` Keyboard & Mouse Test fixture installs side-by-side, runs local key/mouse and settings behavior, reports exclusive resource conflicts as `Faulted`, unloads its ALC, and clears activation on uninstall.
- Product UI pass: the WPF shell now presents Keyboard & Mouse Test as Product 01 with concise package, lifecycle, input-surface, settings, and local-only safety copy.
- Product verification pass: Release build is `0` warnings / `0` errors, `53` tests pass, Host exits cleanly with code `0`, and the latest log records both Healthy and shutdown completion.
- Hardening pass: active-version lookup rejects non-committed state and missing active directories; product packages pass `0.1.0 → 0.2.0` upgrade, active uninstall fallback, and reload acceptance.
- Install UX pass: Host owns the package installer for its session, exposes `Install .tpk / Install update`, keeps the action disabled while a plugin is running, and reports package error codes without discarding the prior active path.
- Release package pass: `tools/New-KeyboardMousePackage.ps1` creates the exact v0.1 package payload and `PACKAGE_RELEASE_POLICY.md` records the local/inner-test boundary, hash-versus-signature rule, version policy, and rollback path.
- Release metadata pass: unsupported package formats and Manifest/package identity mismatches are rejected before installation; the generated package was inspected with matching `0.1.0` metadata and no private SDK copy.
- Personal learning release pass: the user confirmed no server or official authentication is required; the v0.1 package is approved for personal local learning and is explicitly not presented as a production-signed distribution.
- GitHub release automation pass: added `CHANGELOG.md`, pull/push CI, and tag-triggered Release workflow that publishes a self-contained Windows x64 Host, `.tpk`, and SHA-256 manifest.
- Audio Relay platform pass: Product 02 uses `AudioPlaybackConnection` with the official `DeviceWatcher` discovery flow, exclusive `audio.bluetooth.a2dp-sink` ownership, Start/Open/StateChanged handling, and deterministic connection cleanup.
- Audio Relay package pass: `PhoneAudioRelay-0.1.0.tpk` carries the plugin plus `Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll`, omits private `ToolBox.PluginSdk.dll`, installs through the real installer, and starts as `Running / Ready` through the collectible ALC.
- Audio Relay hardware probe pass: the current PC reports Windows A2DP sink API support; no paired A2DP Audio Source is currently present, so discovery correctly normalizes to `Ready / 0 devices` instead of `Faulted`.
- Audio Relay UI pass: Host Product 02 provides package install/update, enable/disable, paired-source refresh and selection, start/stop receiving, a `PHONE → A2DP → WINDOWS MIX` route, and high-DPI visual QA with a dark native ComboBox.
- Audio Relay verification pass: Release build is `0` warnings / `0` errors, all `59` tests pass, runtime dependencies are present, real-platform discovery is covered, and start/stop/ALC unload succeeds in C# integration coverage.
- Build output remains ignored by Git (`bin/`, `obj/`).

Last verification commands:

```powershell
dotnet restore ToolBox.sln
dotnet build ToolBox.sln --configuration Release
dotnet test ToolBox.sln --configuration Release
```

## Completed scope

### Phase 0 — Engineering foundation

- Solution and project structure created.
- `ToolBox.Core`, `ToolBox.Host`, and `ToolBox.Core.Tests` are present.
- Shared build properties, `.editorconfig`, `.gitignore`, README, and Git repository initialized.

### Phase 1 — Host Shell + Diagnostics

- WPF Host Shell with a diagnostic cockpit UI.
- Session ID, Launch Attempt ID, startup stage, and Host status display.
- Structured JSONL logging with asynchronous queue, Info+ default filtering, rolling files, retention, and capacity limits.
- Global dispatcher, AppDomain, and unobserved-task exception logging.
- Host diagnostics snapshots and lifecycle notifications.
- Tests for diagnostics state transitions, JSONL output, and log rolling.

### Phase 2 — PluginSdk + Lifecycle + Manifest

- Added `ToolBox.PluginSdk` with BCL/SDK-only public contract types.
- Defined `PluginApiMajor = 1` and Manifest format version `1`.
- Added explicit lifecycle states and guarded state transitions.
- Added `PluginState` with failure metadata for `Faulted`, `DisableFailed`, `RestartRequired`, and `Quarantined` states.
- Added Manifest v1 parsing and validation for API major, Windows/x64 platform, runtime modes, required fields, and preferred mode.
- Added structured validation errors for malformed and incompatible manifests.
- Added Phase 2 tests for valid, malformed, incompatible, and invalid-lifecycle fixtures.

### Phase 3 — InProcess Runtime + HappyPath

- Added the minimal `IPlugin` and `IPluginContext` SDK contract.
- Added plugin discovery from manifest-bearing directories.
- Added collectible `AssemblyLoadContext` isolation with `AssemblyDependencyResolver`.
- Shared the Host-loaded `ToolBox.PluginSdk` assembly with InProcess plugins.
- Added load, identity validation, start, stop, dispose, unload, and unload verification.
- Added the single-responsibility `HappyPathPlugin` fixture and end-to-end runtime test.
- Preserved truthful lifecycle states when plugin start, stop, or unload fails.

### Phase 4 — KeyboardTest Architecture Spike

- Added the explicitly experimental `IKeyboardTestPlugin` contract for input, settings, and snapshot state.
- Added the real `KeyboardTest` plugin fixture with key, mouse, enable, disable, and settings behavior.
- Added Host capability access without exposing Host/Core internals to the plugin.
- Added a UI-owned WPF signal bench with scoped keyboard/mouse observation; no global hook, Raw Input, or native DLL is used.
- Added enable, settings apply, disable, ALC unload, and lifecycle diagnostics in the Host.
- Confirmed the API is still a spike contract and remains unfrozen.

### Phase 5 — OutOfProcess Runtime

- Added `ToolBox.PluginWorker` with a minimal control-channel worker loop.
- Added Host-side `OutOfProcessPluginRuntime` and `OutOfProcessPluginSession` lifecycle control.
- Added `WorkerProtocol` JSON Lines messages for `Hello`, `HelloAck`, `Request`, `Response`, `Event`, `Error`, `Cancel`, `Heartbeat`, and `Shutdown`.
- Added `WorkerLaunchId` and protocol-major validation for both handshake directions and runtime envelopes.
- Added Windows Job Object startup ownership: create Worker suspended, configure kill-on-close policy, assign Worker, then resume the primary thread.
- Added graceful shutdown, forced termination, failure-state preservation, and cleanup of the Worker process tree.
- Added `WorkerChildProcessPlugin` and `ProtocolMismatchWorker` fixtures plus end-to-end process isolation tests.
- Kept the OutOfProcess boundary explicitly separate from a security sandbox; permissions, package installation, resource arbitration, and service brokering remain out of scope.

### Phase 6 — Fault Fixtures

- Added single-responsibility `CrashPlugin`, `HangPlugin`, and `UnloadLeakPlugin` fixtures.
- Added real InProcess failure-state tests for startup failure, cancellation during stop, and collectible ALC unload failure.
- Added Worker-boundary `CrashPlugin` coverage to verify plugin errors are returned as protocol errors while the Host session enters `Faulted`.
- Added fixture deployment and cleanup verification without leaving Worker processes, GC handles, or temporary fixture roots behind.

### Phase 7 — Lifetime / Restart / Quarantine

- Added `IPluginLifetimeScope` to the SDK context and implemented Core-side `PluginLifetimeScope` resource ownership, cancellation, reverse-order cleanup, and background-task tracking.
- Added configurable `PluginShutdownOptions` and `ShutdownDeadline` with a single remaining-time budget.
- Integrated the deadline through InProcess stop/cleanup/dispose/ALC unload and OutOfProcess stop/shutdown/Worker termination.
- Added structured timeout propagation across Named Pipe requests and preserves `DisableFailed`, `RestartRequired`, `Faulted`, and `Quarantined` semantics.
- Added restart/quarantine controls to loaded plugin and Worker session boundaries without treating failure cleanup as `Disabled`.

### Phase 8 — Resource / Service

- Added SDK contracts for `ResourceKey`, Shared/Exclusive resource access, `ResourceConflictException`, `IResourceLease`, `IResourceManager`, `IServiceBroker`, and `IServiceLease<T>`.
- Added Core `ResourceManager` with plugin-bound views, conflict arbitration, current-holder diagnostics, and idempotent release.
- Added Core `ServiceBroker` with registration, lazy start, shared service instances, reference-counted leases, idle shutdown, and stop-failure recording.
- Bound ResourceLease and ServiceLease ownership to `PluginLifetimeScope` so cancellation and reverse cleanup release plugin claims.
- Passed the real KeyboardTest spike through the resource boundary without adding global hooks, native input, or product-specific services.

### Phase 9 — Package

- Added the `.tpk` ZIP package boundary with staging extraction and limits for path traversal, absolute paths, directory escape, symlink/reparse points, duplicate/case-collision paths, entry count, per-file size, total decompressed size, and compression ratio.
- Added package Manifest/API/platform validation, package metadata validation, SHA-256 payload validation, runtime assembly structural smoke validation, and rejection of duplicate private `ToolBox.PluginSdk.dll` payloads.
- Added side-by-side version installation under `Plugins/PluginId/versions/Version`, atomic `state.json` writes with transaction phase/revision, activation, uninstall, and failure cleanup that preserves the previous version.
- Added separate plugin data storage with Config/State-only snapshots under `PluginData/PluginId/rollback`; Cache/UserData are not copied and uninstall does not remove current user data.
- Added deterministic attack tests for `BadZipPackage`, `BadManifestPackage`, `IncompatibleApiPlugin`, and package hash mismatch.

### Phase 10 — Plugin API v1 Freeze

- Added the frozen stable API inventory and compatibility rules in [`PLUGIN_API_V1.md`](PLUGIN_API_V1.md).
- Added reflection and semantic compatibility tests for the stable PluginSdk export set, interface signatures, generic constraints, constants, enum values, Manifest JSON field names, and the `PLUGIN_API_MAJOR_UNSUPPORTED` error code.
- Added `PluginSdkCompatibility` with a pinned old SDK reference and a legacy-compiled plugin DLL; the fixture is loaded directly by the current ALC instead of being rebuilt against the current SDK.
- Kept `ToolBox.PluginSdk.Experimental` explicitly outside the v1 compatibility promise; Keyboard & Mouse Test and Phone Audio Relay consume it only as version-coupled product bridges.

### Post-freeze — Keyboard & Mouse Test product scope

- Selected `com.toolbox.keyboard-test` as the first formal product plugin candidate.
- Recorded the smallest product path in [`PRODUCT_KEYBOARD_MOUSE_SCOPE.md`](PRODUCT_KEYBOARD_MOUSE_SCOPE.md): package activation, local input surface, settings, resource conflict, lifecycle truth, ALC unload, and Config/State separation.
- Kept global hooks, Raw Input, native DLLs, Android/Bluetooth, permissions, sandboxing, marketplace, and Updater outside the scope.

### Post-freeze — Keyboard & Mouse Test productization

- Reused the Phase 9 package layout as the only Host discovery path: `Plugins/com.toolbox.keyboard-test/state.json` selects `versions/0.1.0`.
- Removed the Host build-time copy of the raw KeyboardTest fixture, so a package must be installed and activated before Enable is available.
- Renamed the shipped manifest to `Keyboard & Mouse Test` while keeping the experimental contract outside the frozen stable SDK surface.
- Added package-backed product acceptance tests for installation, active-version resolution, local input, settings, resource conflict, Stop/Unload, ALC collection, and uninstall.
- Verified the complete checkpoint with `53` passing tests and a clean Release Host smoke.

### Post-freeze — Keyboard & Mouse Test hardening

- Added fail-closed activation resolution for transaction phases other than `Committed` and for missing active version directories.
- Added product-package upgrade and active-version uninstall fallback coverage, including loading the restored previous version.
- Added the Host `.tpk` picker and session-owned installer; installation is blocked while an in-process product instance is loaded.
- Preserved the stable Plugin API boundary and kept installation errors visible through structured package error codes.

### Post-freeze — Product package release readiness

- Added [`PACKAGE_RELEASE_POLICY.md`](PACKAGE_RELEASE_POLICY.md) to distinguish v0.1 local/inner-test integrity checks from v0.2 production authenticity.
- Added [`tools/New-KeyboardMousePackage.ps1`](tools/New-KeyboardMousePackage.ps1) for explicit, non-overwriting Keyboard & Mouse Test `.tpk` generation from Release output.
- Kept package payload minimal: Manifest, runtime assembly, deps file, and SHA-256 metadata; the packer never copies `ToolBox.PluginSdk.dll`.
- Added metadata rejection coverage for unsupported package format and package/Manifest identity mismatch.
- Verified the generated package, Release build, `53` tests, Host smoke, and temporary-resource cleanup.

### Personal learning release

- Approved the current `.tpk` flow for personal learning without a server or official authentication service.
- Generated `artifacts/KeyboardMouse-0.1.0.tpk` from the Release output; the directory is Git-ignored and the package can be installed through Host's `Install .tpk` action.
- Kept the authenticity limitation explicit: SHA-256 protects package integrity only; public distribution would require a future signature contract.

### Post-freeze — Phone Audio Relay Product 02

- Added the experimental `IAudioRelayPlugin` capability, immutable device/snapshot records, and explicit Disabled/Refreshing/Ready/Connecting/Streaming/Unsupported/Error states without changing the frozen stable v1 namespace.
- Added `com.toolbox.audio-relay` as an in-process Windows x64 plugin targeting Windows 10 build 19041 or later and claiming the exclusive `audio.bluetooth.a2dp-sink` resource.
- Implemented paired A2DP Audio Source discovery with `AudioPlaybackConnection.GetDeviceSelector()` and `DeviceWatcher`; the local Windows no-source `ERROR_FILE_NOT_FOUND` case is treated as an empty ready list.
- Implemented `TryCreateFromId → StartAsync → OpenAsync`, open-status/error mapping, connection-state recovery, disconnect, dispose, and Host shutdown cleanup.
- Added Product 02 Host UX, package identity preflight, device selection, connection controls, status/error diagnostics, Windows-mix safety copy, dark ComboBox styling, and high-DPI window sizing.
- Added [`tools/New-AudioRelayPackage.ps1`](tools/New-AudioRelayPackage.ps1), [`PHONE_AUDIO_RELAY.md`](PHONE_AUDIO_RELAY.md), Release workflow assets, real-platform tests, fake-transport lifecycle tests, and collectible-ALC start/unload acceptance.
- Generated `artifacts/PhoneAudioRelay-0.1.0.tpk`; physical audio playback remains to be accepted with a paired Android phone because this machine currently has no paired A2DP Audio Source.

## Next phase: Physical Android acceptance, then optional v0.2 authenticity planning

The implementation and package paths are complete. The next acceptance step requires a paired Android phone:

1. Pair the phone in Windows Bluetooth settings and confirm it appears as an A2DP Audio Source.
2. Install `PhoneAudioRelay-0.1.0.tpk`, enable the plugin, refresh, select the phone, and start receiving.
3. Verify phone media and PC application audio are audible together through the current Windows output, then stop/disable and confirm clean release.

After physical acceptance, optional v0.2 work can be scoped around the future authenticity contract:

1. Define the official public-key and signed-update-manifest format.
2. Decide how signature verification composes with existing package SHA-256 validation.
3. Review the experimental-to-stable contract boundary before promising any long-lived product-specific API.

仍然不在 v0.1 范围内：

- Dependency solver, marketplace, permissions enforcement, updater, or further formal product plugins.
- Data channels or security sandbox enforcement.
- Global input capture, Raw Input, native input, macros, and remote/telemetry features.

## Architectural guardrails

- PluginSdk is the only long-term plugin boundary.
- Host must never claim a plugin is Disabled until its active lifecycle is gone.
- Plugin API v1 is frozen; incompatible changes require a new API major and compatible 1.x changes must preserve the recorded baseline.
- Phase changes must follow: implement → build → test → fix → accept.
- Every major milestone must end with a written retrospective in `PROJECT_RETROSPECTIVES.md`: record failures, root causes, why earlier checks missed them, corrections, verification evidence, prevention measures, and remaining risks.
- A milestone is not complete and the checkpoint must not advance until both the retrospective and `PROJECT_CONTEXT.md` are updated.
- Failed lifecycle operations remain visible as failure states; do not hide them behind Disabled.
- Keep the Host small and do not build future phases early.

## Resume checklist

When continuing this project:

1. Read this file and the approved implementation plan.
2. Check `git status` before editing.
3. Confirm the current phase and its acceptance criteria.
4. Make the smallest in-scope change.
5. Run restore/build/test before moving the checkpoint forward.
6. Update this file with verified results, decisions, blockers, and the next concrete step.
7. After every major milestone, add a retrospective to `PROJECT_RETROSPECTIVES.md` before marking it complete.
