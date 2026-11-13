#!/usr/bin/env python3
"""Project an assembled full/diagnostic ZIP without changing its target scope.

Usage: project-runtime-package.py --package INPUT.zip --output OUTPUT.zip
The output must not exist. Retained members keep their exact uncompressed bytes;
runtime-package.json records input provenance, unchanged scope and native hashes.
This projection is not runtime qualification and never upgrades source coverage.
"""

import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import stat
import tempfile
import zipfile


PREFIX = "addons/necronomidom/"
SCOPE = PREFIX + "ci-package-scope.json"
DESCRIPTOR = PREFIX + "necronomidom.gdextension"
MANIFEST = PREFIX + "runtime-package.json"
OMITTED_SUFFIXES = (
    ".cs", ".csproj", ".fsproj", ".targets", ".props", ".nupkg", ".snupkg",
    ".exe", ".binlog", ".pdb", ".sln", ".slnx", ".cs.uid",
)
OMITTED_COMPONENTS = {"obj", ".godot", ".git"}
NATIVE_SUFFIXES = (".so", ".dll", ".dylib")


def validate_members(archive):
    """Validate even omitted members before writing anything to the output."""
    members = archive.infolist()
    names = {}
    for member in members:
        name = member.orig_filename
        parts = name.removesuffix("/").split("/")
        if (name != member.filename or "\\" in name or ":" in name
                or any(ord(character) < 32 or ord(character) == 127 for character in name)
                or any(part in {"", ".", ".."} or part.endswith((" ", ".")) for part in parts)
                or not name.startswith(PREFIX)
                or (name == PREFIX and not member.is_dir())):
            raise ValueError(f"Unsafe package member: {name!r}")
        # These aliases are unsafe on Windows, even when authored on Unix.
        for part in parts:
            stem = part.split(".", 1)[0].upper()
            if (any(character in part for character in '<>"|?*')
                    or stem in {"CON", "PRN", "AUX", "NUL"}
                    or stem in {f"{prefix}{digit}" for prefix in ("COM", "LPT") for digit in "123456789"}):
                raise ValueError(f"Unsafe package member: {name!r}")
        kind = stat.S_IFMT(member.external_attr >> 16)
        if kind not in {0, stat.S_IFREG, stat.S_IFDIR}:
            raise ValueError(f"Unsupported package member type (symlink or special file): {name!r}")
        if kind and (kind == stat.S_IFDIR) != member.is_dir():
            raise ValueError(f"Inconsistent package member type: {name!r}")
        key = name.rstrip("/").casefold()
        if key in names:
            raise ValueError(f"Duplicate package member: {name!r}")
        names[key] = member
    for name, member in names.items():
        parts = name.split("/")
        for index in range(1, len(parts)):
            ancestor = names.get("/".join(parts[:index]))
            if ancestor is not None and not ancestor.is_dir():
                raise ValueError(f"File used as a package directory: {member.filename!r}")
    if MANIFEST.casefold() in names:
        raise ValueError("Input already contains runtime-package.json; use the original assembled ZIP")
    for required in (SCOPE, DESCRIPTOR):
        if required not in archive.namelist() or archive.getinfo(required).is_dir():
            raise ValueError(f"Missing assembled package member: {required}")
    return members


def omitted(name):
    lowered = name.rstrip("/").casefold()
    return (bool(OMITTED_COMPONENTS.intersection(lowered.split("/")))
            or lowered.endswith(OMITTED_SUFFIXES))


def project(package, output):
    """Write a new runtime ZIP, returning its provenance manifest; never clobber."""
    package, output = Path(package), Path(output)
    if output.exists() or output.is_symlink() or package.resolve() == output.resolve():
        raise ValueError(f"Output already exists or aliases the input: {output}")
    with package.open("rb") as source:
        source_digest = hashlib.file_digest(source, "sha256").hexdigest()
        source.seek(0)
        with zipfile.ZipFile(source) as archive:
            members = validate_members(archive)
            scope = json.loads(archive.read(SCOPE))
            if not isinstance(scope, dict) or scope.get("schema") != "necronomidom.package-scope/2":
                raise ValueError("Input has no supported assembled package scope")
            manifest = {
                "schema": "necronomidom.runtime-package/1",
                "source_package": {"filename": package.name, "sha256": source_digest},
                "source_scope": scope,
                "native_binaries": {},
            }
            output.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.TemporaryDirectory(prefix=".necronomidom-runtime-", dir=output.parent) as temporary:
                staged = Path(temporary) / "runtime.zip"
                with zipfile.ZipFile(staged, "w", compression=zipfile.ZIP_DEFLATED) as projected:
                    for member in members:
                        if omitted(member.filename):
                            continue
                        digest = None
                        if (not member.is_dir() and member.filename.startswith(PREFIX + "bin/")
                                and member.filename.casefold().endswith(NATIVE_SUFFIXES)):
                            digest = hashlib.sha256()
                        with archive.open(member) as original, projected.open(copy.copy(member), "w") as retained:
                            while chunk := original.read(1024 * 1024):
                                retained.write(chunk)
                                if digest is not None:
                                    digest.update(chunk)
                        if digest is not None:
                            manifest["native_binaries"][member.filename] = digest.hexdigest()
                    if not manifest["native_binaries"]:
                        raise ValueError("Input has no retained native binaries")
                    info = zipfile.ZipInfo(MANIFEST, date_time=(1980, 1, 1, 0, 0, 0))
                    info.compress_type = zipfile.ZIP_DEFLATED
                    info.create_system = 3
                    info.external_attr = 0o100644 << 16
                    projected.writestr(info, json.dumps(manifest, indent=2, sort_keys=True) + "\n")
                # Same-filesystem, atomic, exclusive publication: a racing writer,
                # symlink or hardlink can never make us replace an existing file.
                os.link(staged, output)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    try:
        manifest = project(arguments.package, arguments.output)
    except (OSError, ValueError, RuntimeError, zipfile.BadZipFile) as error:
        parser.exit(1, f"Runtime package projection failed: {error}\n")
    print(json.dumps(manifest, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
