"""Exercise managed-host provenance, release idempotency and dispatch against a recorded gh, without network."""

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock
import zipfile

SPEC = importlib.util.spec_from_file_location("publish_managed", Path(__file__).with_name("publish-managed.py"))
publish = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(publish)

SHA = "31366b0dfb9c" + "0" * 28
ENV = {"GITHUB_RUN_ID": "77", "GITHUB_RUN_ATTEMPT": "2",
       "GITHUB_WORKFLOW_REF": "Eldritch-Game-Studio/NecronomiDOM/.github/workflows/ci-local.yml@refs/heads/main"}


def package(directory, sha=SHA, files=None, tamper=None):
    files = files or {"core/NecronomiDom.csproj": b"<Project />\n", "runtime/DocumentHost.cs": b"class DocumentHost {}\n"}
    metadata = {"schema": "eldritch.managed-addon/1", "addon": "necronomidom", "revision": sha,
                "profile": "managed-host", "repository": "https://github.com/Eldritch-Game-Studio/NecronomiDOM",
                "files": {name: hashlib.sha256(data).hexdigest() for name, data in files.items()}}
    path = Path(directory) / f"necronomidom-managed-{sha}.zip"
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr("addon-package.json", json.dumps(metadata))
        for name, data in files.items():
            archive.writestr(f"addons/necronomidom/{name}", (tamper or {}).get(name, data))
    return path


class FakeGh:
    """Records commands; answers release view/download from an in-memory release table."""

    def __init__(self, releases=None, ancestor=True, runs=()):
        self.releases = releases or {}
        self.ancestor = ancestor
        self.runs = list(runs)
        self.calls = []

    def __call__(self, command, **kwargs):
        command = list(command)
        self.calls.append((command, kwargs))
        stdout, returncode, stderr = "", 0, ""
        if command[:2] == ["git", "merge-base"]:
            returncode = 0 if self.ancestor else 1
        elif command[:3] == ["gh", "release", "view"]:
            release = self.releases.get(command[3])
            if release is None:
                returncode, stderr = 1, "release not found\n"
            else:
                stdout = json.dumps({"isPrerelease": release["prerelease"]})
        elif command[:3] == ["gh", "release", "download"]:
            target = Path(command[command.index("-D") + 1])
            (target / "provenance.json").write_text(json.dumps(self.releases[command[3]]["provenance"]))
        elif command[:2] == ["gh", "api"] and "/runs?" in command[2]:
            stdout = json.dumps({"workflow_runs": self.runs})
        return subprocess.CompletedProcess(command, returncode, stdout, stderr)

    def commands(self, *prefix):
        return [command for command, _ in self.calls if command[:len(prefix)] == list(prefix)]


class PublishManagedTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        patcher = mock.patch.dict(os.environ, ENV)
        patcher.start()
        self.addCleanup(patcher.stop)

    def record(self, ref="refs/heads/main", qualified=77, path=None):
        path = path or package(self.directory)
        record = publish.provenance(path, SHA, ref, qualified)
        output = self.directory / "provenance.json"
        output.write_text(json.dumps(record))
        return path, output, record

    def test_provenance_identifies_exact_ci_package(self):
        path, _, record = self.record()
        self.assertEqual(record, {
            "schema": "eldritch.managed-provenance/1", "package": "necronomidom",
            "repository": "Eldritch-Game-Studio/NecronomiDOM", "sha": SHA, "ref": "refs/heads/main", "branch": "main",
            "workflow": ENV["GITHUB_WORKFLOW_REF"], "run_id": 77, "run_attempt": 2, "qualified_run_id": 77,
            "release_tag": "managed-31366b0dfb9c", "zip": path.name,
            "zip_sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "upstream": {}})

    def test_provenance_rejects_tampered_or_foreign_packages(self):
        with self.assertRaisesRegex(SystemExit, "member hash"):
            publish.provenance(package(self.directory, tamper={"runtime/DocumentHost.cs": b"x"}), SHA,
                               "refs/heads/main", 77)
        other = "a" * 40
        with self.assertRaisesRegex(SystemExit, "package name"):
            publish.provenance(package(self.directory, sha=other), SHA, "refs/heads/main", 77)
        path = package(self.directory)
        with zipfile.ZipFile(path, "a") as archive:
            archive.writestr("addons/necronomidom/extra.cs", b"")
        with self.assertRaisesRegex(SystemExit, "members differ"):
            publish.provenance(path, SHA, "refs/heads/main", 77)

    def test_load_rejects_bytes_that_differ_from_provenance(self):
        path, output, record = self.record()
        output.write_text(json.dumps({**record, "zip_sha256": "0" * 64}))
        with self.assertRaisesRegex(SystemExit, "differ from provenance"):
            publish.load(output, path)

    def test_main_commit_creates_latest_free_prerelease(self):
        path, output, _ = self.record()
        gh = FakeGh()
        with mock.patch.object(publish.subprocess, "run", gh):
            self.assertEqual(publish.publish(path, output), "managed-31366b0dfb9c")
        [create] = gh.commands("gh", "release", "create")
        self.assertEqual(create[3:6], ["managed-31366b0dfb9c", str(path), f"{output}#provenance.json"])
        self.assertIn("--prerelease", create)
        self.assertIn("--latest=false", create)
        self.assertEqual(create[create.index("--target") + 1], SHA)

    def test_tag_promotes_identical_prerelease_and_refuses_different_bytes(self):
        path, output, record = self.record(ref="refs/tags/v1.2.0")
        gh = FakeGh({"managed-31366b0dfb9c": {"prerelease": True, "provenance": record}})
        with mock.patch.object(publish.subprocess, "run", gh):
            publish.publish(path, output)
        self.assertEqual(gh.commands("gh", "release", "create"), [])
        self.assertEqual(len(gh.commands("gh", "release", "upload")), 1)
        [edit] = gh.commands("gh", "release", "edit")
        self.assertIn("--prerelease=false", edit)
        gh = FakeGh({"managed-31366b0dfb9c": {"prerelease": False, "provenance": {**record, "zip_sha256": "1" * 64}}})
        with mock.patch.object(publish.subprocess, "run", gh), self.assertRaisesRegex(SystemExit, "different package"):
            publish.publish(path, output)

    def test_rerun_of_published_release_changes_nothing(self):
        path, output, record = self.record()
        gh = FakeGh({"managed-31366b0dfb9c": {"prerelease": True, "provenance": record}})
        with mock.patch.object(publish.subprocess, "run", gh):
            publish.publish(path, output)
        self.assertEqual([c for c, _ in gh.calls if c[:3] in (["gh", "release", "create"], ["gh", "release", "edit"],
                                                             ["gh", "release", "upload"])], [])

    def test_dispatch_sends_contract_payload_with_host_login(self):
        path, output, record = self.record()
        gh = FakeGh()
        with mock.patch.dict(os.environ, {"GH_TOKEN": "workflow", "GITHUB_TOKEN": "workflow"}), \
                mock.patch.object(publish.subprocess, "run", gh):
            publish.dispatch(path, output, "Eldritch-Game-Studio/Dirge")
        [(command, kwargs)] = gh.calls
        self.assertEqual(command[:5], ["gh", "api", "--method", "POST", "repos/Eldritch-Game-Studio/Dirge/dispatches"])
        self.assertEqual(json.loads(kwargs["input"]), {"event_type": "upstream-package-published", "client_payload": {
            "package": "necronomidom", "sha": SHA, "release_tag": "managed-31366b0dfb9c",
            "zip_sha256": record["zip_sha256"]}})
        self.assertNotIn("GH_TOKEN", kwargs["env"])
        self.assertNotIn("GITHUB_TOKEN", kwargs["env"])

    def test_source_requires_main_history_and_backfill_qualification(self):
        with mock.patch.object(publish.subprocess, "run", FakeGh()):
            self.assertEqual(publish.source(SHA, "refs/heads/main", False), 77)
            with self.assertRaisesRegex(SystemExit, "only from main"):
                publish.source(SHA, "refs/pull/4/merge", False)
        with mock.patch.object(publish.subprocess, "run", FakeGh(ancestor=False)), \
                self.assertRaisesRegex(SystemExit, "not on origin/main"):
            publish.source(SHA, "refs/tags/v1.2.0", False)
        runs = [{"id": 5, "head_sha": SHA, "head_branch": "main", "conclusion": "success"},
                {"id": 9, "head_sha": SHA, "head_branch": "main", "conclusion": "success"},
                {"id": 12, "head_sha": SHA, "head_branch": "feature", "conclusion": "success"}]
        with mock.patch.object(publish.subprocess, "run", FakeGh(runs=runs)):
            self.assertEqual(publish.source(SHA, "refs/heads/main", True), 9)
        with mock.patch.object(publish.subprocess, "run", FakeGh(runs=runs[2:])), \
                self.assertRaisesRegex(SystemExit, "cannot be backfilled"):
            publish.source(SHA, "refs/heads/main", True)


if __name__ == "__main__":
    unittest.main()
