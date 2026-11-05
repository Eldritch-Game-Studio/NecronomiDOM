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
            ("pull_request", "refs/pull/1/merge", ["README.md"], False, "docs"),
            ("push", "refs/heads/main", ["docs/images/ui.gif", "native/README.md"], False, "docs"),
            ("pull_request", "refs/pull/1/merge", ["addons/necronomidom/core/Layout/FlowLayout.cs"], False, "fast"),
            ("push", "refs/heads/main", ["native/build.sh"], False, "fast"),
            ("push", "refs/heads/main", ["README.md", "unknown/new.code"], False, "fast"),
            ("pull_request", "refs/pull/1/merge", ["README.md", "native/godot-dotnet.lock"], False, "full"),
            ("push", "refs/tags/v1.2.3", ["README.md"], False, "full"),
            ("push", "refs/tags/v1.2.3", [], True, "full"),
            ("workflow_dispatch", "refs/heads/main", [], False, "full"),
            ("workflow_dispatch", "refs/heads/main", [], True, "partial"),
            ("workflow_dispatch", "refs/tags/v1.2.3", [], True, "partial"),
            ("workflow_dispatch", "refs/tags/v1.2.3", [], False, "full"),
            ("push", "refs/heads/main", [], False, "fast"),
        ]
        for event, ref, paths, partial, expected in cases:
            with self.subTest(event=event, ref=ref, paths=paths, partial=partial):
                result = ci.policy(event, ref, paths, partial)
                self.assertEqual(result, {"mode": expected, "code": expected != "docs",
                                          "full": expected == "full", "package": expected in {"full", "partial"}})

    def test_native_and_build_inputs_require_full_pr_gate(self):
        paths = ["native/src/DocumentHost.cs", "native/tests/lifecycle.gd", "native/patches/fix.patch",
                 "addons/necronomidom/runtime/DocumentHost.cs", ".github/workflows/ci.yml",
                 "Directory.Build.props", "Directory.Packages.props", "global.json", "NuGet.Config",
                 "packages.lock.json", "Some.csproj", "test.targets", "NecronomiDOM.slnx",
                 "scripts/qualify-stock-package.py", "future/build.sh", "extension.gdextension"]
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
        root = self.fixture()
        (root / "native").mkdir()
        original = root / "native/bridge.cs"
        original.write_text("native source", encoding="utf-8")
        first = self.commit(root)
        self.assertEqual(ci.changed_paths("push", {"before": "0" * 40, "after": first}, root), ["native/bridge.cs"])
        original.rename(root / "README.md")
        renamed = self.commit(root)
        paths = ci.changed_paths("push", {"before": first, "after": renamed}, root)
        self.assertEqual(set(paths), {"native/bridge.cs", "README.md"})
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
