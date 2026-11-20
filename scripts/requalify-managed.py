#!/usr/bin/env python3
"""Prepare append-only evidence for a managed release whose original CI run is unavailable.

Never rebuild, publish, replace provenance.json, or treat the missing original run as passing.

1. Maintainers dispatch ci.yml on main with requalify_revision=<released sha>. That FULL run
   checks out the released revision, never publishes, and fails unless its managed ZIP is
   byte-identical to the released ZIP. Its head_sha is the main head at dispatch.
2. They then dispatch ci.yml on main with requalification_run=<that run ID>. This script
   authenticates the run and its managed-host artifact. The append-only job then uploads
   provenance-<publisher run ID>-<publisher attempt>.json (schema
   eldritch.managed-requalification/1). sha and qualified_source_sha are the released
   revision. run_id is this recovery publisher and qualified_run_id is the FULL run.
   original_provenance binds the untouched provenance.json by asset ID and raw-byte SHA256.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import tarfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = "necronomidom"
REPOSITORY = "Eldritch-Game-Studio/NecronomiDOM"
BRANCH = "main"
WORKFLOW = ".github/workflows/ci.yml"
ARTIFACT = "managed-host"
# Every FULL job; matrix families are matched by prefix and each member must succeed.
REQUIRED_JOBS = (
    "admit", "changes", "docs", "api", "test", "native-linux-x64", "native-platforms",
    "native-apple", "native-windows-proton", "smoke", "package", "qualify-linux-x64",
    "qualify-platforms", "qualify-apple", "qualify-windows-proton", "managed-package",
)
# Jobs a requalification run skips by design.
SKIPPED_JOBS = ("managed-release", "release", "consumer-dirge", "managed-requalification",
                "managed-requalification-release")


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def gh(path: str, binary: bool = False):
    command = ["gh", "api", path]
    if binary and "/releases/assets/" in path:
        command += ["-H", "Accept: application/octet-stream"]
    data = subprocess.check_output(command)
    return data if binary else json.loads(data)


def pages(path: str, key: str, api=gh) -> list:
    result = []
    page = 1
    while True:
        items = api(f"{path}?per_page=100&page={page}")[key]
        result.extend(items)
        if len(items) < 100:
            return result
        page += 1


def job_family(name: str) -> str:
    return name.removeprefix("local / ").split(" (", 1)[0]


def verify_run(run: dict, jobs: list, run_id: int) -> int:
    require(type(run_id) is int and run_id > 0 and run.get("id") == run_id, "Invalid qualifying run ID")
    require(re.fullmatch(r"[0-9a-f]{40}", run.get("head_sha", "")) is not None, "Invalid qualifying head")
    require(run.get("repository", {}).get("full_name") == REPOSITORY
            and run.get("head_repository", {}).get("full_name") == REPOSITORY
            and run.get("path") == WORKFLOW and run.get("head_branch") == BRANCH
            and run.get("event") == "workflow_dispatch",
            "Qualification must be a main manual dispatch of the maintained CI workflow")
    require(run.get("status") == "completed" and run.get("conclusion") == "success",
            "Independent qualification has not completed successfully")
    attempt = run.get("run_attempt")
    require(type(attempt) is int and attempt > 0, "Invalid qualifying attempt")
    for job in jobs:
        family = job_family(job.get("name", ""))
        require(job.get("run_attempt") == attempt and job.get("status") == "completed", "Job is not from this attempt")
        if family in SKIPPED_JOBS:
            require(job.get("conclusion") == "skipped", f"Requalification must not run {family}")
        else:
            require(job.get("conclusion") == "success", f"FULL qualification requires successful {job.get('name')}")
    for name in REQUIRED_JOBS:
        require(any(job_family(job.get("name", "")) == name for job in jobs),
                f"FULL exact-attempt qualification requires successful {name}")
    return attempt


def verify_package(data: bytes, sha: str, source_files: dict[str, bytes]) -> None:
    with zipfile.ZipFile(io.BytesIO(data)) as bundle:
        metadata = json.loads(bundle.read("addon-package.json"))
        require(all(metadata.get(key) == value for key, value in {
            "schema": "eldritch.managed-addon/1", "addon": PACKAGE, "revision": sha,
            "repository": "https://github.com/" + REPOSITORY, "profile": "managed-host",
        }.items()), "Package manifest does not identify the exact managed-host source")
        expected = {f"addons/{PACKAGE}/" + name: content for name, content in source_files.items()}
        names = bundle.namelist()
        require(len(names) == len(set(names)) and set(names) == set(expected) | {"addon-package.json"},
                "Package archive members differ from exact source")
        require(metadata.get("files") == {name: digest(content) for name, content in source_files.items()},
                "Package manifest differs from exact source tree")
        require(all(bundle.read(name) == content for name, content in expected.items()),
                "Package bytes differ from exact source tree")


def asset(release: dict, name: str) -> dict:
    matches = [item for item in release["assets"] if item["name"] == name]
    require(len(matches) == 1, f"Release must contain exactly one {name}")
    return matches[0]


def prepare(run_id: int, producer_id: int, producer_attempt: int, root: Path = ROOT, api=gh) -> dict:
    prefix = f"repos/{REPOSITORY}"
    run = api(f"{prefix}/actions/runs/{run_id}")
    attempt = run.get("run_attempt")
    jobs = pages(f"{prefix}/actions/runs/{run_id}/attempts/{attempt}/jobs", "jobs", api)
    attempt = verify_run(run, jobs, run_id)
    require(producer_id > 0 and producer_attempt > 0 and producer_id != run_id,
            "Recovery publisher and qualification must be distinct runs")
    artifacts = pages(f"{prefix}/actions/runs/{run_id}/artifacts", "artifacts", api)
    matches = [item for item in artifacts if item.get("name") == ARTIFACT]
    require(len(matches) == 1, f"Qualification must have exactly one {ARTIFACT} artifact")
    artifact = matches[0]
    require(not artifact.get("expired") and artifact.get("workflow_run", {}).get("id") == run_id
            and artifact["workflow_run"].get("head_sha") == run["head_sha"]
            and artifact["workflow_run"].get("head_branch") == BRANCH, "Managed artifact source differs or expired")
    archive_bytes = api(f"{prefix}/actions/artifacts/{artifact['id']}/zip", True)
    require(artifact.get("digest") == "sha256:" + digest(archive_bytes), "Managed artifact archive digest differs")
    with zipfile.ZipFile(io.BytesIO(archive_bytes)) as archive:
        built = json.loads(archive.read("provenance.json"))
        sha = built.get("sha", "")
        require(re.fullmatch(r"[0-9a-f]{40}", sha) is not None and sha != run["head_sha"]
                and built.get("run_id") == run_id and built.get("qualified_run_id") == run_id
                and built.get("run_attempt") == attempt,
                "Qualification is not a requalify_revision run of an earlier revision")
        tag, name = "managed-" + sha[:12], f"{PACKAGE}-managed-{sha}.zip"
        require(archive.namelist().count(name) == 1, "Managed artifact lacks exactly one expected ZIP")
        rebuilt = archive.read(name)
    subprocess.run(["git", "merge-base", "--is-ancestor", sha, run["head_sha"]], cwd=root, check=True)
    subprocess.run(["git", "merge-base", "--is-ancestor", run["head_sha"], f"origin/{BRANCH}"], cwd=root, check=True)
    release = api(f"{prefix}/releases/tags/{tag}")
    require(release.get("tag_name") == tag and not release.get("draft"), "Unexpected or draft managed release")
    original_asset, zip_asset = asset(release, "provenance.json"), asset(release, name)
    original_bytes = api(f"{prefix}/releases/assets/{original_asset['id']}", True)
    original = json.loads(original_bytes)
    package = api(f"{prefix}/releases/assets/{zip_asset['id']}", True)
    require(rebuilt == package, "Newly qualified ZIP is not byte-identical to released ZIP")
    checksum = digest(package)
    identity = {
        "package": PACKAGE, "repository": REPOSITORY, "branch": BRANCH, "sha": sha,
        "release_tag": tag, "zip": name, "zip_sha256": checksum, "upstream": {},
    }
    require(original.get("schema") == "eldritch.managed-provenance/1"
            and all(original.get(key) == value for key, value in identity.items()),
            "Original provenance identity or package differs")
    require(original.get("ref") == "refs/heads/" + BRANCH
            and all(type(original.get(key)) is int and original[key] > 0
                    for key in ("run_id", "run_attempt", "qualified_run_id"))
            and original["run_id"] == original["qualified_run_id"],
            "Original provenance does not identify its original main producer")
    require(original_asset.get("digest") == "sha256:" + digest(original_bytes)
            and zip_asset.get("digest") == "sha256:" + checksum, "Release asset digests differ")
    source_files = {}
    source = subprocess.check_output(["git", "archive", sha, f"addons/{PACKAGE}"], cwd=root)
    with tarfile.open(fileobj=io.BytesIO(source)) as archive:
        for member in archive:
            if member.isdir():
                continue
            require(member.isfile() and member.name.startswith(f"addons/{PACKAGE}/"), "Unsupported source member")
            name_in_addon = member.name.removeprefix(f"addons/{PACKAGE}/")
            if not name_in_addon.endswith((".gdextension", ".gdextension.uid", ".import")):
                source_files[name_in_addon] = archive.extractfile(member).read()
    verify_package(package, sha, source_files)
    # Detect reruns or qualification changes while downloading and validating the packages.
    require(api(f"{prefix}/actions/runs/{run_id}") == run, "Qualification changed during evidence preparation")
    return {
        "schema": "eldritch.managed-requalification/1", **identity,
        "evidence_kind": "independent-full-qualification-and-identical-managed-zip",
        "run_id": producer_id, "run_attempt": producer_attempt, "ref": "refs/heads/" + BRANCH,
        "qualified_run_id": run_id, "qualified_run_attempt": attempt, "qualified_source_sha": sha,
        "original_provenance": {"asset": "provenance.json", "asset_id": original_asset["id"],
                                "sha256": digest(original_bytes)},
        "qualified_artifact": {"id": artifact["id"], "name": ARTIFACT, "sha256": digest(archive_bytes)},
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--qualified-run-id", type=int, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    require(os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("GITHUB_REPOSITORY") == REPOSITORY
            and os.environ.get("GITHUB_EVENT_NAME") == "workflow_dispatch"
            and os.environ.get("GITHUB_REF") == "refs/heads/" + BRANCH
            and os.environ.get("GITHUB_WORKFLOW_REF") == f"{REPOSITORY}/{WORKFLOW}@refs/heads/{BRANCH}",
            "Evidence must be generated by main CI, not a local invocation")
    record = prepare(args.qualified_run_id, int(os.environ["GITHUB_RUN_ID"]), int(os.environ["GITHUB_RUN_ATTEMPT"]))
    args.output.mkdir(parents=True, exist_ok=True)
    output = args.output / f"provenance-{record['run_id']}-{record['run_attempt']}.json"
    output.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n")
    print(f"{output.name} sha256:{digest(output.read_bytes())}")


if __name__ == "__main__":
    main()
