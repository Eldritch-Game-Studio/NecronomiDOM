"""Runtime projection preserves the assembled package, not a reconstructed scope."""

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import warnings
import zipfile


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("runtime_package", ROOT / "scripts/project-runtime-package.py")
runtime = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runtime)
ASSEMBLER_SPEC = importlib.util.spec_from_file_location("assemble_package", ROOT / "scripts/assemble-package.py")
assembler = importlib.util.module_from_spec(ASSEMBLER_SPEC)
ASSEMBLER_SPEC.loader.exec_module(assembler)
PREFIX = "addons/necronomidom/"


class RuntimePackageTests(unittest.TestCase):
    def fixture(self, targets=None, *, partial=False):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / "native").mkdir()
        shutil.copyfile(ROOT / "native/necronomidom.gdextension", root / "native/necronomidom.gdextension")
        for target in assembler.LIBRARIES if targets is None else targets:
            library = root / PREFIX / "bin" / target / assembler.LIBRARIES[target]
            library.parent.mkdir(parents=True, exist_ok=True)
            library.write_bytes(b"native fixture\x00\xff\r\n" + target.encode())
        self.retained = {
            "runtime/bridge.gd": b"extends Node\r\n# original line endings\r\n",
            "runtime/bridge.gd.uid": b"uid://original\n",
            "necronomidom.gdextension.uid": b"uid://extension\n",
            "shaders/fill.gdshader": b"shader_type canvas_item;\n",
            "fonts/font.otf": b"\x00\xfforiginal-font\x80",
            "fonts/font.otf.import": b"original import configuration\n",
            "fonts/OFL.txt": b"Original font license\r\n",
            "LICENSE": b"Original license\n",
            "THIRD_PARTY_NOTICES.txt": b"Original third-party notices\n",
            "vendor/NOTICE": b"Original vendor notice\n",
            "vendor/.gdignore": b"",
            "assets/object.txt": b"not an obj directory\n",
        }
        self.managed = [
            "managed/Library.cs", "managed/Library.cs.uid", "managed/Library.csproj",
            "managed/Library.fsproj", "managed/Library.targets", "managed/Library.props",
            "managed/Library.nupkg", "managed/Library.snupkg", "managed/Tool.exe",
            "managed/Build.binlog", "managed/Library.pdb", "managed/Library.sln",
            "managed/Library.slnx", "managed/UPPER.CS", "managed/UPPER.CS.UID",
            "managed/UPPER.CSPROJ", "managed/UPPER.EXE", "managed/UPPER.PDB",
            "core/obj/project.assets.json", "obj/asset.dat", ".godot/imported/cache.dat",
            ".git/config", "nested/.godot/cache.dat", "nested/.git/index",
            "uppercase/OBJ/cache.dat", "uppercase/.GODOT/cache.dat", "uppercase/.GIT/config",
        ]
        for relative, data in self.retained.items():
            path = root / PREFIX / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        for relative in self.managed:
            path = root / PREFIX / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"managed build/source/cache fixture\n")
        filename, scope = assembler.assemble(root, "NecronomiDOM-original", partial=partial)
        return root / "dist" / filename, root / "dist/runtime.zip", scope

    def assert_projection(self, package, output, scope):
        original_bytes = package.read_bytes()
        manifest = runtime.project(package, output)
        self.assertEqual(package.read_bytes(), original_bytes)
        self.assertEqual(manifest["schema"], "necronomidom.runtime-package/1")
        self.assertEqual(manifest["source_package"], {
            "filename": package.name,
            "sha256": hashlib.sha256(original_bytes).hexdigest(),
        })
        self.assertEqual(manifest["source_scope"], scope)
        with zipfile.ZipFile(package) as original, zipfile.ZipFile(output) as projected:
            self.assertEqual(json.loads(projected.read(PREFIX + "runtime-package.json")), manifest)
            # The scope JSON and descriptor are opaque retained bytes, including
            # whitespace; deriving target claims from available libraries is forbidden.
            for name in ("ci-package-scope.json", "necronomidom.gdextension"):
                self.assertEqual(projected.read(PREFIX + name), original.read(PREFIX + name))
            for relative, data in self.retained.items():
                self.assertEqual(projected.read(PREFIX + relative), data)
            for relative in self.managed:
                self.assertNotIn(PREFIX + relative, projected.namelist())
            expected = set(original.namelist())
            expected.difference_update(PREFIX + relative for relative in self.managed)
            # Filter excluded cache directories as well as their files. Managed
            # source parent directories remain, preserving other archive entries.
            expected = {name for name in expected if not any(
                part.casefold() in {"obj", ".godot", ".git"} for part in name.split("/"))}
            self.assertEqual(set(projected.namelist()), expected | {PREFIX + "runtime-package.json"})
            for name in expected:
                self.assertEqual(projected.read(name), original.read(name), name)
            expected_native = {
                PREFIX + "bin/" + target + "/" + assembler.LIBRARIES[target]:
                    hashlib.sha256(original.read(PREFIX + "bin/" + target + "/" + assembler.LIBRARIES[target])).hexdigest()
                for target in scope["included_targets"]
            }
            self.assertEqual(manifest["native_binaries"], expected_native)
        return manifest

    def test_five_target_package_keeps_complete_target_set_and_exact_bytes(self):
        package, output, scope = self.fixture()
        manifest = self.assert_projection(package, output, scope)
        self.assertEqual(len(manifest["native_binaries"]), 5)
        self.assertTrue(manifest["source_scope"]["target_set_complete"])
        self.assertFalse(manifest["source_scope"]["diagnostic"])
        self.assertEqual(manifest["source_scope"]["omitted_targets"], [])

    def test_diagnostic_scope_is_never_promoted(self):
        package, output, scope = self.fixture(["linux-x64"], partial=True)
        manifest = self.assert_projection(package, output, scope)
        self.assertEqual(manifest["source_scope"]["included_targets"], ["linux-x64"])
        self.assertTrue(manifest["source_scope"]["diagnostic"])
        self.assertFalse(manifest["source_scope"]["target_set_complete"])

    def test_complete_but_diagnostic_scope_stays_diagnostic(self):
        package, output, scope = self.fixture(partial=True)
        manifest = self.assert_projection(package, output, scope)
        self.assertTrue(manifest["source_scope"]["diagnostic"])
        self.assertTrue(manifest["source_scope"]["target_set_complete"])

    def test_unsafe_members_are_rejected_before_filtering(self):
        unsafe = [
            "../escape.cs", "/addons/necronomidom/absolute.cs", "C:/absolute.cs",
            "addons\\necronomidom\\backslash.cs", PREFIX + "../escape.cs",
            PREFIX + "./local.cs", PREFIX + "nested//double.cs", PREFIX + "nested/../../escape.cs",
            "addons/other/Library.cs", "project.godot", "addons/", PREFIX.rstrip("/"),
            PREFIX + "stream:alternate.cs", PREFIX + "trailing.cs ", PREFIX + "trailing.cs.",
            PREFIX + "control\x01.cs", PREFIX + "NUL.cs", PREFIX + "COM1.txt", PREFIX + "wild*.cs",
            PREFIX + "obj/../../escape.cs", PREFIX + ".git/../escape.cs",
        ]
        for name in unsafe:
            with self.subTest(name=name):
                package, output, _ = self.fixture()
                with zipfile.ZipFile(package, "a") as archive:
                    archive.writestr(name, b"unsafe")
                with self.assertRaisesRegex(ValueError, "Unsafe"):
                    runtime.project(package, output)
                self.assertFalse(output.exists())

    def test_nul_truncated_member_is_rejected(self):
        package, output, _ = self.fixture()
        with zipfile.ZipFile(package, "a") as archive:
            archive.writestr(PREFIX + "nul-marker.cs", b"unsafe")
        package.write_bytes(package.read_bytes().replace(b"nul-marker.cs", b"nul\x00marker.cs"))
        with self.assertRaisesRegex(ValueError, "Unsafe"):
            runtime.project(package, output)
        self.assertFalse(output.exists())

    def test_symlinks_and_special_files_are_rejected_even_when_omitted(self):
        for mode in (stat.S_IFLNK, stat.S_IFIFO, stat.S_IFSOCK):
            for name in ("link.gd", "obj/link.cs"):
                with self.subTest(mode=mode, name=name):
                    package, output, _ = self.fixture()
                    info = zipfile.ZipInfo(PREFIX + name)
                    info.create_system = 3
                    info.external_attr = (mode | 0o777) << 16
                    with zipfile.ZipFile(package, "a") as archive:
                        archive.writestr(info, b"../../outside")
                    with self.assertRaisesRegex(ValueError, "Unsupported package member type"):
                        runtime.project(package, output)
                    self.assertFalse(output.exists())

    def test_duplicate_members_and_cross_platform_aliases_are_rejected(self):
        for name in ("runtime/bridge.gd", "RUNTIME/BRIDGE.GD", "managed/Library.cs", "LICENSE/"):
            with self.subTest(name=name):
                package, output, _ = self.fixture()
                with warnings.catch_warnings():
                    warnings.simplefilter("ignore", UserWarning)
                    with zipfile.ZipFile(package, "a") as archive:
                        archive.writestr(PREFIX + name, b"")
                with self.assertRaisesRegex(ValueError, "Duplicate"):
                    runtime.project(package, output)
                self.assertFalse(output.exists())

    def test_file_cannot_also_be_an_implicit_directory(self):
        package, output, _ = self.fixture()
        with zipfile.ZipFile(package, "a") as archive:
            archive.writestr(PREFIX + "LICENSE/child.txt", b"conflict")
        with self.assertRaisesRegex(ValueError, "File used as a package directory"):
            runtime.project(package, output)
        self.assertFalse(output.exists())

    def test_inconsistent_directory_type_is_rejected(self):
        package, output, _ = self.fixture()
        info = zipfile.ZipInfo(PREFIX + "not-a-directory")
        info.create_system = 3
        info.external_attr = (stat.S_IFDIR | 0o755) << 16
        with zipfile.ZipFile(package, "a") as archive:
            archive.writestr(info, b"")
        with self.assertRaisesRegex(ValueError, "Inconsistent"):
            runtime.project(package, output)
        self.assertFalse(output.exists())

    def test_original_input_and_existing_outputs_cannot_be_overwritten(self):
        for alias in ("same", "hardlink", "symlink", "existing", "broken-symlink"):
            with self.subTest(alias=alias):
                package, output, _ = self.fixture()
                original = package.read_bytes()
                if alias == "same":
                    output = package
                elif alias == "hardlink":
                    os.link(package, output)
                elif alias == "symlink":
                    output.symlink_to(package)
                elif alias == "broken-symlink":
                    output.symlink_to(output.parent / "missing.zip")
                else:
                    output.write_bytes(b"existing output")
                with self.assertRaisesRegex(ValueError, "Output already exists"):
                    runtime.project(package, output)
                self.assertEqual(package.read_bytes(), original)
                if alias == "existing":
                    self.assertEqual(output.read_bytes(), b"existing output")
                elif alias == "broken-symlink":
                    self.assertTrue(output.is_symlink())

    def test_racing_output_creation_is_not_overwritten(self):
        package, output, _ = self.fixture()
        link = os.link

        def competing_writer(source, destination):
            Path(destination).write_bytes(b"competing writer")
            link(source, destination)

        with mock.patch.object(runtime.os, "link", side_effect=competing_writer):
            with self.assertRaises(FileExistsError):
                runtime.project(package, output)
        self.assertEqual(output.read_bytes(), b"competing writer")
        self.assertEqual(list(output.parent.glob(".necronomidom-runtime-*")), [])

    def test_corrupt_retained_bytes_leave_no_partial_output(self):
        package, output, _ = self.fixture()
        with zipfile.ZipFile(package, "a") as archive:
            archive.writestr(PREFIX + "corrupt.dat", b"unique-crc-payload", compress_type=zipfile.ZIP_STORED)
        package.write_bytes(package.read_bytes().replace(b"unique-crc-payload", b"broken-crc-payload"))
        with self.assertRaises(zipfile.BadZipFile):
            runtime.project(package, output)
        self.assertFalse(output.exists())
        self.assertEqual(list(output.parent.glob(".necronomidom-runtime-*")), [])

    def test_runtime_input_is_not_silently_reprojected(self):
        package, output, _ = self.fixture()
        runtime.project(package, output)
        second = output.with_name("second-runtime.zip")
        with self.assertRaisesRegex(ValueError, "already contains runtime-package.json"):
            runtime.project(output, second)
        self.assertFalse(second.exists())

    def test_missing_contract_members_or_native_libraries_are_rejected(self):
        for missing in ("ci-package-scope.json", "necronomidom.gdextension", "native"):
            with self.subTest(missing=missing):
                package, output, _ = self.fixture()
                incomplete = package.with_name("incomplete.zip")
                with zipfile.ZipFile(package) as source, zipfile.ZipFile(incomplete, "w") as target:
                    for member in source.infolist():
                        if member.filename == PREFIX + missing:
                            continue
                        if missing == "native" and member.filename.startswith(PREFIX + "bin/"):
                            continue
                        target.writestr(member, source.read(member))
                with self.assertRaisesRegex(ValueError, "Missing assembled|no retained native"):
                    runtime.project(incomplete, output)
                self.assertFalse(output.exists())

    def test_legacy_windows_deferred_scope_is_rejected(self):
        package, output, _ = self.fixture()
        legacy = package.with_name("legacy.zip")
        with zipfile.ZipFile(package) as source, zipfile.ZipFile(legacy, "w") as target:
            for member in source.infolist():
                data = source.read(member)
                if member.filename == PREFIX + "ci-package-scope.json":
                    scope = json.loads(data)
                    data = json.dumps({**scope, "schema": "necronomidom.package-scope/1",
                                       "windows_deferred": True}).encode()
                target.writestr(member, data)
        with self.assertRaisesRegex(ValueError, "no supported assembled package scope"):
            runtime.project(legacy, output)
        self.assertFalse(output.exists())

    def test_cli_needs_no_github_environment_and_creates_output_directory(self):
        package, output, scope = self.fixture()
        output = output.parent / "new/runtime.zip"
        environment = {key: value for key, value in os.environ.items() if not key.startswith("GITHUB_")}
        result = subprocess.run(
            [sys.executable, str(ROOT / "scripts/project-runtime-package.py"),
             "--package", str(package), "--output", str(output)],
            env=environment, capture_output=True, text=True, check=True,
        )
        manifest = json.loads(result.stdout)
        self.assertEqual(manifest["source_scope"], scope)
        with zipfile.ZipFile(output) as archive:
            self.assertEqual(json.loads(archive.read(PREFIX + "runtime-package.json")), manifest)

    def test_cli_reports_failure_without_replacing_input(self):
        package, _, _ = self.fixture()
        original = package.read_bytes()
        result = subprocess.run(
            [sys.executable, str(ROOT / "scripts/project-runtime-package.py"),
             "--package", str(package), "--output", str(package)],
            capture_output=True, text=True,
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Runtime package projection failed", result.stderr)
        self.assertEqual(package.read_bytes(), original)


if __name__ == "__main__":
    unittest.main()
