"""Exercise managed-host provenance, release idempotency and dispatch against a recorded gh, without network."""

import copy
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
ENV = {"GITHUB_RUN_ID": "77", "GITHUB_RUN_ATTEMPT": "2", "GITHUB_SHA": SHA,
       "GITHUB_REPOSITORY": publish.REPOSITORY, "GITHUB_REF": "refs/heads/main",
       "GITHUB_WORKFLOW_REF": "Eldritch-Game-Studio/NecronomiDOM/.github/workflows/ci.yml@refs/heads/main"}


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


def run_proof(run_id=77, **changes):
    return {"id": run_id, "run_attempt": 2, "head_sha": SHA, "head_branch": "main",
            "event": "push", "status": "completed", "conclusion": "success",
            "repository": {"full_name": publish.REPOSITORY},
            "head_repository": {"full_name": publish.REPOSITORY},
            "path": ".github/workflows/ci.yml", **changes}


class FakeGh:
    """Records commands; answers release view/download from an in-memory release table."""

    def __init__(self, releases=None, ancestor=True, runs=(), proofs=None, tag_sha=SHA):
        self.releases = releases or {}
        self.ancestor = ancestor
        self.runs = list(runs)
        self.proofs = proofs if proofs is not None else {77: run_proof()}
        self.tag_sha = tag_sha
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
            release = self.releases[command[3]]
            (target / "provenance.json").write_text(json.dumps(release["provenance"]))
            (target / release["provenance"]["zip"]).write_bytes(release["bytes"])
        elif command[:3] == ["gh", "release", "create"]:
            self.releases[command[3]] = {
                "prerelease": "--prerelease" in command,
                "provenance": json.loads(Path(command[5].split("#")[0]).read_text()),
                "bytes": Path(command[4]).read_bytes()}
        elif command[:2] == ["gh", "api"] and "/runs?" in command[2]:
            stdout = json.dumps({"workflow_runs": self.runs})
        elif command[:2] == ["gh", "api"] and "/commits/" in command[2]:
            stdout = json.dumps({"sha": self.tag_sha})
        elif command[:2] == ["gh", "api"] and "/actions/runs/" in command[2]:
            proof = self.proofs.get(int(command[2].split("/runs/")[1].split("/")[0]))
            if proof is None:
                returncode, stderr = 1, "HTTP 404: Not Found"
            else:
                stdout = json.dumps(proof)
        if returncode and kwargs.get("check"):
            raise subprocess.CalledProcessError(returncode, command, stdout, stderr)
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
        with mock.patch.dict(os.environ, {"GITHUB_WORKFLOW_REF":
                f"{publish.REPOSITORY}/.github/workflows/{publish.CI_WORKFLOW}@{ref}"}):
            record = publish.provenance(path, SHA, ref, qualified)
        output = self.directory / "provenance.json"
        output.write_text(json.dumps(record))
        return path, output, record

    def released(self, path, record, **kwargs):
        return FakeGh({record["release_tag"]: {"prerelease": True, "provenance": copy.deepcopy(record),
                                              "bytes": path.read_bytes()}}, **kwargs)

    def assert_no_release_mutation_or_notification(self, gh):
        for prefix in (("gh", "release", "create"), ("gh", "release", "edit"),
                       ("gh", "release", "upload"), ("gh", "api", "--method", "POST")):
            self.assertEqual(gh.commands(*prefix), [])

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

    def test_tag_promotion_preserves_original_provenance_and_assets(self):
        path, _, original = self.record()
        path, output, _ = self.record(ref="refs/tags/v1.2.0", path=path)
        gh = self.released(path, original)
        before = copy.deepcopy(gh.releases)
        with mock.patch.object(publish.subprocess, "run", gh):
            publish.publish(path, output)
        self.assertEqual(gh.commands("gh", "release", "create"), [])
        self.assertEqual(gh.commands("gh", "release", "upload"), [])
        [edit] = gh.commands("gh", "release", "edit")
        self.assertIn("--prerelease=false", edit)
        self.assertEqual(gh.releases, before)

    def test_rerun_of_published_release_changes_nothing(self):
        path, output, record = self.record()
        gh = self.released(path, record)
        output.write_text(json.dumps({**record, "run_id": 88, "run_attempt": 1, "qualified_run_id": 88}))
        with mock.patch.object(publish.subprocess, "run", gh):
            publish.publish(path, output)
        self.assertEqual([c for c, _ in gh.calls if c[:3] in (["gh", "release", "create"], ["gh", "release", "edit"],
                                                             ["gh", "release", "upload"])], [])
        self.assertEqual(gh.releases[record["release_tag"]]["provenance"], record)

    def test_dispatch_sends_contract_payload_with_host_login(self):
        path, output, record = self.record()
        gh = self.released(path, record)
        with mock.patch.dict(os.environ, {"GH_TOKEN": "workflow", "GITHUB_TOKEN": "workflow"}), \
                mock.patch.object(publish.subprocess, "run", gh):
            publish.dispatch(path, output, "Eldritch-Game-Studio/Dirge")
        [(command, kwargs)] = [(c, kw) for c, kw in gh.calls if c[:4] == ["gh", "api", "--method", "POST"]]
        self.assertEqual(command[:5], ["gh", "api", "--method", "POST", "repos/Eldritch-Game-Studio/Dirge/dispatches"])
        self.assertEqual(json.loads(kwargs["input"]), {"event_type": "upstream-package-published", "client_payload": {
            "package": "necronomidom", "sha": SHA, "release_tag": "managed-31366b0dfb9c",
            "zip_sha256": record["zip_sha256"]}})
        self.assertNotIn("GH_TOKEN", kwargs["env"])
        self.assertNotIn("GITHUB_TOKEN", kwargs["env"])

    def test_identical_package_rejects_missing_or_wrong_run_proof_before_reuse_and_dispatch(self):
        path, output, record = self.record()
        changes = [
            None, {"head_sha": "a" * 40}, {"event": "workflow_dispatch"}, {"head_branch": "other"},
            {"repository": {"full_name": "attacker/repo"}}, {"head_repository": {"full_name": "attacker/repo"}},
            {"path": ".github/workflows/untrusted.yml"}, {"id": 78}, {"run_attempt": 1},
            {"status": "completed", "conclusion": "failure"},
            {"status": "in_progress", "conclusion": None},
        ]
        for change in changes:
            for operation in ("publish", "dispatch"):
                with self.subTest(change=change, operation=operation):
                    gh = self.released(path, record, proofs={} if change is None else {77: run_proof(**change)})
                    before = copy.deepcopy(gh.releases)
                    # Historical proof cannot borrow the current run's live-publication exception.
                    with mock.patch.dict(os.environ, {"GITHUB_RUN_ID": "88"}), \
                            mock.patch.object(publish.subprocess, "run", gh), \
                            self.assertRaises((SystemExit, subprocess.CalledProcessError)):
                        if operation == "publish":
                            publish.publish(path, output)
                        else:
                            publish.dispatch(path, output, "Eldritch-Game-Studio/Dirge")
                    self.assert_no_release_mutation_or_notification(gh)
                    self.assertEqual(gh.releases, before)

    def test_canonical_assets_and_tag_are_verified_not_just_matching_record_digest(self):
        path, output, record = self.record()
        for defect in ("tag", "bytes", "digest", "source", "workflow", "schema", "missing-run", "branch", "upstream"):
            with self.subTest(defect=defect):
                gh = self.released(path, record)
                release = gh.releases[record["release_tag"]]
                if defect == "tag":
                    gh.tag_sha = "a" * 40
                elif defect == "bytes":
                    release["bytes"] = b"corrupt"
                elif defect == "missing-run":
                    release["provenance"].pop("qualified_run_id")
                else:
                    key, value = {
                        "digest": ("zip_sha256", "0" * 64), "source": ("sha", "a" * 40),
                        "workflow": ("workflow", "attacker/repo/.github/workflows/ci-local.yml@refs/heads/main"),
                        "schema": ("schema", "foreign"), "branch": ("branch", "other"),
                        "upstream": ("upstream", {"foreign": "unproven"}),
                    }[defect]
                    release["provenance"][key] = value
                before = copy.deepcopy(gh.releases)
                with mock.patch.object(publish.subprocess, "run", gh), self.assertRaises(SystemExit):
                    publish.dispatch(path, output, "Eldritch-Game-Studio/Dirge")
                self.assert_no_release_mutation_or_notification(gh)
                self.assertEqual(gh.releases, before)

    def test_backfill_needs_successful_exact_main_push_separate_from_producer(self):
        path, output, record = self.record(qualified=5)
        proofs = {77: run_proof(event="workflow_dispatch", head_sha="b" * 40), 5: run_proof(5)}
        gh = self.released(path, record, proofs=proofs)
        with mock.patch.object(publish.subprocess, "run", gh):
            publish.publish(path, output)
            publish.dispatch(path, output, "Eldritch-Game-Studio/Dirge")
        for change in (None, {"event": "workflow_dispatch"}, {"head_sha": "a" * 40},
                       {"head_branch": "v1.2.0"}, {"status": "in_progress", "conclusion": None}):
            with self.subTest(change=change):
                bad = {77: proofs[77]}
                if change is not None:
                    bad[5] = run_proof(5, **change)
                gh = self.released(path, record, proofs=bad)
                with mock.patch.object(publish.subprocess, "run", gh), \
                        self.assertRaises((SystemExit, subprocess.CalledProcessError)):
                    publish.dispatch(path, output, "Eldritch-Game-Studio/Dirge")
                self.assert_no_release_mutation_or_notification(gh)

    def test_new_release_can_notify_only_from_its_exact_live_producer(self):
        path, output, record = self.record()
        live = run_proof(status="in_progress", conclusion=None)
        gh = FakeGh(proofs={77: live})
        with mock.patch.object(publish.subprocess, "run", gh):
            publish.publish(path, output)
            publish.dispatch(path, output, "Eldritch-Game-Studio/Dirge")
        self.assertEqual(len(gh.commands("gh", "api", "--method", "POST")), 1)
        for key, value in (("GITHUB_RUN_ATTEMPT", "3"), ("GITHUB_SHA", "a" * 40),
                           ("GITHUB_REPOSITORY", "attacker/repo"), ("GITHUB_REF", "refs/pull/1/merge"),
                           ("GITHUB_WORKFLOW_REF", "untrusted")):
            with self.subTest(key=key):
                gh = self.released(path, record, proofs={77: live})
                with mock.patch.dict(os.environ, {key: value}), mock.patch.object(publish.subprocess, "run", gh), \
                        self.assertRaises(SystemExit):
                    publish.dispatch(path, output, "Eldritch-Game-Studio/Dirge")
                self.assert_no_release_mutation_or_notification(gh)

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
