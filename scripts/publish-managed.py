#!/usr/bin/env python3
"""Publish a CI-built managed-host package as release managed-<sha12> and notify direct consumers.

  source      prove the revision is on main; a backfill also needs that revision's own passing main CI run
  provenance  verify the package built by scripts/package-managed.py and write provenance.json
  publish     create (or idempotently confirm) the release with the ZIP and provenance.json
  dispatch    send repository_dispatch upstream-package-published to a consumer repository

Only CI writes these releases; consumers install a ZIP whose sha256 matches release, provenance and dispatch.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import zipfile

PACKAGE = "necronomidom"
REPOSITORY = "Eldritch-Game-Studio/NecronomiDOM"
BRANCH = "main"
SCHEMA = "eldritch.managed-provenance/1"
ADDON_SCHEMA = "eldritch.managed-addon/1"
EVENT = "upstream-package-published"
CI_WORKFLOW = "ci.yml"
SHA = re.compile(r"[0-9a-f]{40}")


def run(*command: str, input: str | None = None, env: dict[str, str] | None = None) -> str:
    return subprocess.run(command, check=True, text=True, stdout=subprocess.PIPE, input=input, env=env).stdout


def gh_json(*args: str) -> object:
    return json.loads(run("gh", *args) or "null")


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def release_tag(sha: str) -> str:
    return f"managed-{sha[:12]}"


def zip_name(sha: str) -> str:
    return f"{PACKAGE}-managed-{sha}.zip"


def require_sha(sha: str) -> str:
    if not SHA.fullmatch(sha):
        raise SystemExit(f"Not an exact 40-character commit: {sha!r}")
    return sha


def source(sha: str, ref: str, backfill: bool) -> int:
    """Return the run that qualified the revision: this run, or for a backfill the revision's own main CI run."""
    require_sha(sha)
    if not (ref == f"refs/heads/{BRANCH}" or ref.startswith("refs/tags/v")):
        raise SystemExit(f"Managed packages are published only from {BRANCH} or v* tags, not {ref}")
    if subprocess.run(["git", "merge-base", "--is-ancestor", sha, f"origin/{BRANCH}"]).returncode != 0:
        raise SystemExit(f"{sha} is not on origin/{BRANCH}")
    if not backfill:
        return int(os.environ["GITHUB_RUN_ID"])
    runs = gh_json("api", f"repos/{REPOSITORY}/actions/workflows/{CI_WORKFLOW}/runs"
                   f"?head_sha={sha}&event=push&branch={BRANCH}&status=success&per_page=100")
    qualified = sorted(item["id"] for item in runs["workflow_runs"]
                       if item["head_sha"] == sha and item["head_branch"] == BRANCH and item["conclusion"] == "success")
    if not qualified:
        raise SystemExit(f"{sha} has no successful {BRANCH} push run of {CI_WORKFLOW}; it cannot be backfilled")
    return qualified[-1]


def verify_package(path: Path, sha: str) -> None:
    """The ZIP must be package-managed.py output for exactly this revision, every file matching its manifest."""
    if path.name != zip_name(sha):
        raise SystemExit(f"Unexpected package name {path.name}")
    with zipfile.ZipFile(path) as package:
        metadata = json.loads(package.read("addon-package.json"))
        if (metadata.get("schema"), metadata.get("addon"), metadata.get("revision"), metadata.get("profile")) != (
                ADDON_SCHEMA, PACKAGE, sha, "managed-host"):
            raise SystemExit("addon-package.json does not identify this managed-host revision")
        prefix = f"addons/{PACKAGE}/"
        names = {name for name in package.namelist() if name != "addon-package.json"}
        if names != {prefix + name for name in metadata["files"]}:
            raise SystemExit("Package members differ from addon-package.json")
        for name, digest in metadata["files"].items():
            if hashlib.sha256(package.read(prefix + name)).hexdigest() != digest:
                raise SystemExit(f"Package member hash differs: {name}")


def provenance(path: Path, sha: str, ref: str, qualified_run_id: int) -> dict[str, object]:
    verify_package(path, sha)
    return {
        "schema": SCHEMA,
        "package": PACKAGE,
        "repository": REPOSITORY,
        "sha": sha,
        "ref": ref,
        "branch": BRANCH,
        "workflow": os.environ.get("GITHUB_WORKFLOW_REF", ""),
        "run_id": int(os.environ["GITHUB_RUN_ID"]),
        "run_attempt": int(os.environ["GITHUB_RUN_ATTEMPT"]),
        "qualified_run_id": qualified_run_id,
        "release_tag": release_tag(sha),
        "zip": path.name,
        "zip_sha256": sha256(path),
        "upstream": {},
    }


