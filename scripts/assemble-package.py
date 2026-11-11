#!/usr/bin/env python3
"""Assemble the exact target-scoped ZIP; runtime qualification happens afterward."""

import configparser
import json
import os
from pathlib import Path
import shutil


LIBRARIES = {
    "linux-x64": "libnecronomidom.so",
    "linux-arm64": "libnecronomidom.so",
    "win-x64": "necronomidom.dll",
    "osx-arm64": "libnecronomidom.dylib",
    "osx-x64": "libnecronomidom.dylib",
}


def assemble(root, name, *, partial=False, windows=False):
    addon = root / "addons/necronomidom"
    descriptor = addon / "necronomidom.gdextension"
    shutil.copyfile(root / "native/necronomidom.gdextension", descriptor)
    # Windows deferral is intentional, never permission to ship a stale DLL.
    windows_directory = addon / "bin/win-x64"
    if not windows and windows_directory.exists():
        shutil.rmtree(windows_directory)
    included = [rid for rid, library in LIBRARIES.items()
                if (addon / "bin" / rid / library).is_file()
                and (addon / "bin" / rid / library).stat().st_size > 0]
    required = [rid for rid in LIBRARIES if windows or rid != "win-x64"]
    missing = [rid for rid in required if rid not in included]
    if missing and not partial:
        raise ValueError("Missing enabled native targets: " + ", ".join(missing))
    if not included:
        raise ValueError("Cannot package an addon without a native library")
    if partial or not windows:
        config = configparser.ConfigParser(interpolation=None)
        config.optionxform = str
        config.read(descriptor, encoding="utf-8")
        for platform, library in list(config["libraries"].items()):
            path = root / library.strip('"').removeprefix("res://")
            if not path.is_file() or path.stat().st_size == 0:
                config.remove_option("libraries", platform)
        if not config["libraries"]:
            raise ValueError("Installed descriptor has no native library entries")
        with descriptor.open("w", encoding="utf-8") as output:
            config.write(output)
    omitted = [rid for rid in LIBRARIES if rid not in included]
    scope = {
        "schema": "necronomidom.package-scope/1",
        "included_targets": included,
        "omitted_targets": omitted,
        "windows_deferred": not windows,
        "diagnostic": partial,
        "target_set_complete": not omitted,
        "qualification": "Scope only; runtime jobs must qualify this exact ZIP before release.",
    }
    (addon / "ci-package-scope.json").write_text(json.dumps(scope, indent=2) + "\n", encoding="utf-8")
    suffix = ("-no-windows" if not windows else "") + ("-partial" if partial else "")
    filename = name + suffix + ".zip"
    (root / "dist").mkdir(exist_ok=True)
    shutil.make_archive(str(root / "dist" / filename.removesuffix(".zip")), "zip", root,
                        "addons/necronomidom")
    return filename, scope


def main():
    root = Path(__file__).resolve().parents[1]
    name = "NecronomiDOM-" + os.environ["GITHUB_REF_NAME"].replace("/", "-")
    filename, scope = assemble(root, name, partial=os.environ.get("PARTIAL") == "true",
                               windows=os.environ.get("WINDOWS_ENABLED") == "true")
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(f"name={filename}\n")
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
        summary.write(f"ZIP: `{filename}`\n\nIncluded targets: {', '.join(scope['included_targets'])}.\n\n")
        summary.write(f"Omitted targets: {', '.join(scope['omitted_targets']) or 'none'}.\n\n")
        if scope["windows_deferred"]:
            summary.write("Windows is deferred and unqualified. Five-target release publication is disabled.\n")
    print(json.dumps(scope, indent=2))


if __name__ == "__main__":
    main()
