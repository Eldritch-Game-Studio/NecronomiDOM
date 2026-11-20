import hashlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile

SPEC = importlib.util.spec_from_file_location("requalify", Path(__file__).with_name("requalify-managed.py"))
requalify = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(requalify)
REPOSITORY = requalify.REPOSITORY
PREFIX = f"repos/{REPOSITORY}"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def git(root, *args):
    return subprocess.check_output(["git", *args], cwd=root, text=True).strip()


class RequalifyTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        root = self.root = Path(self.temporary.name)
        git(root, "init", "-q", "-b", "main")
        git(root, "config", "user.email", "ci@example.invalid")
        git(root, "config", "user.name", "CI")
        (root / "addons/necronomidom").mkdir(parents=True)
        (root / "addons/necronomidom/a.gd").write_text("extends Node\n")
        (root / "addons/necronomidom/x.gdextension").write_text("host\n")
        git(root, "add", ".")
        git(root, "commit", "-qm", "release")
        self.sha = git(root, "rev-parse", "HEAD")
        (root / "README").write_text("later\n")
        git(root, "add", ".")
        git(root, "commit", "-qm", "later")
        self.head = git(root, "rev-parse", "HEAD")
        git(root, "update-ref", "refs/remotes/origin/main", self.head)
        self.zip_name = f"necronomidom-managed-{self.sha}.zip"
        self.package = self.build_package(b"extends Node\n")
        self.original = (json.dumps({
            "schema": "eldritch.managed-provenance/1", "package": "necronomidom", "repository": REPOSITORY,
            "branch": "main", "sha": self.sha, "ref": "refs/heads/main", "release_tag": f"managed-{self.sha[:12]}",
            "zip": self.zip_name, "zip_sha256": digest(self.package), "upstream": {},
            "run_id": 5, "run_attempt": 1, "qualified_run_id": 5,
        }) + "\n").encode()
        self.rebuilt = self.package

    def tearDown(self):
        self.temporary.cleanup()

    def build_package(self, content):
        data = io.BytesIO()
        with zipfile.ZipFile(data, "w") as bundle:
            bundle.writestr("addon-package.json", json.dumps({
                "schema": "eldritch.managed-addon/1", "addon": "necronomidom", "revision": self.sha,
                "repository": "https://github.com/" + REPOSITORY, "profile": "managed-host",
                "files": {"a.gd": digest(content)}}))
            bundle.writestr("addons/necronomidom/a.gd", content)
        return data.getvalue()

    def artifact_zip(self):
        data = io.BytesIO()
        with zipfile.ZipFile(data, "w") as archive:
            archive.writestr(self.zip_name, self.rebuilt)
            archive.writestr("provenance.json", json.dumps({
                "sha": self.sha, "run_id": 77, "run_attempt": 1, "qualified_run_id": 77}))
        return data.getvalue()

    def api(self, jobs=None, event="workflow_dispatch"):
        names = jobs or [*requalify.REQUIRED_JOBS, "native-apple (osx-x64)"]
        job_list = [{"name": f"local / {name}", "status": "completed", "conclusion": "success", "run_attempt": 1}
                    for name in names]
        job_list.append({"name": "local / managed-release", "status": "completed", "conclusion": "skipped",
                         "run_attempt": 1})
        run = {"id": 77, "head_sha": self.head, "repository": {"full_name": REPOSITORY},
               "head_repository": {"full_name": REPOSITORY}, "path": ".github/workflows/ci.yml",
               "head_branch": "main", "event": event, "status": "completed", "conclusion": "success",
               "run_attempt": 1}
        archive = self.artifact_zip()
        release = {"tag_name": f"managed-{self.sha[:12]}", "draft": False, "assets": [
            {"name": "provenance.json", "id": 1, "digest": "sha256:" + digest(self.original)},
            {"name": self.zip_name, "id": 2, "digest": "sha256:" + digest(self.package)}]}
        responses = {
            f"{PREFIX}/actions/runs/77": run,
            f"{PREFIX}/actions/runs/77/attempts/1/jobs?per_page=100&page=1": {"jobs": job_list},
            f"{PREFIX}/actions/runs/77/artifacts?per_page=100&page=1": {"artifacts": [{
                "id": 9, "name": "managed-host", "expired": False, "digest": "sha256:" + digest(archive),
                "workflow_run": {"id": 77, "head_sha": self.head, "head_branch": "main"}}]},
            f"{PREFIX}/actions/artifacts/9/zip": archive,
            f"{PREFIX}/releases/tags/managed-{self.sha[:12]}": release,
            f"{PREFIX}/releases/assets/1": self.original,
            f"{PREFIX}/releases/assets/2": self.package,
        }
        return lambda path, binary=False: responses[path]

    def test_receipt_binds_release_source_and_qualified_run(self):
        record = requalify.prepare(77, 88, 1, self.root, self.api())
        self.assertEqual(record["sha"], self.sha)
        self.assertEqual(record["qualified_source_sha"], self.sha)
        self.assertEqual((record["run_id"], record["run_attempt"], record["qualified_run_id"]), (88, 1, 77))
        self.assertEqual(record["original_provenance"], {
            "asset": "provenance.json", "asset_id": 1, "sha256": digest(self.original)})
        self.assertEqual(record["schema"], "eldritch.managed-requalification/1")
        self.assertEqual(record["upstream"], {})

    def test_rejects_different_zip_bytes(self):
        self.rebuilt = self.build_package(b"extends Node\n").replace(b"addon", b"addoN", 1)
        with self.assertRaisesRegex(ValueError, "byte-identical"):
            requalify.prepare(77, 88, 1, self.root, self.api())

    def test_rejects_incomplete_or_wrong_runs(self):
        with self.assertRaisesRegex(ValueError, "requires successful changes"):
            requalify.prepare(77, 88, 1, self.root, self.api(jobs=["admit"]))
        with self.assertRaisesRegex(ValueError, "manual dispatch"):
            requalify.prepare(77, 88, 1, self.root, self.api(event="push"))
        with self.assertRaisesRegex(ValueError, "distinct"):
            requalify.prepare(77, 77, 1, self.root, self.api())


if __name__ == "__main__":
    unittest.main()
