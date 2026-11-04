#!/usr/bin/env bash
# Builds the NecronomiDOM GDExtension library for standard (non-.NET) Godot.
#
#   native/build.sh [RID...]      e.g. linux-x64, linux-arm64, win-x64, osx-arm64, osx-x64 (default: this machine)
#
# NativeAOT compiles only for the host OS, so each OS builds its own RIDs. Output:
#   addons/necronomidom/bin/<rid>/<library>  and  addons/necronomidom/necronomidom.gdextension
# Requires the .NET 10 SDK and the platform C toolchain NativeAOT needs (clang on Linux, Xcode on macOS,
# MSVC build tools on Windows).
set -euo pipefail
HOST_OS="$(uname -s)"
HERE="$(cd "$(dirname "$0")" && pwd)"
ADDON="$HERE/../addons/necronomidom"
WORK="${NECRONOMIDOM_WORK:-$HERE/.work}"
# The SDK correction and its complete source identity are checked in, not applied to a shared checkout.
source "$HERE/godot-dotnet.lock"

# Keep MSBuild property arguments intact under Git Bash; pass native paths to Python/.NET explicitly.
case "$HOST_OS" in
  MINGW*|MSYS*|CYGWIN*)
    WORK="$(cygpath -u "$WORK")"
    if [[ -n "${MSYS2_ARG_CONV_EXCL:-}" ]]; then
      export MSYS2_ARG_CONV_EXCL="$MSYS2_ARG_CONV_EXCL;/p:"
    else
      export MSYS2_ARG_CONV_EXCL="/p:"
    fi
    if [[ -n "${MSYS2_ENV_CONV_EXCL:-}" ]]; then
      export MSYS2_ENV_CONV_EXCL="$MSYS2_ENV_CONV_EXCL;NUGET_PACKAGES"
    else
      export MSYS2_ENV_CONV_EXCL="NUGET_PACKAGES"
    fi
    ;;
esac

native_path() {
  case "$HOST_OS" in
    MINGW*|MSYS*|CYGWIN*) cygpath -m "$1" ;;
    *) printf '%s\n' "$1" ;;
  esac
}

sha256_file() {
  local result
  if command -v sha256sum >/dev/null 2>&1; then
    result="$(sha256sum "$1")"
  else
    result="$(shasum -a 256 "$1")"
  fi
  printf '%s\n' "${result%% *}"
}

PATCH="$HERE/$GODOT_DOTNET_PATCH"
if [[ "$(sha256_file "$PATCH")" != "$GODOT_DOTNET_PATCH_SHA256" ]]; then
  echo "SDK patch checksum differs from native/godot-dotnet.lock; update its provenance before building." >&2
  exit 1
fi

mkdir -p "$WORK"
WORK="$(cd "$WORK" && pwd)"
SDK_ID="$GODOT_DOTNET_COMMIT-$GODOT_DOTNET_PATCH_SHA256"
# Short physical names keep Windows compiler/package paths bounded. Full identity is checked before reuse.
SDK_KEY="${GODOT_DOTNET_COMMIT:0:12}-${GODOT_DOTNET_PATCH_SHA256:0:16}"
SDK_ROOT="$WORK/sdk/$SDK_KEY"
if [[ -e "$SDK_ROOT" ]]; then
  if [[ ! -f "$SDK_ROOT/.source-identity" || "$(cat "$SDK_ROOT/.source-identity")" != "$SDK_ID" ]]; then
    echo "SDK cache key collision or missing full source identity: $SDK_ROOT (preserved unchanged)." >&2
    exit 1
  fi
else
  mkdir -p "$SDK_ROOT"
  printf '%s\n' "$SDK_ID" > "$SDK_ROOT/.source-identity"
fi
SDK_SOURCE="$SDK_ROOT/godot-dotnet"
FEED="$SDK_ROOT/feed"
FEED_NATIVE="$(native_path "$FEED")"
PROJECT_NATIVE="$(native_path "$HERE/NecronomiDom.Native.csproj")"
# This is distinct from upstream 4.7.0-dev; the full source identity remains guarded above and in the lock.
BINDINGS_VERSION="4.7.0-dev.ndom.g${GODOT_DOTNET_COMMIT:0:12}.p${GODOT_DOTNET_PATCH_SHA256:0:16}"
export NUGET_PACKAGES="$(native_path "$SDK_ROOT/packages")"

