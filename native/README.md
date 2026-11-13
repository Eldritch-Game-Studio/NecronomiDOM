# NativeAOT GDExtension build

This directory compiles the shared addon runtime against the Godot .NET GDExtension SDK. The resulting native library runs in standard, non-.NET Godot; consumers do not need a .NET installation. Native compilation requires the .NET 10 SDK, Git, Bash, SHA-256 tooling (`sha256sum` or `shasum`) and the platform's NativeAOT C toolchain. Preparing the managed SDK feed from source additionally requires Python 3 available as `python` and the SDK generator's host-native ClangSharp dependencies.

```sh
native/build.sh linux-x64
```

The complete desktop distribution requires Linux x64/arm64, Windows x64, and macOS x64/arm64 libraries. NativeAOT uses each target operating system's toolchain: Windows compilation uses genuine Windows .NET/MSVC/Windows SDK binaries inside isolated Proton on Linux, not Linux .NET crosscompilation. Every full package requires final-ZIP qualification with official standard Godot 4.7.2. The Windows qualifier is a **Proton compatibility check**, not native-Windows runtime proof. macOS uses one Apple silicon host: the ARM64 .NET SDK crosscompiles the Intel library, and Rosetta runs the actual Intel Godot process. These checks are not evidence that any unexercised platform/export works. Android, iOS and Web are outside this distribution scope.

## CI policy

| Event/change | Required work |
| --- | --- |
| Ordinary code PR | Unit tests, capability checks, Linux x64 NativeAOT build and all existing native/.NET runtime smokes |
| Code push to `main` (including native/tooling code) | The same fast checks; no cross-platform matrix or distribution ZIP |
| Native bindings, SDK pin/patch, build or packaging PR | Fast checks **plus all five native libraries and freshly installed full/runtime-only final-ZIP qualification on each**, with Windows executed under Proton |
| Documentation/media-only PR or `main` push | Offline documentation/link/content checks, capability freshness and Python helper regressions; no .NET, engine or native build |
| `v*` tag push | All five native libraries, runtime smokes and both exact final-ZIP qualifications, including Windows-Proton; only then publication of both ZIPs |
| Manual dispatch | All five targets by default; `partial=true` is Linux x64-only diagnostic packaging (full/runtime-only variants), never publication |

All CI/CD executes on local runners, including PRs; there is no hosted fallback.
The thin `ci.yml` calls `ci-local.yml@main`, which owns the trusted job definitions.
Every local runner group must allow only
`Eldritch-Game-Studio/NecronomiDOM/.github/workflows/ci-local.yml@refs/heads/main`,
not PR-controlled workflow files. Its checkout-free admission verifies the caller's
repository, event type and exact event SHA (the immutable merge snapshot for PRs).
Every checkout uses that same SHA and does not persist credentials.
Changes to the reusable workflow are not exercised as candidate YAML by a source PR:
the caller uses trusted `main`. After qualified changes merge, observe a full dispatch
from `main` before claiming the new workflow has run end to end.

Internal-head PRs are admitted directly. External PRs require the protected
`local-runner-external-pr` environment with required reviewers and self-review
disabled. Only after configuring that protection should an administrator set
`LOCAL_EXTERNAL_PR_APPROVAL_ENABLED=true`. Without the flag admission fails before
checkout even if the environment is absent; approval cannot be supplied as a
workflow input. The `local-runner-internal` environment needs no approval.
These runner-group/environment protections are deployment prerequisites, not
properties an untrusted PR can enforce for itself.

Classification, documentation, unit tests, ZIP assembly and release publication use
`[self-hosted, macOS, ARM64, eldritch-apple]`, as do both macOS native builds and
final-ZIP qualifications. The provisioned runner supplies Python at
`$HOME/.local/share/eldritch-macos/tools/bin`, .NET 10 at `$HOME/.dotnet`,
Apple Command Line Tools and Rosetta. No hosted macOS runner or `setup-python`
download is used. Native compilation and .NET unit tests run with the ARM64 SDK;
`osx-x64` is an explicit NativeAOT same-OS crossarchitecture target, not an
ARM binary relabeled as Intel. `lipo` checks each produced library. Every stock
Godot invocation is bound with `/usr/bin/arch -arm64` or `-x86_64`, and the fresh
project reports its real process feature before the existing lifecycle/DOM checks.
Standard Godot qualification does not require an Intel .NET runtime.

