"""Every full package carries all five native targets; only manual diagnostics omit any."""

import configparser
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("assemble_package", ROOT / "scripts/assemble-package.py")
package = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(package)


class PackageScopeTests(unittest.TestCase):
    def fixture(self, targets):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / "native").mkdir()
        shutil.copyfile(ROOT / "native/necronomidom.gdextension", root / "native/necronomidom.gdextension")
        for target in targets:
            library = root / "addons/necronomidom/bin" / target / package.LIBRARIES[target]
            library.parent.mkdir(parents=True, exist_ok=True)
            library.write_bytes(b"test fixture")
        return root

    def test_full_package_contains_all_five_targets_and_descriptor_entries(self):
        root = self.fixture(package.LIBRARIES)
        name, scope = package.assemble(root, "NecronomiDOM-test")
        self.assertEqual(name, "NecronomiDOM-test.zip")
        self.assertEqual(scope["schema"], "necronomidom.package-scope/2")
        self.assertEqual(scope["included_targets"], list(package.LIBRARIES))
        self.assertEqual(scope["omitted_targets"], [])
        self.assertFalse(scope["diagnostic"])
        self.assertTrue(scope["target_set_complete"])
        self.assertNotIn("windows_deferred", scope)
        with zipfile.ZipFile(root / "dist" / name) as archive:
            for target, library in package.LIBRARIES.items():
                self.assertIn(f"addons/necronomidom/bin/{target}/{library}", archive.namelist())
            descriptor = configparser.ConfigParser(interpolation=None)
            descriptor.read_string(archive.read("addons/necronomidom/necronomidom.gdextension").decode())
            self.assertIn("windows.x86_64", descriptor["libraries"])
            self.assertEqual(len(descriptor["libraries"]), 5)
            self.assertEqual(json.loads(archive.read("addons/necronomidom/ci-package-scope.json")), scope)

    def test_every_target_including_windows_remains_required(self):
        for missing in package.LIBRARIES:
            with self.subTest(missing=missing):
                root = self.fixture([target for target in package.LIBRARIES if target != missing])
                with self.assertRaisesRegex(ValueError, f"Missing native targets: {missing}$"):
                    package.assemble(root, "NecronomiDOM-test")

    def test_empty_windows_library_is_missing(self):
        root = self.fixture(package.LIBRARIES)
        (root / "addons/necronomidom/bin/win-x64/necronomidom.dll").write_bytes(b"")
        with self.assertRaisesRegex(ValueError, "win-x64"):
            package.assemble(root, "NecronomiDOM-test")

    def test_manual_diagnostic_is_explicitly_partial(self):
        root = self.fixture(["linux-x64"])
        name, scope = package.assemble(root, "NecronomiDOM-test", partial=True)
        self.assertEqual(name, "NecronomiDOM-test-partial.zip")
        self.assertEqual(scope["included_targets"], ["linux-x64"])
        self.assertEqual(scope["omitted_targets"], [target for target in package.LIBRARIES if target != "linux-x64"])
        self.assertTrue(scope["diagnostic"])
        self.assertFalse(scope["target_set_complete"])
        with zipfile.ZipFile(root / "dist" / name) as archive:
            descriptor = configparser.ConfigParser(interpolation=None)
            descriptor.read_string(archive.read("addons/necronomidom/necronomidom.gdextension").decode())
            self.assertEqual(list(descriptor["libraries"]), ["linux.x86_64"])


if __name__ == "__main__":
    unittest.main()
