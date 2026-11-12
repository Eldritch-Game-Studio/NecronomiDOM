#!/usr/bin/env python3
"""Download the exact package artifact, verifying complete bytes before extraction."""
import argparse
import hashlib
import http.client
import os
from pathlib import Path
import re
import shutil
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, new_url):
        return None


def artifact_url(repository, artifact_id, token):
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
        raise ValueError("Invalid repository identity")
    request = urllib.request.Request(
        f"https://api.github.com/repos/{repository}/actions/artifacts/{artifact_id}/zip",
        headers={"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json",
                 "X-GitHub-Api-Version": "2022-11-28"})
    try:
        with urllib.request.build_opener(NoRedirect).open(request, timeout=30):
            raise ValueError("Artifact endpoint did not return a download redirect")
    except urllib.error.HTTPError as error:
        if error.code != 302:
            raise RuntimeError(f"Artifact API returned HTTP {error.code}") from None
        url = error.headers.get("Location", "")
    parsed = urllib.parse.urlsplit(url)
    if parsed.scheme != "https" or not parsed.hostname or parsed.username or parsed.password:
        raise ValueError("Artifact download redirect is not authenticated HTTPS")
    return url


def transfer(url, destination, expected_digest):
    digest = hashlib.sha256()
    total = 0
    # The signed blob request intentionally receives no GitHub credentials.
    with urllib.request.urlopen(url, timeout=30) as response, destination.open("wb") as output:
        if response.status != 200 or urllib.parse.urlsplit(response.url).scheme != "https":
            raise ValueError("Artifact storage did not return HTTPS success")
        length = response.headers.get("Content-Length")
        for chunk in iter(lambda: response.read(1024 * 1024), b""):
            output.write(chunk)
            digest.update(chunk)
            total += len(chunk)
    if length is not None and total != int(length):
        raise ValueError("Artifact transfer ended before Content-Length")
    actual = "sha256:" + digest.hexdigest()
    if actual != expected_digest:
        raise ValueError(f"Artifact digest mismatch: received {total} bytes, {actual}")
    return total


def install(archive_path, filename, destination):
    if Path(filename).name != filename or not filename.endswith(".zip"):
        raise ValueError("Expected a package ZIP basename")
    with zipfile.ZipFile(archive_path) as outer:
        if outer.namelist() != [filename] or outer.testzip() is not None:
            raise ValueError("Artifact must contain exactly the expected intact package ZIP")
        with outer.open(filename) as source, tempfile.NamedTemporaryFile(
                dir=destination, prefix=".package-", delete=False) as output:
            temporary = Path(output.name)
            try:
                shutil.copyfileobj(source, output)
                output.close()
                with zipfile.ZipFile(temporary) as inner:
                    if inner.testzip() is not None:
                        raise ValueError("Package ZIP failed CRC validation")
                temporary.replace(destination / filename)
            finally:
                temporary.unlink(missing_ok=True)


def download(repository, artifact_id, expected_digest, filename, destination, token):
    if re.fullmatch(r"[0-9a-f]{64}", expected_digest):
        expected_digest = "sha256:" + expected_digest
    if not re.fullmatch(r"sha256:[0-9a-f]{64}", expected_digest):
        raise ValueError("A producer SHA256 artifact digest is required")
    destination.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="artifact-", dir=destination) as temporary:
        archive = Path(temporary) / "artifact.zip"
        for attempt in range(3):
            try:
                url = artifact_url(repository, artifact_id, token)
                size = transfer(url, archive, expected_digest)
                break
            except (OSError, ValueError, RuntimeError, http.client.HTTPException) as error:
                # Do not print signed URLs, bearer tokens, or urllib exception strings.
                print(f"Artifact transfer attempt {attempt + 1} failed ({type(error).__name__})", flush=True)
                if attempt == 2:
                    raise RuntimeError("Artifact transfer failed after three complete-download attempts") from None
                time.sleep(5)
        install(archive, filename, destination)
        print(f"Verified artifact {artifact_id}: {size} bytes, {expected_digest}; package {filename}")

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact-id", type=int, required=True)
    parser.add_argument("--digest", required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--destination", type=Path, default=Path("dist"))
    args = parser.parse_args()
    try:
        download(os.environ["GITHUB_REPOSITORY"], args.artifact_id, args.digest,
                 args.name, args.destination, os.environ["GH_TOKEN"])
    except (OSError, ValueError, RuntimeError, zipfile.BadZipFile) as error:
        parser.exit(1, f"Package download failed ({type(error).__name__}); no package accepted\n")


if __name__ == "__main__":
    main()