Expensive Apple commands run through `scripts/apple-locked.py`, which holds the
shared `$HOME/.local/state/eldritch-macos/producer.lock` with exclusive `fcntl`
locking in the foreground and forwards termination to the command process group.
It shares capacity with engine/game SSH workers without sharing source checkouts.

Linux x64 (`[self-hosted,Linux,X64,eldritch-linux]`) produces the pinned managed SDK
feed and executes all Linux native/.NET/graphical smokes. Linux ARM64
(`[self-hosted,Linux,ARM64,eldritch-linux-arm64]`) remains a required native build and
exact final-ZIP runtime check. Mandatory `native-windows-proton` and
`qualify-windows-proton` jobs run on the existing Linux x64 labels. They never
request a Windows runner or relabel the Linux host as Windows. A missing provider
configuration fails closed; there is no optional/deferred Windows full-package mode.
The Linux and native macOS gates cannot be skipped or replaced by Proton.
Actual Linux ARM64 execution remains a local provisioning prerequisite.
Linux needs .NET 10, Python as `python`/`python3`, Clang/zlib, and x64 additionally
Xvfb/Mesa and the net8 demo runtime. CI does not assume an apt-based Linux distribution.

### Externally provisioned Windows-Proton provider

An administrator provisions verified, genuine vendor tools outside the checkout.
The repository variable `NECRONOMIDOM_WINDOWS_TOOLCHAIN` contains an **absolute local
path to a JSON configuration**, not JSON contents, a workflow input or credentials.
The same configuration must be available to both Linux-hosted Windows jobs.
Its schema is:

| Field | Required value |
| --- | --- |
| `schema` | Integer `1` |
| `proton_root` | Absolute path to the verified Proton installation |
| `dotnet_root` | Absolute path to the genuine Windows .NET 10 SDK |
| `dotnet_sdk_version` | Exact installed, serviced Windows .NET SDK version, such as `10.0.404` |
| `msvc_root` | Absolute path to the genuine VC/Tools/MSVC toolset root |
| `windows_sdk_root` | Absolute path to the genuine Windows Kits/10 root |
| `windows_sdk_version` | Installed SDK version, such as `10.0.26100.0` |
| `work_root` | Existing writable disk-backed scratch directory, not tmpfs |
| `fingerprints` | Objects with absolute provider-file `path` and actual 64-hex `sha256` |
| `qualification` | Object with `kind: "windows-proton"`, absolute proof-JSON `receipt` path and its actual 64-hex `sha256` |

Fingerprint coverage includes the Wine executable and Proton version file,
`dotnet.exe`, `cl.exe`, `link.exe`, `lib.exe`, SDK `kernel32.lib`/`ucrt.lib`, and
MSVC `oldnames.lib`/`libcmt.lib`/`libvcruntime.lib`. Both
`dotnet_root/sdk/<dotnet_sdk_version>/trustedroots/codesignctl.pem` and
`dotnet_root/sdk/<dotnet_sdk_version>/trustedroots/timestampctl.pem` require
authenticated SHA-256 fingerprints from the genuine Microsoft SDK distribution.
The receipt records actual provider proof; neither it nor package-scope metadata
substitutes for final-package runtime qualification. Provider files, hashes and
receipt are provisioned externally, never invented by CI. Keep machine-specific
paths and credentials out of tracked files.