def load(provenance_path: Path, package_path: Path) -> dict[str, object]:
    record = json.loads(provenance_path.read_text(encoding="utf-8"))
    sha = require_sha(str(record.get("sha")))
    if (record.get("schema"), record.get("package"), record.get("repository"), record.get("release_tag"),
            record.get("zip")) != (SCHEMA, PACKAGE, REPOSITORY, release_tag(sha), zip_name(sha)):
        raise SystemExit("provenance.json does not describe this package")
    if record.get("zip_sha256") != sha256(package_path):
        raise SystemExit("Package bytes differ from provenance.json")
    verify_package(package_path, sha)
    return record


def view(tag: str) -> dict[str, object] | None:
    result = subprocess.run(["gh", "release", "view", tag, "-R", REPOSITORY, "--json", "isPrerelease"],
                            text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode == 0:
        return json.loads(result.stdout)
    if "release not found" in result.stderr:
        return None
    raise SystemExit(result.stderr)


def publish(package_path: Path, provenance_path: Path) -> str:
    record = load(provenance_path, package_path)
    tag, sha = str(record["release_tag"]), str(record["sha"])
    prerelease = not str(record["ref"]).startswith("refs/tags/v")
    existing = view(tag)
    if existing is None:
        notes = (f"CI-built managed-host source package of {REPOSITORY}@{sha}.\n\n"
                 f"- ZIP sha256: `{record['zip_sha256']}`\n- Source ref: `{record['ref']}`\n"
                 f"- Built by run {record['run_id']}, qualified by run {record['qualified_run_id']}\n")
        created = subprocess.run(
            ["gh", "release", "create", tag, str(package_path), f"{provenance_path}#provenance.json",
             "-R", REPOSITORY, "--target", sha, "--title", f"NecronomiDOM managed-host {sha[:12]}",
             "--notes", notes, "--latest=false", *(["--prerelease"] if prerelease else [])])
        if created.returncode == 0:
            print(f"Published {tag}")
            return tag
        # A concurrent main and tag run of one commit may both create; the loser confirms the winner's bytes.
        existing = view(tag)
        if existing is None:
            raise SystemExit(f"Could not create {tag}")
    # The package is deterministic: a rerun or a tag of an already published main commit must reproduce its bytes.
    with tempfile.TemporaryDirectory() as directory:
        run("gh", "release", "download", tag, "-R", REPOSITORY, "-p", "provenance.json", "-D", directory)
        published = json.loads((Path(directory) / "provenance.json").read_text(encoding="utf-8"))
    if (published.get("sha"), published.get("zip_sha256")) != (sha, record["zip_sha256"]):
        raise SystemExit(f"{tag} already holds different package bytes; refusing to replace a published release")
    if existing["isPrerelease"] and not prerelease:
        run("gh", "release", "upload", tag, f"{provenance_path}#provenance.json", "-R", REPOSITORY, "--clobber")
        run("gh", "release", "edit", tag, "-R", REPOSITORY, "--prerelease=false", "--latest=false")
        print(f"Promoted {tag} to the {record['ref']} release")
    else:
        print(f"{tag} already published with identical bytes")
    return tag


def dispatch(package_path: Path, provenance_path: Path, consumer: str) -> dict[str, object]:
    record = load(provenance_path, package_path)
    request = {"event_type": EVENT, "client_payload": {
        "package": PACKAGE, "sha": record["sha"], "release_tag": record["release_tag"],
        "zip_sha256": record["zip_sha256"]}}
    # Cross-repository dispatch uses the runner host's own gh login, never a workflow token.
    env = {key: value for key, value in os.environ.items() if key not in {"GH_TOKEN", "GITHUB_TOKEN"}}
    run("gh", "api", "--method", "POST", f"repos/{consumer}/dispatches", "--input", "-",
        input=json.dumps(request), env=env)
    print(f"Dispatched {EVENT} {record['release_tag']} to {consumer}")
    return request


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    command = commands.add_parser("source")
    command.add_argument("--sha", required=True)
    command.add_argument("--ref", required=True)
    command.add_argument("--backfill", action="store_true")
    command = commands.add_parser("provenance")
    command.add_argument("--zip", type=Path, required=True)
    command.add_argument("--sha", required=True)
    command.add_argument("--ref", required=True)
    command.add_argument("--qualified-run-id", type=int, required=True)
    command.add_argument("--output", type=Path, required=True)
    for name in ("publish", "dispatch"):
        command = commands.add_parser(name)
        command.add_argument("--zip", type=Path, required=True)
        command.add_argument("--provenance", type=Path, required=True)
        if name == "dispatch":
            command.add_argument("--consumer", required=True)
    args = parser.parse_args()
    if args.command == "source":
        print(source(args.sha, args.ref, args.backfill))
    elif args.command == "provenance":
        record = provenance(args.zip, require_sha(args.sha), args.ref, args.qualified_run_id)
        args.output.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        print(json.dumps(record, indent=2, sort_keys=True))
    elif args.command == "publish":
        publish(args.zip, args.provenance)
    else:
        dispatch(args.zip, args.provenance, args.consumer)
    return 0


if __name__ == "__main__":
    sys.exit(main())
