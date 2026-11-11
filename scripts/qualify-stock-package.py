#!/usr/bin/env python3
"""Qualify the built addon ZIP in a fresh project using official standard Godot."""

import argparse
import configparser
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tempfile
import zipfile


def run(command, timeout=180):
    print("+ " + " ".join(map(str, command)), flush=True)
    try:
        result = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                text=True, timeout=timeout)
    except subprocess.TimeoutExpired as error:
        output = error.stdout
        if isinstance(output, bytes):
            output = output.decode("utf-8", errors="replace")
        if output:
            print(output, end="", flush=True)
        raise
    print(result.stdout, end="", flush=True)
    if result.returncode:
        raise RuntimeError(f"Godot exited with status {result.returncode}")
    return result.stdout


def godot_command(godot, architecture=None):
    if architecture is None:
        return [str(godot)]
    if platform.system() != "Darwin" or architecture not in {"arm64", "x86_64"}:
        raise ValueError("Architecture binding requires macOS and arm64 or x86_64")
    # Apply to every process, including version/import/editor/lifecycle, not just
    # the shell or Python. A universal binary otherwise defaults to ARM on Apple silicon.
    return ["/usr/bin/arch", "-" + architecture, str(godot)]


def qualify(godot, package, version, architecture=None):
    executable = godot_command(godot, architecture)
    engine_version = run(executable + ["--version"]).strip()
    if not engine_version.startswith(version + ".stable.official."):
        raise RuntimeError(f"Expected official standard Godot {version}, got {engine_version!r}")

    repository = Path(__file__).resolve().parents[1]
    with tempfile.TemporaryDirectory(prefix="necronomidom-stock-package-") as temporary:
        project = Path(temporary)
        with zipfile.ZipFile(package) as archive:
            archive.extractall(project)
        descriptor = project / "addons/necronomidom/necronomidom.gdextension"
        config = configparser.ConfigParser(interpolation=None)
        config.optionxform = str
        config.read(descriptor, encoding="utf-8")
        if not config.has_section("libraries") or not config["libraries"]:
            raise RuntimeError("Installed package has no native library entries")
        for platform, library in config["libraries"].items():
            resource = library.strip('"')
            if not resource.startswith("res://"):
                raise RuntimeError(f"Invalid library resource for {platform}: {resource}")
            path = project / resource.removeprefix("res://")
            if not path.is_file() or path.stat().st_size == 0:
                raise RuntimeError(f"Package advertises a missing library for {platform}: {resource}")

        # Reuse the real demo action and DOM-output oracle, not a synthetic exit-only scene.
        # No source addon, project import cache, C# project or build output is copied.
        (project / "demo/gdscript").mkdir(parents=True)
        (project / "native/tests").mkdir(parents=True)
        for relative in ("demo/gdscript/demo.gd", "demo/gdscript/demo.tscn",
                         "demo/grimoire.html", "demo/grimoire.css",
                         "native/tests/lifecycle.gd", "native/tests/lifecycle.html"):
            shutil.copyfile(repository / relative, project / relative)
        (project / "project.godot").write_text(
            'config_version=5\n\n[application]\n'
            'config/name="NecronomiDOM installed-package qualification"\n'
            'run/main_scene="res://demo/gdscript/demo.tscn"\n\n'
            '[display]\nwindow/size/viewport_width=1440\n'
            'window/size/viewport_height=1000\n\n'
            '[gui]\ncommon/snap_controls_to_pixels=false\n', encoding="utf-8")
        if (project / ".godot").exists():
            raise RuntimeError("Installed package must not contain a warmed project cache")
        command = executable + ["--headless", "--audio-driver", "Dummy", "--path", str(project)]
        # Editor import includes a clean process shutdown. A crash here must block packaging.
        run(command + ["--import"])
        if architecture is not None:
            (project / "architecture.gd").write_text(
                'extends SceneTree\nfunc _initialize():\n'
                f'    if not OS.has_feature("{architecture}"):\n'
                '        push_error("Unexpected Godot process architecture")\n'
                '        quit(1)\n        return\n'
                f'    print("STOCK_PACKAGE_ARCH {architecture}")\n'
                '    quit(0)\n', encoding="utf-8")
            proof = run(command + ["--script", "res://architecture.gd"])
            if f"STOCK_PACKAGE_ARCH {architecture}" not in proof.splitlines():
                raise RuntimeError("Stock Godot did not prove the requested process architecture")
        run(command + ["--editor", "res://demo/gdscript/demo.tscn", "--quit"])
        output = run(command + ["res://demo/gdscript/demo.tscn", "--", "--smoke"])
        if 'count="One shape stirs beneath the page."' not in output:
            raise RuntimeError("Native Summon action did not produce the expected DOM output")
        lifecycle = run(command + ["--script", "res://native/tests/lifecycle.gd"])
        marker = "native-lifecycle: cycles=4 reloads=4 withdrawn=3 final-host-mounted=true"
        if marker not in lifecycle.splitlines():
            raise RuntimeError("Native lifecycle did not complete mount/reload/withdrawal coverage")
    print("STOCK_PACKAGE_RESULT PASS")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", required=True, type=Path, help="official standard Godot binary")
    parser.add_argument("--package", required=True, type=Path, help="assembled addon ZIP")
    parser.add_argument("--godot-version", default="4.7.2")
    parser.add_argument("--architecture", choices=("arm64", "x86_64"),
                        help="bind every macOS Godot invocation, including Rosetta x86_64")
    args = parser.parse_args()
    try:
        qualify(args.godot.resolve(), args.package.resolve(), args.godot_version, args.architecture)
    except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired, zipfile.BadZipFile) as error:
        print("STOCK_PACKAGE_RESULT FAIL: " + str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
