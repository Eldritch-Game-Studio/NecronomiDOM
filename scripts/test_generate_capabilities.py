"""Behavior boundaries for deterministic generation, truth sharing and read-only CI checks."""

import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("generate-capabilities.py")
SPEC = importlib.util.spec_from_file_location("generate_capabilities", SCRIPT)
generator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(generator)
ROOT = SCRIPT.resolve().parents[1]


class CapabilityGenerationTests(unittest.TestCase):
    def fixture(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        for path in (generator.MANIFEST, generator.SYNTAX_PROFILE):
            target = root / path
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / path, target)
        manifest = json.loads((root / generator.MANIFEST).read_text(encoding="utf-8"))
        for group in manifest["groups"]:
            for source in group["sources"]:
                target = root / generator.CORE / source
                target.parent.mkdir(parents=True, exist_ok=True)
                target.touch()
        return root, manifest

    def save_manifest(self, root, manifest):
        (root / generator.MANIFEST).write_text(json.dumps(manifest), encoding="utf-8")

    def test_output_is_deterministic_and_does_not_promote_parser_only_features(self):
        first = generator.render_outputs(ROOT)
        self.assertEqual(first, generator.render_outputs(ROOT))
        inventory = json.loads(first[generator.OUTPUTS[1]])
        css = json.loads(first[generator.OUTPUTS[2]])
        html = json.loads(first[generator.OUTPUTS[3]])
        admitted = {item["name"] for item in inventory["properties"]}
        completions = {item["name"] for item in css["properties"]}
        self.assertIn("filter", inventory["syntaxOnlyProperties"])
        self.assertNotIn("filter", admitted)
        self.assertNotIn("filter", completions)
        self.assertIn("text-decoration-color", admitted)
        self.assertNotIn("text-decoration-color", completions)
        self.assertNotIn("article", {item["name"] for item in html["tags"]})
        cursor = next(item for item in css["properties"] if item["name"] == "cursor")
        self.assertEqual([item["name"] for item in cursor["values"]], ["auto", "default", "pointer", "text"])
        self.assertIn("no corresponding effect", first[generator.OUTPUTS[0]])

    def test_ci_check_detects_missing_and_stale_bytes_without_writing(self):
        root, _ = self.fixture()
        self.assertEqual(set(generator.generate(root, check=True)), {path.as_posix() for path in generator.OUTPUTS})
        self.assertFalse((root / generator.OUTPUTS[0]).exists())
        self.assertEqual(generator.generate(root), [])
        self.assertEqual(generator.generate(root, check=True), [])
        stale = root / generator.OUTPUTS[0]
        stale.write_bytes(b"stale\r\n")
        self.assertEqual(generator.generate(root, check=True), [generator.OUTPUTS[0].as_posix()])
        self.assertEqual(stale.read_bytes(), b"stale\r\n")
        generator.generate(root)
        self.assertEqual(generator.generate(root, check=True), [])

    def test_manifest_change_updates_reference_inventory_and_completions(self):
        root, manifest = self.fixture()
        before = generator.render_outputs(root)
        manifest["keywords"]["cursor"].append("help")
        self.save_manifest(root, manifest)
        after = generator.render_outputs(root)
        for index in (0, 1, 2):
            self.assertNotEqual(before[generator.OUTPUTS[index]], after[generator.OUTPUTS[index]])
        self.assertEqual(before[generator.OUTPUTS[3]], after[generator.OUTPUTS[3]])
        self.assertIn("`help`", after[generator.OUTPUTS[0]])

    def test_duplicate_or_non_parser_admission_fails_instead_of_generating_claims(self):
        root, manifest = self.fixture()
        manifest["groups"][0]["properties"].append("display")
        self.save_manifest(root, manifest)
        with self.assertRaisesRegex(ValueError, "Duplicate renderer property"):
            generator.render_outputs(root)
        manifest["groups"][0]["properties"][-1] = "invented-renderer-feature"
        self.save_manifest(root, manifest)
        with self.assertRaisesRegex(ValueError, "absent from syntax profile"):
            generator.render_outputs(root)

    def test_missing_constraint_source_fails_instead_of_silently_staling_links(self):
        root, manifest = self.fixture()
        manifest["groups"][0]["sources"].append("Layout/MissingConstraint.cs")
        self.save_manifest(root, manifest)
        with self.assertRaisesRegex(ValueError, "Missing constraint implementation"):
            generator.render_outputs(root)

    def test_native_api_inventory_uses_named_annotations_and_native_branch(self):
        root, _ = self.fixture()
        path = root / "addons/necronomidom/runtime/DocumentHost.Test.cs"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(
            '#if NECRONOMIDOM_GDEXTENSION\n'
            '[Signal(Name = "changed")]\n'
            '#else\n[Signal]\n#endif\n'
            'public delegate void ChangedEventHandler(string value);\n'
            '[BindMethod(Name = "set_value")]\n'
            'public void SetValue(string selector,\n string value) { }\n'
            'public void NotBound() { }\n',
            encoding="utf-8",
        )
        api, inputs = generator.native_api(root)
        self.assertEqual([item["name"] for item in api["methods"]], ["set_value"])
        self.assertEqual(api["methods"][0]["parameters"], "string selector, string value")
        self.assertEqual([item["name"] for item in api["signals"]], ["changed"])
        self.assertEqual(inputs, [path.relative_to(root)])
        output = generator.render_outputs(root)
        self.assertIn("`set_value`", output[generator.OUTPUTS[0]])
        inventory = json.loads(output[generator.OUTPUTS[1]])
        self.assertEqual(inventory["nativeApi"], api)

    def test_unrecognized_native_api_shape_fails_closed(self):
        root, _ = self.fixture()
        path = root / "native/src/DocumentHost.Test.cs"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('[BindMethod]\\npublic void Unnamed() { }\\n', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Unrecognized native binding"):
            generator.native_api(root)
        path.write_text('#if UNKNOWN_SYMBOL\\n#endif\\n', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Unsupported API reference preprocessor"):
            generator.native_api(root)


if __name__ == "__main__":
    unittest.main()
