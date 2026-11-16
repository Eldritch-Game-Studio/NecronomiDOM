#!/usr/bin/env python3
"""Check or update the declared public C# API of the core and runtime assemblies.

  python3 scripts/api-baseline.py check     # CI gate: fails on any undeclared addition or removal
  python3 scripts/api-baseline.py update    # declare additions in PublicAPI.Unshipped.txt, drop removed symbols
  python3 scripts/api-baseline.py ship      # at a release: move Unshipped declarations into Shipped

A removed or changed line in a PublicAPI.Shipped.txt is a breaking change; it must be in the same change as the code.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parent.parent
PROJECT = ROOT / "api/runtime/NecronomiDom.Runtime.Api.csproj"
# Diagnostics name the compiling project; the core assembly reports against api/core, the runtime against api/runtime.
BASELINES = {"NecronomiDom.csproj": ROOT / "api/core", "NecronomiDom.Runtime.Api.csproj": ROOT / "api/runtime"}
NULLABLE = "#nullable enable"
DIAGNOSTIC = re.compile(
    r": (?:error|warning) (?P<id>RS0016|RS0017): Symbol '(?P<symbol>.*)' is "
    r"(?:not part of the declared public API|part of the declared (?:public )?API, but).*\[(?P<project>[^\]]+)\]\s*$")


def build() -> tuple[int, str]:
    # One invocation checks both: the global property flows to the core ProjectReference.
    command = ["dotnet", "build", str(PROJECT), "-nologo", "--disable-build-servers", "-m:1", "-nr:false",
               "-p:UseSharedCompilation=false", "-p:NecronomiDomApiCheck=true", "-clp:NoSummary"]
    result = subprocess.run(command, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    return result.returncode, result.stdout


def read(path: Path) -> list[str]:
    if not path.exists():
        return []
    return [line for line in path.read_text(encoding="utf-8").splitlines() if line.strip() and line != NULLABLE]


def write(path: Path, entries: set[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join([NULLABLE, *sorted(entries, key=str.casefold)]) + "\n", encoding="utf-8")


def diagnostics(output: str) -> dict[Path, dict[str, set[str]]]:
    found: dict[Path, dict[str, set[str]]] = {}
    for line in output.splitlines():
        match = DIAGNOSTIC.search(line)
        if not match:
            continue
        baseline = BASELINES.get(Path(match["project"]).name)
        if baseline is None:
            raise SystemExit(f"API diagnostic from an unexpected project: {line}")
        found.setdefault(baseline, {"RS0016": set(), "RS0017": set()})[match["id"]].add(match["symbol"])
    return found


def check() -> int:
    status, output = build()
    if status == 0:
        print("Public API matches api/core and api/runtime.")
        return 0
    print(output)
    found = diagnostics(output)
    for baseline, kinds in sorted(found.items()):
        for symbol in sorted(kinds["RS0017"]):
            print(f"BREAKING {baseline.relative_to(ROOT)}: removed or changed {symbol}")
        for symbol in sorted(kinds["RS0016"]):
            print(f"UNDECLARED {baseline.relative_to(ROOT)}: added {symbol}")
    if found:
        print("Declare intentional API changes in the same change: python3 scripts/api-baseline.py update")
    return 1


def update() -> int:
    for baseline in BASELINES.values():
        for name in ("PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt"):
            if not (baseline / name).exists():
                write(baseline / name, set())
    # Declaring a type can reveal diagnostics for its members; converge in a few builds.
    for _ in range(5):
        status, output = build()
        found = diagnostics(output)
        if not found:
            break
        for baseline, kinds in found.items():
            for name, add in (("PublicAPI.Shipped.txt", False), ("PublicAPI.Unshipped.txt", True)):
                entries = set(read(baseline / name)) - kinds["RS0017"]
                if add:
                    entries |= kinds["RS0016"]
                write(baseline / name, entries)
    else:
        print(output)
        raise SystemExit("API declarations did not converge")
    if status != 0:
        print(output)
        raise SystemExit("Build failed for a reason other than the API baseline")
    print("Updated api/core and api/runtime; review the PublicAPI.*.txt diff.")
    return 0


def ship() -> int:
    for baseline in BASELINES.values():
        write(baseline / "PublicAPI.Shipped.txt",
              set(read(baseline / "PublicAPI.Shipped.txt")) | set(read(baseline / "PublicAPI.Unshipped.txt")))
        write(baseline / "PublicAPI.Unshipped.txt", set())
    return check()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=("check", "update", "ship"))
    return {"check": check, "update": update, "ship": ship}[parser.parse_args().command]()


if __name__ == "__main__":
    sys.exit(main())
