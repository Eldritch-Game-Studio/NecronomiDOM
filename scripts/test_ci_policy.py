"""Exercise event/path decisions and real Git change discovery, without engine/native builds."""

import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location("ci_policy", Path(__file__).with_name("ci-policy.py"))
ci = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ci)


class PolicyTests(unittest.TestCase):
    def test_event_path_matrix(self):
        cases = [
            ("pull_request", "refs/pull/1/merge", ["README.md"], False, "docs", False),
            ("push", "refs/heads/main", ["docs/images/ui.gif", "native/README.md"], False, "docs", False),
            ("pull_request", "refs/pull/1/merge", ["native/README.md"], False, "docs", False),
            ("pull_request", "refs/pull/1/merge", ["README.md", ".gitattributes"], False, "full", False),
            ("push", "refs/heads/main", [".gitignore"], False, "fast", True),
            ("pull_request", "refs/pull/1/merge", ["addons/necronomidom/core/Layout/FlowLayout.cs"], False, "fast", False),
            ("push", "refs/heads/main", ["native/build.sh"], False, "fast", True),
            ("push", "refs/heads/main", ["README.md", "unknown/new.code"], False, "fast", True),
            ("pull_request", "refs/pull/1/merge", ["README.md", "native/godot-dotnet.lock"], False, "full", False),
            ("pull_request", "refs/pull/1/merge", ["scripts/windows-proton.py"], True, "full", False),
            ("push", "refs/tags/v1.2.3", ["README.md"], False, "full", True),
            ("push", "refs/tags/v1.2.3", [], True, "full", True),
            ("workflow_dispatch", "refs/heads/main", [], False, "full", True),
            ("workflow_dispatch", "refs/heads/main", [], True, "partial", False),
            ("workflow_dispatch", "refs/tags/v1.2.3", [], True, "partial", False),
            ("workflow_dispatch", "refs/tags/v1.2.3", [], False, "full", True),
            ("workflow_dispatch", "refs/heads/feature", [], False, "full", False),
            ("workflow_dispatch", "refs/heads/feature", [], True, "partial", False),
            ("push", "refs/heads/main", [], False, "fast", True),
        ]
        for event, ref, paths, partial, expected, managed in cases:
            with self.subTest(event=event, ref=ref, paths=paths, partial=partial):
                result = ci.policy(event, ref, paths, partial)
                self.assertEqual({key: result[key] for key in ("mode", "code", "full", "package")},
                                 {"mode": expected, "code": expected != "docs",
                                  "full": expected == "full", "package": expected in {"full", "partial"}})
                self.assertEqual(result["managed"], managed)

    def test_backfill_publishes_only_the_managed_package(self):
        result = ci.policy("workflow_dispatch", "refs/heads/main", [], partial=False, backfill=True)
        self.assertEqual(result, {"mode": "backfill", "code": False, "full": False, "package": False,
                                  "consumer": False, "managed": True})
        self.assertFalse(ci.policy("push", "refs/heads/feature", ["addons/necronomidom/core/Dom/Node.cs"])["managed"])
        self.assertFalse(ci.policy("pull_request", "refs/pull/1/merge", ["scripts/ci-policy.py"])["managed"])

    def test_consumer_job_runs_for_prs_changing_host_compiled_source(self):
        for path, expected in [("addons/necronomidom/core/Layout/FlowLayout.cs", True),
                               ("addons/necronomidom/runtime/DocumentHost.cs", True),
                               ("addons/necronomidom/vendor/AngleSharp/src/AngleSharp/Dom/Node.cs", True),
                               ("addons/necronomidom/NecronomiDom.targets", True),
                               ("addons/necronomidom/runtime/README.md", False),
                               ("tests/NecronomiDom.Tests/Dom/NodeTests.cs", False),
                               ("native/src/Extension.cs", False),
                               ("addons/necronomidom/fonts/OFL.txt", False)]:
            with self.subTest(path=path):
                self.assertEqual(ci.policy("pull_request", "refs/pull/1/merge", [path])["consumer"], expected)
                self.assertFalse(ci.policy("push", "refs/heads/main", [path])["consumer"])

    def test_native_and_build_inputs_require_full_pr_gate(self):
        paths = ["native/src/DocumentHost.cs", "native/tests/lifecycle.gd", "native/patches/fix.patch",
                 "addons/necronomidom/runtime/DocumentHost.cs", ".github/workflows/ci.yml",
                 "Directory.Build.props", "Directory.Packages.props", "global.json", "NuGet.Config",
                 "packages.lock.json", "Some.csproj", "test.targets", "NecronomiDOM.slnx",
                 "scripts/qualify-stock-package.py", "scripts/windows-proton.py",
                 "scripts/assemble-package.py", ".github/workflows/ci-local.yml",
                 "scripts/project-runtime-package.py", "scripts/test_runtime_package.py",
                 "scripts/download-package.py",
                 "future/build.sh", "extension.gdextension",
                 ".gitattributes", ".gitignore", "addons/necronomidom/.gitattributes",
                 "addons/necronomidom/.gitignore"]
        for path in paths:
            with self.subTest(path=path):
                self.assertEqual(ci.policy("pull_request", "refs/pull/1/merge", [path])["mode"], "full")

    def fixture(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        self.git(root, "init", "-q", "-b", "main")
        self.git(root, "config", "user.name", "CI tests")
        self.git(root, "config", "user.email", "ci@example.invalid")
        return root

    def git(self, root, *args):
        return subprocess.check_output(["git", *args], cwd=root).decode().strip()

    def commit(self, root):
        self.git(root, "add", "--all")
        self.git(root, "commit", "-qm", "fixture")
        return self.git(root, "rev-parse", "HEAD")

    def test_first_push_deletion_and_rename_preserve_both_policy_inputs(self):
        for relative in ("native/bridge.cs", ".gitattributes", ".gitignore", "addons/necronomidom/.gitignore"):
            with self.subTest(original=relative):
                root = self.fixture()
                original = root / relative
                original.parent.mkdir(parents=True, exist_ok=True)
                original.write_text("native or packaging source", encoding="utf-8")
                first = self.commit(root)
                self.assertEqual(ci.changed_paths("push", {"before": "0" * 40, "after": first}, root), [relative])
                original.rename(root / "README.md")
                renamed = self.commit(root)
                paths = ci.changed_paths("push", {"before": first, "after": renamed}, root)
                self.assertEqual(set(paths), {relative, "README.md"})
                self.assertEqual(ci.policy("pull_request", "refs/pull/1/merge", paths)["mode"], "full")
                (root / "README.md").unlink()
                deleted = self.commit(root)
                self.assertEqual(ci.changed_paths("push", {"before": renamed, "after": deleted}, root), ["README.md"])

    def test_pr_diff_uses_merge_base_not_unrelated_base_changes(self):
        root = self.fixture()
        (root / "README.md").write_text("base", encoding="utf-8")
        self.commit(root)
        self.git(root, "checkout", "-qb", "topic")
        (root / "README.md").write_text("topic", encoding="utf-8")
        head = self.commit(root)
        self.git(root, "checkout", "-q", "main")
        (root / "native").mkdir()
        (root / "native/build.sh").write_text("new base tooling", encoding="utf-8")
        base = self.commit(root)
        paths = ci.changed_paths("pull_request", {"pull_request": {"base": {"sha": base}, "head": {"sha": head}}}, root)
        self.assertEqual(paths, ["README.md"])
        self.assertEqual(ci.policy("pull_request", "refs/pull/1/merge", paths)["mode"], "docs")


if __name__ == "__main__":
    unittest.main()
