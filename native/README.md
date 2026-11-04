# NativeAOT GDExtension build

This directory compiles the shared addon runtime against the Godot .NET GDExtension SDK. The resulting native library runs in standard, non-.NET Godot; consumers do not need a .NET installation. Native compilation requires the .NET 10 SDK, Git, Bash, SHA-256 tooling (`sha256sum` or `shasum`) and the platform's NativeAOT C toolchain. Preparing the managed SDK feed from source additionally requires Python 3 available as `python` and the SDK generator's host-native ClangSharp dependencies.

```sh
native/build.sh linux-x64
```

NativeAOT builds for the host operating system. The desktop distribution targets are Linux x64/arm64, Windows x64, and macOS x64/arm64; each must be built and installed-package-qualified on its own native runner with official standard Godot 4.7.2. Those targets are not evidence that any unexercised platform/export works. Android, iOS and Web are outside this distribution scope.

## Decision: withdraw SDK ownership at public main-loop shutdown

`godot-dotnet.lock` pins upstream source commit `965a09db8b42f79d7987270063eb19abb985ceb6` and the checked-in `patches/godot-dotnet-scene-teardown.patch`. The patch changes the SDK's own lifecycle, not the engine and not an addon-side private-access workaround:

- Register the public `GDExtensionMainLoopCallbacks.shutdown_func`, available since Godot 4.5 and documented to run while Godot is still initialized.
- At that callback, terminate initialized **Editor**, then **Scene** ownership before the first shutdown message-queue flush. Each owning user terminator runs before SDK wrapper disposal; editor plugins are removed before class/resource teardown.
- Use the same idempotent routine for ordinary level-deinitialization callbacks, providing the unload fallback without invoking a user's owning terminator twice.
- Dispose the synchronization context at **Core**.

An initialized-level bitmask records callback ownership, not per-class registration stages. Every class in this addon's `ExtensionEntryPoint` is registered at Scene. Upstream's own `Godot.EditorIntegration.Main.DeinitializeTypes` already explicitly removes its editor plugins/classes and disposes its tracked instances at Editor. This correction preserves that ordering; it does not change which classes are registered, force process exit, suppress import failures or add a GC shutdown hook. Public stock-API fallbacks for optional shaping/accessibility enhancements remain unchanged.

### Rationale and evidence

Before this correction, upstream's bridge registered only a frame callback and deferred all automatic plugin/class/resource cleanup until Core. Moving cleanup only to the Scene deinitialization callback was insufficient: that candidate built successfully, but the integration owner's fresh installed-package import still aborted. It is superseded by the public shutdown-callback patch.

Observed failure on the released native binary: a fresh addon/font import with official `4.7.2.stable.official.ed1daf0bf` reaches editor initialization, then faults at stock engine PC `0x1c7ae9f` (`mov 0x20(%rbx), %eax`, with `rbx == 8`). A debugger-only fresh import reproduced that exact fault. The native library's later signal handler is not the fault site. The corresponding static owner pointer at `0x9012880` was null.

[INFERENCE, source/disassembly mapping] The caller at `0x1c84e60` matches `EditorHelp::_gen_extensions_docs`: load the static `doc` pointer, call `DocTools::generate` with flags `3`, then conditionally merge `ext_doc`. In stock source, `EditorHelp::_load_doc_thread` queues this static callback; `EditorHelp::cleanup_doc` joins the worker and deletes/nulls `doc`, but the queued callback can remain. `DocTools::generate` attempts to insert extension class docs through the null owner's class map, matching the null-plus-eight fault.

The relevant stock source order is:

1. `OS_LinuxBSD::run` finishes the main loop with `SceneTree::finalize`, which exits/deletes the root and its EditorNode. EditorNode's destructor calls `EditorHelp::cleanup_doc`.
2. `Main::cleanup` calls `GDExtensionManager::shutdown`, dispatching the public extension shutdown callbacks.
3. `Main::cleanup` performs its **first** message-queue flush, before deleting the main-loop object. This is already too early for ordinary Editor/Scene deinitialization to withdraw stale registrations.
4. Editor and Scene deinitialization run later, followed by server teardown, another queue flush and Core cleanup.

The new shutdown callback withdraws the addon's Scene registrations at step 2, after ordinary tree withdrawal but before the first queued work at step 3, and releases tracked resources while their supporting types/servers exist. This is an SDK lifetime correction, not a claim that FontFile finalization caused the original fault. A prior addon-only collect/wait-finalizers experiment did not fix cold import and is not part of this patch.

Source references:

