"""Exercise API-baseline diagnostic attribution and declaration updates without building."""

import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest import mock

SPEC = importlib.util.spec_from_file_location("api_baseline", Path(__file__).with_name("api-baseline.py"))
api = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(api)

CORE = "/work/addons/necronomidom/core/NecronomiDom.csproj"
RUNTIME = "/work/api/runtime/NecronomiDom.Runtime.Api.csproj"
OUTPUT = f"""\
/work/addons/necronomidom/core/Dom/Node.cs(12,17): error RS0016: Symbol 'NecronomiDom.Dom.Node.Clone(bool deep = false) -> NecronomiDom.Dom.Node!' is not part of the declared public API [{CORE}]
/work/addons/necronomidom/runtime/DocumentHost.cs(40,9): error RS0017: Symbol 'NecronomiDom.Runtime.DocumentHost.Click(string! selector) -> void' is part of the declared public API, but is either not public or could not be found [{RUNTIME}]
/work/addons/necronomidom/runtime/DocumentHost.cs(41,9): error RS0016: Symbol 'NecronomiDom.Runtime.DocumentHost.Key(char key = 'x') -> void' is not part of the declared public API [{RUNTIME}]
/work/addons/necronomidom/runtime/DocumentHost.cs(41,9): error RS0016: Symbol 'NecronomiDom.Runtime.DocumentHost.Key(char key = 'x') -> void' is not part of the declared public API [{RUNTIME}]
/work/addons/necronomidom/runtime/DocumentHost.cs(5,1): warning CS0168: The variable 'x' is declared but never used [{RUNTIME}]
"""


class ApiBaselineTests(unittest.TestCase):
    def test_diagnostics_are_attributed_to_their_baseline(self):
        found = api.diagnostics(OUTPUT)
        self.assertEqual(found, {
            api.ROOT / "api/core": {"RS0016": {"NecronomiDom.Dom.Node.Clone(bool deep = false) -> NecronomiDom.Dom.Node!"},
                                    "RS0017": set()},
            api.ROOT / "api/runtime": {"RS0016": {"NecronomiDom.Runtime.DocumentHost.Key(char key = 'x') -> void"},
                                       "RS0017": {"NecronomiDom.Runtime.DocumentHost.Click(string! selector) -> void"}},
        })

    def test_unexpected_project_is_refused(self):
        line = OUTPUT.splitlines()[0].replace(CORE, "/work/demo/Other.csproj")
        with self.assertRaisesRegex(SystemExit, "unexpected project"):
            api.diagnostics(line)

    def test_update_declares_additions_unshipped_and_drops_removals_everywhere(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            baselines = {"NecronomiDom.csproj": root / "core", "NecronomiDom.Runtime.Api.csproj": root / "runtime"}
            removed = "NecronomiDom.Runtime.DocumentHost.Click(string! selector) -> void"
            api.write(root / "runtime/PublicAPI.Shipped.txt", {removed, "NecronomiDom.Runtime.DocumentHost"})
            builds = iter([(1, OUTPUT), (0, "Build succeeded.\n")])
            with mock.patch.object(api, "BASELINES", baselines), mock.patch.object(api, "build", lambda: next(builds)):
                self.assertEqual(api.update(), 0)
            self.assertEqual((root / "runtime/PublicAPI.Shipped.txt").read_text(),
                             "#nullable enable\nNecronomiDom.Runtime.DocumentHost\n")
            self.assertEqual(api.read(root / "runtime/PublicAPI.Unshipped.txt"),
                             ["NecronomiDom.Runtime.DocumentHost.Key(char key = 'x') -> void"])
            self.assertEqual(api.read(root / "core/PublicAPI.Unshipped.txt"),
                             ["NecronomiDom.Dom.Node.Clone(bool deep = false) -> NecronomiDom.Dom.Node!"])


if __name__ == "__main__":
    unittest.main()
