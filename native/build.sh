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
HERE="$(cd "$(dirname "$0")" && pwd)"
ADDON="$HERE/../addons/necronomidom"
WORK="$HERE/.work"
# GDExtension .NET bindings (github.com/godotengine/godot-dotnet), pinned. They target the Godot 4.7 extension API.
GODOT_DOTNET_COMMIT=965a09db8b42f79d7987270063eb19abb985ceb6

if [[ $# -eq 0 ]]; then
  case "$(uname -s)-$(uname -m)" in
    Linux-x86_64) set -- linux-x64 ;; Linux-aarch64) set -- linux-arm64 ;;
    Darwin-arm64) set -- osx-arm64 ;; Darwin-x86_64) set -- osx-x64 ;;
    MINGW*|MSYS*|CYGWIN*) set -- win-x64 ;;
    *) echo "Pass a runtime identifier." >&2; exit 2 ;;
  esac
fi

if [[ ! -f "$WORK/feed/.commit" || "$(cat "$WORK/feed/.commit")" != "$GODOT_DOTNET_COMMIT" ]]; then
  rm -rf "$WORK/godot-dotnet" "$WORK/feed"
  git clone --quiet https://github.com/godotengine/godot-dotnet.git "$WORK/godot-dotnet"
  git -C "$WORK/godot-dotnet" checkout --quiet "$GODOT_DOTNET_COMMIT"
  # Bindings generation is opt-in upstream; audit and generated-doc warnings are not ours to fix.
  (cd "$WORK/godot-dotnet" && ./build.sh --productBuild --pushNupkgsLocal "$WORK/feed" \
    /p:GenerateGodotBindings=true /p:NuGetAudit=false --warnAsError false)
  echo "$GODOT_DOTNET_COMMIT" > "$WORK/feed/.commit"
  rm -rf "$HOME/.nuget/packages/godot.bindings/4.7.0-dev" "$HOME/.nuget/packages/godot.sourcegeneration/4.7.0-dev"
fi

for rid in "$@"; do
  case "$rid" in
    linux-*) lib="libnecronomidom.so" ;; osx-*) lib="libnecronomidom.dylib" ;; win-*) lib="necronomidom.dll" ;;
    *) echo "Unsupported RID $rid" >&2; exit 2 ;;
  esac
  dotnet publish "$HERE/NecronomiDom.Native.csproj" -c Release -r "$rid" -nologo -v q
  built="$(find "$HERE/bin/Release/net10.0/$rid/publish" -maxdepth 1 -name 'NecronomiDom.Native.*' \( -name '*.so' -o -name '*.dylib' -o -name '*.dll' \) | head -1)"
  mkdir -p "$ADDON/bin/$rid"
  cp "$built" "$ADDON/bin/$rid/$lib"
  echo "built $ADDON/bin/$rid/$lib"
done

cp "$HERE/necronomidom.gdextension" "$ADDON/necronomidom.gdextension"