- [Pinned upstream bridge](https://github.com/godotengine/godot-dotnet/blob/965a09db8b42f79d7987270063eb19abb985ceb6/src/Godot.Bindings/Bridge/GodotBridge.cs)
- [Pinned upstream editor integration cleanup](https://github.com/godotengine/godot-dotnet/blob/965a09db8b42f79d7987270063eb19abb985ceb6/src/Godot.EditorIntegration/Main.cs)
- [Stock engine documentation callback and cleanup](https://github.com/godotengine/godot/blob/ed1daf0bf/editor/doc/editor_help.cpp)
- [Stock engine shutdown ordering](https://github.com/godotengine/godot/blob/ed1daf0bf/main/main.cpp)
- [Public shutdown callback contract](https://github.com/godotengine/godot-dotnet/blob/965a09db8b42f79d7987270063eb19abb985ceb6/gdextension/gdextension_interface.h)
- [Stock SceneTree finalization](https://github.com/godotengine/godot/blob/ed1daf0bf/scene/main/scene_tree.cpp)
- [Original reproduction and controls](https://github.com/Eldritch-Game-Studio/NecronomiDOM/issues/5)

### Trade-off and upstream proposal

The dependency carries one explicit local SDK correction until an upstream solution is available. Proposed upstream change: use the public main-loop shutdown callback for idempotent Editor/Scene ownership withdrawal, with level-deinitialization callbacks as the unload fallback, rather than retaining scene classes/resources and editor plugins until Core or assuming Scene deinitialization precedes every shutdown queue flush. Invoke each initialized owning terminator once before disposing its SDK resources. Include cold standard-editor import/quit with a registered Scene class as a regression, plus live native host withdrawal and reload. A broader SDK API for classes registered across several ownership levels is a separate design question. No upstream submission is implied by this document.

The engine's unguarded, queued static documentation callback is independently unsafe once its owner is destroyed. This patch does not modify that engine code or assume that any unrelated engine shutdown defect is fixed.

## Reproducible dependency and cache identity

`build.sh` always checks the patch SHA-256 against the lock and derives the bindings version from that lock. Without an external feed it prepares the SDK source locally, with Linux used as the CI producer. Its private cache is:

```text
${NECRONOMIDOM_WORK:-native/.work}/sdk/<upstream-prefix12>-<patch-prefix16>/
```

Short physical names bound Windows compiler/package paths. A `.source-identity` file retains the **full** upstream commit and patch SHA-256; a missing/mismatched full identity refuses reuse before touching a colliding directory. That identity owns its source checkout, feed and `packages/` NuGet cache. The two consumed SDK packages use a distinct prerelease version with those prefixes (`4.7.0-dev.ndom.g<commit12>.p<patch16>`), never upstream `4.7.0-dev`. The script passes this version to `NecronomiDom.Native.csproj`; direct publication without the selected dependency version is rejected.

For local SDK preparation, the script validates the source commit, changed-file set and corrected bridge checksum before reuse, and preserves a copy of the lock and patch alongside a successfully prepared feed. Altered or partially prepared source is refused and retained for inspection. It never patches the old/shared `.work/godot-dotnet` checkout, deletes old feeds or removes packages from the global NuGet cache. An existing `NECRONOMIDOM_WORK` root can continue to be used without reinterpreting its older SDK cache as patched.

Windows builds run under Git Bash with `cygpath` available. The script normalizes `NECRONOMIDOM_WORK` and any external feed path, passes Windows-native paths to Python/.NET and `NUGET_PACKAGES`, preserves `/p:` MSBuild arguments and HTTP(S) source URLs against MSYS conversion, and enables long-path support for a locally prepared dependency checkout. A short CI work root such as `${runner.temp}/nd` is recommended. Local SDK preparation invokes the pinned SDK's cross-platform `eng/common/build.py` entry point directly, avoiding its shell wrapper's argument re-splitting. No custom Godot binary or private API is required.

### Managed feed handoff between native hosts

CI generates the platform-independent managed SDK packages once on Linux, then uses those exact packages for NativeAOT compilation on each real target host. This separates the build-time bindings generator's native dependency from the final addon's target ABI. The observed macOS Intel SDK-generation failure was a missing `libclang.dylib` in the ClangSharp generator dependency; sharing its already generated managed output does not skip native compilation or runtime acceptance.

To consume a trusted prebuilt feed instead of cloning/building the SDK:

```sh
NECRONOMIDOM_SDK_FEED="/path/to/sdk-managed-feed" native/build.sh osx-x64
```

The same interface accepts Windows paths under Git Bash and applies to all five desktop RIDs. The supplied directory must contain:

- `.identity` equal to the **full** `$GODOT_DOTNET_COMMIT-$GODOT_DOTNET_PATCH_SHA256` from the checked-in lock.
- Nonempty `Godot.Bindings.<bindings-version>.nupkg` and `Godot.SourceGeneration.<bindings-version>.nupkg`, using the version derived from that lock.

An explicitly supplied empty/nonexistent path, wrong/missing identity, or missing required package fails immediately; there is no fallback to local SDK generation. The external feed is not modified. Its artifact transport is a trust boundary: the identity marker is provenance matching, not a cryptographic signature authenticating arbitrary third-party packages. Each consumer still checks the checked-in patch SHA-256 and keeps NuGet packages isolated under its full-identity-guarded work cache.

After validation, `build.sh` emits `sdk-feed=<absolute active feed path>` to `GITHUB_OUTPUT` when that standard CI variable is present. A locally prepared feed also receives `godot-dotnet.lock` and the named patch. CI stages only the two required `.nupkg` files, `.identity`, lock and patch from that output path into the `sdk-managed-feed` artifact, including hidden files so `.identity` survives. It does not reconstruct the cache key/version independently or transfer other SDK/editor/tool packages.

Payload evidence from the pinned corrected feed:

| Package | Managed payload | Nuspec dependencies |
| --- | --- | --- |
| `Godot.Bindings` | `lib/net10.0/Godot.Bindings.dll` and XML documentation | Empty `net10.0` group |
| `Godot.SourceGeneration` | `analyzers/dotnet/cs/Godot.SourceGeneration.dll` | Empty `.NETStandard2.0` group |

Both DLLs have CLR flags `0x1` (`ILONLY`), with neither `32BITREQUIRED` nor `32BITPREFERRED` set. The inspected archives contain no RID-specific `runtimes/` or native-library payloads. Thus the shared inputs are architecture-independent managed assemblies, not Linux native libraries. `dotnet publish -r <rid>` and the host C toolchain still produce every final addon library on its matching operating system/architecture, followed by installed-package stock-Godot qualification there.


## Verification contract and regression inputs

The public-shutdown candidate built successfully on Linux x64 and passed `scripts/qualify-stock-package.py` with official standard Godot `4.7.2.stable.official.ed1daf0bf`: fresh installed-ZIP font import and shutdown, editor launch/quit, the Grimoire Summon DOM output, and four native host mount/reload cycles with three explicit withdrawals and the final host/inspector still mounted at process shutdown. The debugger evidence above concerns the original binary, not this replacement. Linux x64 does not establish the other four desktop targets; the complete candidate must satisfy the following gates on its matching native hosts:

Additional Linux x64 checks passed the native authoring/shared-resource smoke and the stock .NET workbench, inspector/reload and typed-binding smokes. A real standard-Godot Vulkan/Forward+ Grimoire run under isolated headless gamescope rendered the expected Summon text, saved a viewport image and exited cleanly; that image was inspected. This is functional rendering evidence, not performance, HDR, export-template or other-platform acceptance.

Keep standard and .NET editor import caches separate. Reopening the source tree in standard Godot after a .NET editor import reproduced a different script-documentation worker crash; the same native scene in both a fresh installed package and a clean source copy imported/opened/exited successfully. That mixed-cache case is not qualified. It does not justify changing this SDK ownership patch or claiming the engine's script-documentation worker is fixed.

1. Build the five desktop runtime identifiers on their corresponding native hosts and assemble/package the actual produced libraries.
2. On each target host, install that package in a genuinely fresh project with no `.godot` directory. Run standard Godot 4.7.2 `--headless --audio-driver Dummy --import`, requiring success **including shutdown**. Do not reuse the debugger's warmed project and do not ignore a failed import.
3. Launch/quit the standard editor on the installed project, requiring a clean shutdown.
4. Run the packaged GDScript demo smoke, checking its real Summon count text and process status.
5. Exercise native host withdrawal, inspector ownership and reload with the input below, then run the existing native/.NET authoring and runtime regressions. A clean import alone does not establish runtime correctness, rendering or other platforms.

`tests/` is an isolated, real-engine regression project. Copy its contents and the built addon into a fresh scratch project (not into a warmed checkout):

```sh
scratch="$(mktemp -d)"
cp -R native/tests/. "$scratch/"
mkdir -p "$scratch/addons"
cp -R addons/necronomidom "$scratch/addons/"
"$GODOT_STANDARD" --headless --audio-driver Dummy --path "$scratch" --import
"$GODOT_STANDARD" --headless --audio-driver Dummy --path "$scratch" --editor --quit-after 3
"$GODOT_STANDARD" --headless --audio-driver Dummy --path "$scratch" --script res://lifecycle.gd
```

Run each command with a finite supervising timeout and require exit status zero; the script must additionally emit:

```text
native-lifecycle: cycles=4 reloads=4 withdrawn=3 final-host-mounted=true
```

The script mounts four native hosts against the real packaged font, updates text through the real DOM signal API, opens the inspector, forces an actual document replacement, verifies signal rebinding after reload, and frees three hosts before reentry. It leaves the final host and inspector mounted so ordinary SceneTree shutdown must withdraw them. These are behavior-based inputs, not tests that search source text. Use a separate newly copied project when repeating cold-import acceptance; the commands above intentionally warm their own project for subsequent authoring/runtime checks.
