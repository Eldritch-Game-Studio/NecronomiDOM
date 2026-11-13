"""Exercise Windows deferral without relaxing any enabled-target package gate."""

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

    def test_deferred_windows_cannot_leak_stale_binary_or_descriptor(self):
        root = self.fixture(package.LIBRARIES)
        name, scope = package.assemble(root, "NecronomiDOM-test")
        self.assertEqual(name, "NecronomiDOM-test-no-windows.zip")
        self.assertEqual(scope["omitted_targets"], ["win-x64"])
        self.assertTrue(scope["windows_deferred"])
        self.assertFalse(scope["target_set_complete"])
        with zipfile.ZipFile(root / "dist" / name) as archive:
            self.assertFalse(any("/win-x64/" in path for path in archive.namelist()))
            descriptor = configparser.ConfigParser(interpolation=None)
            descriptor.read_string(archive.read("addons/necronomidom/necronomidom.gdextension").decode())
            self.assertNotIn("windows.x86_64", descriptor["libraries"])
            self.assertEqual(len(descriptor["libraries"]), 4)
            self.assertEqual(json.loads(archive.read("addons/necronomidom/ci-package-scope.json")), scope)

    def test_every_enabled_target_remains_required(self):
        enabled = [target for target in package.LIBRARIES if target != "win-x64"]
        for missing in enabled:
            with self.subTest(missing=missing):
                root = self.fixture([target for target in enabled if target != missing])
                with self.assertRaisesRegex(ValueError, "Missing enabled native targets"):
                    package.assemble(root, "NecronomiDOM-test")

    def test_future_windows_enablement_requires_real_windows_library(self):
        root = self.fixture([target for target in package.LIBRARIES if target != "win-x64"])
        with self.assertRaisesRegex(ValueError, "win-x64"):
            package.assemble(root, "NecronomiDOM-test", windows=True)
        complete = self.fixture(package.LIBRARIES)
        name, scope = package.assemble(complete, "NecronomiDOM-test", windows=True)
        self.assertEqual(name, "NecronomiDOM-test.zip")
        self.assertTrue(scope["target_set_complete"])
        self.assertFalse(scope["windows_deferred"])
        self.assertEqual(scope["omitted_targets"], [])

    def test_manual_diagnostic_is_explicitly_partial(self):
        root = self.fixture(["linux-x64"])
        name, scope = package.assemble(root, "NecronomiDOM-test", partial=True)
        self.assertEqual(name, "NecronomiDOM-test-no-windows-partial.zip")
        self.assertEqual(scope["included_targets"], ["linux-x64"])
        self.assertTrue(scope["diagnostic"])
        self.assertFalse(scope["target_set_complete"])

    def test_bootstrap_manual_diagnostic_with_windows_enabled(self):
        root = self.fixture(["linux-x64"])
        name, scope = package.assemble(root, "NecronomiDOM-test", partial=True, windows=True)
        self.assertEqual(name, "NecronomiDOM-test-partial.zip")
        self.assertEqual(scope["included_targets"], ["linux-x64"])
        self.assertTrue(scope["diagnostic"])
        self.assertFalse(scope["windows_deferred"])
        self.assertFalse(scope["target_set_complete"])


if __name__ == "__main__":
    unittest.main()
