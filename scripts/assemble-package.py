#!/usr/bin/env python3
"""Assemble the exact five-target ZIP; runtime qualification happens afterward."""

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


def assemble(root, name, *, partial=False):
    addon = root / "addons/necronomidom"
    descriptor = addon / "necronomidom.gdextension"
    shutil.copyfile(root / "native/necronomidom.gdextension", descriptor)
    included = [rid for rid, library in LIBRARIES.items()
                if (addon / "bin" / rid / library).is_file()
                and (addon / "bin" / rid / library).stat().st_size > 0]
    omitted = [rid for rid in LIBRARIES if rid not in included]
    if omitted and not partial:
        raise ValueError("Missing native targets: " + ", ".join(omitted))
    if not included:
        raise ValueError("Cannot package an addon without a native library")
    if partial:
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
    scope = {
        "schema": "necronomidom.package-scope/2",
        "included_targets": included,
        "omitted_targets": omitted,
        "diagnostic": partial,
        "target_set_complete": not omitted,
        "qualification": "Scope only; runtime jobs must qualify this exact ZIP before release.",
    }
    (addon / "ci-package-scope.json").write_text(json.dumps(scope, indent=2) + "\n", encoding="utf-8")
    filename = name + ("-partial" if partial else "") + ".zip"
    (root / "dist").mkdir(exist_ok=True)
    shutil.make_archive(str(root / "dist" / filename.removesuffix(".zip")), "zip", root,
                        "addons/necronomidom")
    return filename, scope


def main():
    root = Path(__file__).resolve().parents[1]
    name = "NecronomiDOM-" + os.environ["GITHUB_REF_NAME"].replace("/", "-")
    filename, scope = assemble(root, name, partial=os.environ.get("PARTIAL") == "true")
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(f"name={filename}\n")
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
        summary.write(f"ZIP: `{filename}`\n\nIncluded targets: {', '.join(scope['included_targets'])}.\n\n")
        summary.write(f"Omitted targets: {', '.join(scope['omitted_targets']) or 'none'}.\n\n")
        if scope["diagnostic"]:
            summary.write("Manual partial diagnostic; never a release.\n")
    print(json.dumps(scope, indent=2))


if __name__ == "__main__":
    main()