if [[ $# -eq 0 ]]; then
  case "$HOST_OS-$(uname -m)" in
    Linux-x86_64) set -- linux-x64 ;; Linux-aarch64) set -- linux-arm64 ;;
    Darwin-arm64) set -- osx-arm64 ;; Darwin-x86_64) set -- osx-x64 ;;
    MINGW*|MSYS*|CYGWIN*) set -- win-x64 ;;
    *) echo "Pass a runtime identifier." >&2; exit 2 ;;
  esac
fi

if [[ ! -d "$SDK_SOURCE" ]]; then
  git clone --quiet --config core.longpaths=true "$GODOT_DOTNET_REPOSITORY" "$SDK_SOURCE"
  git -C "$SDK_SOURCE" checkout --quiet "$GODOT_DOTNET_COMMIT"
  git -C "$SDK_SOURCE" apply --whitespace=error "$PATCH"
fi

# Refuse altered/partially prepared source instead of deleting it or relabeling it as this pin.
if [[ "$(git -C "$SDK_SOURCE" rev-parse HEAD)" != "$GODOT_DOTNET_COMMIT" \
   || "$(git -C "$SDK_SOURCE" diff --name-only HEAD)" != "$GODOT_DOTNET_PATCHED_FILE" \
   || -n "$(git -C "$SDK_SOURCE" ls-files --others --exclude-standard)" \
   || "$(sha256_file "$SDK_SOURCE/$GODOT_DOTNET_PATCHED_FILE")" != "$GODOT_DOTNET_PATCHED_FILE_SHA256" ]]; then
  echo "SDK cache source differs from its pinned correction: $SDK_SOURCE" >&2
  echo "Preserved it unchanged. Use another NECRONOMIDOM_WORK directory or inspect the retained source." >&2
  exit 1
fi

if [[ ! -f "$FEED/.identity" || "$(cat "$FEED/.identity")" != "$SDK_ID" ]]; then
  # Bindings generation is opt-in upstream; audit and generated-doc warnings are not ours to fix.
  # Upstream's cross-platform entry point avoids its shell wrapper's lossy argument re-splitting.
  (cd "$SDK_SOURCE" && python "$(native_path "$SDK_SOURCE/eng/common/build.py")" \
    --restore --build --productBuild --pushNupkgsLocal "$FEED_NATIVE" \
    /p:GenerateGodotBindings=true /p:NuGetAudit=false /p:PackageVersion="$BINDINGS_VERSION" --warnAsError false)
  cp "$HERE/godot-dotnet.lock" "$SDK_ROOT/godot-dotnet.lock"
  cp "$PATCH" "$SDK_ROOT/$(basename "$PATCH")"
  printf '%s\n' "$SDK_ID" > "$FEED/.identity"
fi

for rid in "$@"; do
  case "$rid" in
    linux-*) lib="libnecronomidom.so" ;; osx-*) lib="libnecronomidom.dylib" ;; win-*) lib="necronomidom.dll" ;;
    *) echo "Unsupported RID $rid" >&2; exit 2 ;;
  esac
  dotnet publish "$PROJECT_NATIVE" -c Release -r "$rid" -nologo -v q \
    /p:GodotBindingsVersion="$BINDINGS_VERSION" \
    --source "$FEED_NATIVE" --source https://api.nuget.org/v3/index.json
  built="$(find "$HERE/bin/Release/net10.0/$rid/publish" -maxdepth 1 -name 'NecronomiDom.Native.*' \( -name '*.so' -o -name '*.dylib' -o -name '*.dll' \) | head -1)"
  mkdir -p "$ADDON/bin/$rid"
  cp "$built" "$ADDON/bin/$rid/$lib"
  echo "built $ADDON/bin/$rid/$lib"
done

cp "$HERE/necronomidom.gdextension" "$ADDON/necronomidom.gdextension"