The Linux host also supplies bubblewrap and private Xvfb support. Each invocation
uses a fresh materialized vendor default prefix and a private copied workspace on
the configured disk-backed storage. Provider roots are mounted read-only; only
owned job storage is writable. No job installs compiler prerequisites, modifies
shared Proton files, or falls back to unsandboxed Wine. Owned wineserver shutdown
must complete before its namespace is torn down. The helper uses
`DOTNET_SYSTEM_GLOBALIZATION_USENLS=true`, not invariant globalization.
CI execution stays inside the authenticated runner admission and its inherited
CPU/memory cgroup limits; the helper does not detach into a separate resource scope.
Local invocations likewise require the host's authorized bounded workload wrapper.

Before the Windows build, the helper restores only these two approved roots,
each present in **both** authenticated SDK trustedroots bundles:

| Root | Certificate SHA-256 |
| --- | --- |
| DigiCert Assured ID Root CA | `3e9099b5015e8f486c00bcea9d111ee721faba355a89bcf1df69561e3dc6325c` |
| VeriSign Universal Root CA | `2399561127a57125de8cefea610ddf2fa078b5c8067f4e828290bfb860e84b3c` |

The genuine MSVC-compiled `windows-signing-roots` utility uses CryptoAPI to validate
the certificate hashes and restore them into CurrentUser ROOT in the fresh owned
Wine prefix, restricted to CodeSigning and TimeStamp EKUs. It emits fingerprint
evidence before and after restoration and fails unless the resulting trust is valid.
This grants no TLS trust and changes neither host trust nor shared Proton trust.
Signature verification and CRL/revocation checks remain enabled; there is no bypass.

The workflow invokes the same CLI available for provisioned local use:

```sh
python3 scripts/windows-proton.py build \
  --toolchain "$NECRONOMIDOM_WINDOWS_TOOLCHAIN" \
  --sdk-feed "$SDK_FEED" \
  --output addons/necronomidom/bin/win-x64/necronomidom.dll
python3 scripts/windows-proton.py qualify \
  --toolchain "$NECRONOMIDOM_WINDOWS_TOOLCHAIN" \
  --package "$FINAL_PACKAGE" --godot-version 4.7.2
```

`SDK_FEED` is the current portable managed SDK feed produced by the Linux build;
`FINAL_PACKAGE` is the exact downloaded distribution ZIP. The build reuses
`native/build.sh` and its locked feed/patch identity and copies only the resulting
Windows DLL back. The qualifier downloads official Windows Godot with official
checksum verification and runs the existing strict runtime/editor/demo/lifecycle/
incremental-bridge oracles, requiring Windows and x86_64 process features.
Successful toolchain bring-up or a standalone NativeAOT probe is not addon
qualification and is not native-Windows runtime proof.

Qualification executes the authenticated engine EXE directly, not its separate
console process-tree wrapper. Version identity uses the engine's stdout and exit
status; all version stderr is printed unchanged and retained byte-for-byte beneath
`work_root/qualification-logs`, including failed attempts. Runtime, import and
editor commands retain combined diagnostic capture and every existing oracle.

The Linux provider also requires fontconfig's `fc-list` and its ordinary
`/etc/fonts/fonts.conf`. Qualification copies the available system/vendor fonts
into the owned Windows font directory and registers those actual faces, retaining
fontconfig aliases while avoiding unusable host NT paths. Host fonts and font
configuration remain read-only. Consumer qualification exposes no compiler or
.NET SDK drive, PATH, or environment; only builds receive those producer tools.

### Final-package and release gates

Full assembly fails if any of the five libraries is absent or empty, including the
Windows DLL. Only explicit manual `partial=true` dispatches may produce a
`-partial.zip`: the assembler retains only Linux x64 and prunes other descriptor
entries. `addons/necronomidom/ci-package-scope.json` and the job summary state
included/omitted targets and diagnostic status; scope is not a runtime receipt.
Godot `ERROR:`/`SCRIPT ERROR:` output and RID/ObjectDB leak diagnostics fail
qualification even if the process exits zero and prints a success marker. The
editor bridge runs the same complete oracle through a real `EditorPlugin`,
preserving the editor's native SceneTree and normal teardown instead of replacing
its main loop with `--editor --script`.


