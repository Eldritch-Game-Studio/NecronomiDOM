#!/usr/bin/env python3
"""Classify unprivileged CI events; unknown paths always receive code checks."""

import argparse
import json
import os
from pathlib import PurePosixPath, Path
import subprocess


MEDIA = {".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".mp4", ".webm"}
BUILD_SUFFIXES = {".csproj", ".props", ".targets", ".sln", ".slnx", ".lock", ".gdextension"}


def path_kind(path):
    name = PurePosixPath(path)
    if name.suffix.lower() == ".md" or path == "LICENSE":
        return "docs"
    if path.startswith("docs/images/") and name.suffix.lower() in MEDIA:
        return "docs"
    if (path.startswith(("native/", ".github/", "scripts/", "addons/necronomidom/runtime/"))
            or name.suffix.lower() in BUILD_SUFFIXES
            or name.name in {"global.json", "NuGet.Config", "nuget.config", "packages.lock.json"}
            or name.name.startswith(("Dockerfile", "Makefile"))
            or name.suffix.lower() in {".sh", ".ps1", ".cmd", ".bat"}):
        return "full"
    return "fast"


def policy(event_name, ref, paths, partial=False):
    if event_name == "workflow_dispatch":
        mode = "partial" if partial else "full"
    elif ref.startswith("refs/tags/v"):
        mode = "full"
    elif paths and all(path_kind(path) == "docs" for path in paths):
        mode = "docs"
    elif event_name == "pull_request" and any(path_kind(path) == "full" for path in paths):
        mode = "full"
    else:
        # A code push (even native/tooling changes) gets fast feedback; full qualification is a PR/tag gate.
        mode = "fast"
    return {"mode": mode, "code": mode != "docs", "full": mode == "full",
            "package": mode in {"full", "partial"}}


def git(*args, cwd=None, input=None):
    return subprocess.check_output(["git", *args], cwd=cwd, input=input)


def changed_paths(event_name, event, cwd=None):
    if event_name == "workflow_dispatch" or event.get("ref", "").startswith("refs/tags/"):
        return []
    if event_name == "pull_request":
        pr = event["pull_request"]
        head = pr["head"]["sha"]
        base = git("merge-base", pr["base"]["sha"], head, cwd=cwd).decode().strip()
    else:
        head = event["after"]
        base = event["before"]
        if not base.strip("0"):
            base = git("mktree", cwd=cwd, input=b"").decode().strip()
    # No rename detection: both the removed and added names participate in policy.
    # NUL delimiters preserve spaces/newlines; a deleted native file cannot masquerade as new documentation.
    data = git("diff", "--no-renames", "--name-only", "-z", base, head, "--", cwd=cwd)
    return [os.fsdecode(path) for path in data.split(b"\0") if path]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--event", default=os.environ.get("GITHUB_EVENT_PATH"), required=False)
    args = parser.parse_args()
    event = json.loads(Path(args.event).read_text(encoding="utf-8"))
    event_name = os.environ["GITHUB_EVENT_NAME"]
    partial = event.get("inputs", {}).get("partial", False) in (True, "true")
    paths = changed_paths(event_name, event)
    result = policy(event_name, os.environ["GITHUB_REF"], paths, partial)
    print(json.dumps({**result, "paths": paths}, indent=2))
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        for key, value in result.items():
            output.write(f"{key}={str(value).lower()}\n")
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
        summary.write(f"CI policy: **{result['mode']}**; compared {len(paths)} changed paths.\n")


if __name__ == "__main__":
    main()
