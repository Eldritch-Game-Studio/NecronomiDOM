#!/usr/bin/env python3
"""Check authored Markdown content and link targets without a flaky external-network gate.

Inline/reference Markdown links and HTTP(S) autolinks are checked. Local target files
must exist; URL shape (not remote availability) is checked. Heading fragments are
not interpreted. Vendored upstream documentation is outside this authored-doc gate.
"""

import json
from pathlib import Path
import re
import subprocess
from urllib.parse import unquote, urlsplit


INLINE = re.compile(r"\]\(\s*(<[^>]+>|[^\s)]+)(?:\s+[\"'][^\n]*?[\"'])?\s*\)")
REFERENCE = re.compile(r"^\s{0,3}\[[^\]]+\]:\s*(<[^>]+>|\S+)", re.MULTILINE)
AUTOLINK = re.compile(r"<(https?://[^>]+)>")


def without_code(text):
    result = []
    fence = None
    for line in text.splitlines():
        marker = re.match(r"^\s*(`{3,}|~{3,})", line)
        if marker:
            token = marker.group(1)
            if fence is None:
                fence = token
            elif token[0] == fence[0] and len(token) >= len(fence):
                fence = None
            result.append("")
        elif fence is None:
            result.append(re.sub(r"(`+).*?\1", "", line))
    if fence is not None:
        raise ValueError("Unclosed fenced code block")
    return "\n".join(result)


def check_markdown(root, relative):
    path = root / relative
    text = path.read_text(encoding="utf-8")
    if not text.strip() or "\x00" in text:
        raise ValueError("Empty document or NUL byte")
    prose = without_code(text)
    targets = [match.group(1).strip("<>") for pattern in (INLINE, REFERENCE, AUTOLINK)
               for match in pattern.finditer(prose)]
    for target in targets:
        url = urlsplit(target)
        if url.scheme in {"http", "https"} or target.startswith("//"):
            if not url.netloc or any(character.isspace() for character in target):
                raise ValueError(f"Malformed external URL: {target}")
        elif url.scheme == "mailto":
            if "@" not in url.path:
                raise ValueError(f"Malformed email link: {target}")
        elif url.scheme:
            raise ValueError(f"Unsupported documentation link scheme: {target}")
        elif url.path:
            destination = (root / unquote(url.path).lstrip("/") if url.path.startswith("/")
                           else path.parent / unquote(url.path))
            if not destination.exists():
                raise ValueError(f"Missing local link target: {target}")
    return len(targets)


def check(root):
    tracked = subprocess.check_output(["git", "ls-files", "-z"], cwd=root)
    count = links = 0
    for raw in tracked.split(b"\0"):
        if not raw:
            continue
        relative = raw.decode("utf-8")
        path = root / relative
        if relative.endswith(".md") and "/vendor/" not in relative:
            try:
                links += check_markdown(root, relative)
            except ValueError as error:
                raise ValueError(f"{relative}: {error}") from error
            count += 1
        elif relative.startswith("docs/generated/") and relative.endswith(".json"):
            json.loads(path.read_text(encoding="utf-8"))
        elif relative.startswith("docs/images/") and path.stat().st_size == 0:
            raise ValueError(f"Empty documentation media: {relative}")
    print(f"Documentation: {count} Markdown files, {links} link targets; external URLs checked offline")


if __name__ == "__main__":
    check(Path(__file__).resolve().parents[1])