After assembly, CI derives `<full-stem>-runtime.zip` from that exact ZIP using:

```sh
python3 scripts/project-runtime-package.py --package "$FULL_PACKAGE" --output "$RUNTIME_PACKAGE"
```

The full ZIP is for Godot .NET/C# integration and remains the Godot Asset Store
package. The runtime-only variant supports GDScript and Dirge1.1's standard-Godot
dependency installer without managed source/build inputs. Projection preserves
native binaries, the GDExtension descriptor, GDScript/assets, licenses/notices and
`ci-package-scope.json` byte-for-byte. The added
`addons/necronomidom/runtime-package.json`, schema `necronomidom.runtime-package/1`,
records the original ZIP filename/SHA256 and retained native binary hashes.
Scope is inherited, never upgraded: a partial source stays partial.
Manual diagnostics yield `-partial.zip` and `-partial-runtime.zip`, both Linux
x64-only; projection cannot make them releasable.

ZIP assembly or projection does not constitute runtime proof. `qualify-linux-x64`,
`qualify-platforms` (Linux ARM64), `qualify-apple` (both architecture-bound processes)
and `qualify-windows-proton` all run the same strict existing final-package oracles
on **both** ZIPs. The ZIPs are uploaded together in the `NecronomiDOM` artifact;
every qualifier and the release step download by immutable artifact ID and SHA256
digest, requiring the exact two filenames and complete outer/inner ZIP validation
before installation. A tag release requires success for both ZIPs from all
qualifiers and publishes those exact bytes, with no post-release projection.
Failure of either ZIP blocks publication of both. Manual diagnostics still
qualify both on Linux x64 and can never release, even when dispatched against a
tag. The obsolete Linux Docker `act` launcher cannot reproduce the Apple or
Windows-Proton contract; use manual `partial=true` for diagnostics or
`native/build.sh linux-x64` locally.

`scripts/ci-policy.py` classifies complete Git diffs: PRs compare their head with the
base/head merge base; pushes compare before/after, including an empty-tree first push.
Deleted and renamed files count under both old and new paths. Markdown, `LICENSE`
and allowlisted media under `docs/images/` are documentation; unknown paths get code
checks, never a silent skip. Mixed changes use the stronger gate. Native inputs
include `native/`, shared `addons/necronomidom/runtime/` bindings, workflow/helper
scripts, project/build properties, SDK/NuGet pins, extension descriptors, and
`.gitattributes`/`.gitignore` packaging controls at any directory depth. Shared
runtime changes conservatively take the full PR gate because that code is adapted
and compiled into the native extension; core-only changes take the fast gate.

Only a newer run of the **same PR** automatically cancels its predecessor. Main,
manual and release runs have separate run-ID groups and are never automatically
cancelled. Manual runs, including those selecting a tag, cannot publish a release.
`scripts/check-docs.py` checks authored Markdown local file/image targets, UTF-8,
nonempty content, fenced code, generated JSON and nonempty documentation media.
HTTP(S) links are syntax-checked offline, not fetched; remote availability and
heading-fragment correctness are not claimed. Vendored upstream Markdown is excluded.

The Linux producer caches **only pinned SDK dependencies**, not addon NativeAOT
binaries. The exact `ndom-sdk-v1-Linux-X64-<sha256>` key and persistent work directory
hash the complete lock/patch/build/workflow inputs plus actual .NET, compiler/linker,
Python/Git, OS/runner-image, installed-package and compiler-flag identity. There are
no broad restore keys; a changed identity gets a different directory. The SDK's own
full commit/patch identity checks still run. Other local targets consume that run's
portable managed-feed artifact and build their own native libraries.
All binary handoffs use run-scoped GitHub artifacts, including self-hosted Linux:
failed-job-only reruns can retrieve the original producer's artifact instead of
assuming an attempt-local directory exists. This costs artifact transport but
preserves provenance and never invents a replacement build.

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

Direct native-Windows builds use Git Bash with `cygpath` available. The script normalizes `NECRONOMIDOM_WORK` and any external feed path, passes Windows-native paths to Python/.NET and `NUGET_PACKAGES`, preserves `/p:` MSBuild arguments against MSYS conversion, and enables long-path support for a locally prepared dependency checkout. Linux-hosted Windows-Proton builds instead use the helper's scoped Windows .NET launcher and path mapping while retaining the same build script, feed identity and publish settings. Local SDK preparation invokes the pinned SDK's cross-platform `eng/common/build.py` entry point directly, avoiding its shell wrapper's argument re-splitting. No custom Godot binary or private API is required.

Native publication uses an isolated `nuget.config` under the source-identity cache, containing exactly the selected local managed feed and `https://api.nuget.org/v3/index.json`. XML serialization escapes filesystem paths, and `RestoreConfigFile` selects that configuration instead of passing mixed filesystem/URI `--source` arguments. Windows CI treated the HTTPS CLI source as a local path even with MSYS conversion disabled; this records the observed failure without attributing it to an unproven layer. The `dotnet publish` process still disables MSYS argument conversion because its filesystem arguments are already native; ordinary shell/Git operations keep their normal behavior.

### Managed feed handoff between native hosts

CI generates the platform-independent managed SDK packages once on Linux, then uses those exact packages for target-specific NativeAOT compilation, including the genuine Windows toolchain under Proton. This separates the build-time bindings generator's native dependency from the final addon's target ABI. The observed macOS Intel SDK-generation failure was a missing `libclang.dylib` in the ClangSharp generator dependency; sharing its already generated managed output does not skip native compilation or runtime acceptance.

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

Both DLLs have CLR flags `0x1` (`ILONLY`), with neither `32BITREQUIRED` nor `32BITPREFERRED` set. The inspected archives contain no RID-specific `runtimes/` or native-library payloads. Thus the shared inputs are architecture-independent managed assemblies, not Linux native libraries. `dotnet publish -r <rid>` and the target platform's C toolchain still produce every final addon library. Linux and macOS execute natively; genuine Windows tools and stock Godot execute under isolated Proton, with installed-package qualification explicitly limited to that compatibility environment.


## Verification contract and regression inputs

The public-shutdown candidate built successfully on Linux x64 and passed `scripts/qualify-stock-package.py` with official standard Godot `4.7.2.stable.official.ed1daf0bf`: fresh installed-ZIP font import and shutdown, editor launch/quit, the Grimoire Summon DOM output, and four native host mount/reload cycles with three explicit withdrawals and the final host/inspector still mounted at process shutdown. The debugger evidence above concerns the original binary, not this replacement. Linux x64 does not establish the other four desktop targets; the complete candidate must satisfy the following gates in every declared qualification environment:

Additional Linux x64 checks passed the native authoring/shared-resource smoke and the stock .NET workbench, inspector/reload and typed-binding smokes. A real standard-Godot Vulkan/Forward+ Grimoire run under isolated headless gamescope rendered the expected Summon text, saved a viewport image and exited cleanly; that image was inspected. This is functional rendering evidence, not performance, HDR, export-template or other-platform acceptance.

Keep standard and .NET editor import caches separate. Reopening the source tree in standard Godot after a .NET editor import reproduced a different script-documentation worker crash; the same native scene in both a fresh installed package and a clean source copy imported/opened/exited successfully. That mixed-cache case is not qualified. It does not justify changing this SDK ownership patch or claiming the engine's script-documentation worker is fixed.

1. Build the five desktop runtime identifiers with their corresponding platform toolchains, using isolated Proton for the genuine Windows toolchain, and assemble/package the actual produced libraries.
2. In each declared qualification environment, install that package in a genuinely fresh project with no `.godot` directory. Run standard Godot 4.7.2 `--headless --audio-driver Dummy --import`, requiring success **including shutdown**. Do not reuse the debugger's warmed project and do not ignore a failed import. Windows-Proton success is compatibility evidence, not native-Windows runtime proof.
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
