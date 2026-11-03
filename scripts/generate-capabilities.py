#!/usr/bin/env python3
"""Generate renderer reference and editor custom data from runtime admission definitions.

No engine, .NET build, third-party Python packages, network, or reflection required.
Run without arguments to regenerate, or --check for a read-only CI freshness gate.
"""

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

CORE = Path("addons/necronomidom/core")
MANIFEST = CORE / "Css/Data/runtime-capabilities.json"
SYNTAX_PROFILE = CORE / "Css/Data/supported-properties.json"
OUTPUTS = (
    Path("docs/capabilities.md"),
    Path("docs/generated/capabilities.json"),
    Path("docs/generated/necronomidom.css-data.json"),
    Path("docs/generated/necronomidom.html-data.json"),
)


def json_text(value):
    return json.dumps(value, ensure_ascii=False, indent=2) + "\n"


def load_definitions(root):
    manifest = json.loads((root / MANIFEST).read_text(encoding="utf-8"))
    syntax = json.loads((root / SYNTAX_PROFILE).read_text(encoding="utf-8"))
    if manifest["schemaVersion"] != 1:
        raise ValueError("Unknown runtime capability schema")
    seen = set()
    syntax_names = set(syntax["properties"])
    for group in manifest["groups"]:
        for name in group["properties"]:
            if name in seen:
                raise ValueError("Duplicate renderer property: " + name)
            if name not in syntax_names:
                raise ValueError("Renderer property absent from syntax profile: " + name)
            seen.add(name)
        for source in group["sources"]:
            if not (root / CORE / source).is_file():
                raise ValueError("Missing constraint implementation: " + source)
    for name in manifest["keywords"]:
        if name not in seen:
            raise ValueError("Keyword restriction on unadmitted property: " + name)
    for key in ("elements", "inlineElements", "pseudoClasses", "stylesheetRuleTypes"):
        if len(set(manifest[key])) != len(manifest[key]):
            raise ValueError("Duplicate capability: " + key)
    if not set(manifest["inlineElements"]) <= set(manifest["elements"]):
        raise ValueError("Inline elements must also be admitted elements")
    return manifest, syntax


def native_branch(text):
    """Select the explicit NativeAOT compilation branch; reject unknown conditions."""
    active = True
    stack = []
    result = []
    for line in text.splitlines():
        directive = line.strip()
        if directive.startswith("#if "):
            symbol = directive[4:].strip()
            if symbol not in ("NECRONOMIDOM_GDEXTENSION", "!NECRONOMIDOM_GDEXTENSION"):
                raise ValueError("Unsupported API reference preprocessor condition: " + symbol)
            condition = symbol == "NECRONOMIDOM_GDEXTENSION"
            stack.append((active, condition))
            active = active and condition
        elif directive == "#else":
            parent, condition = stack[-1]
            active = parent and not condition
        elif directive == "#endif":
            active, _ = stack.pop()
        elif directive.startswith("#elif "):
            raise ValueError("Unsupported API reference preprocessor directive: " + directive)
        elif active:
            result.append(line)
        else:
            result.append("")
    if stack:
        raise ValueError("Unclosed API reference preprocessor conditional")
    return "\n".join(result)


def native_api(root):
    """Read explicit binding declarations, not arbitrary public members or event strings.

    This deliberately bounded declaration reader fails closed when a binding uses a
    new attribute/signature shape. It is not a general C# parser or binding generator.
    """
    paths = sorted(
        list((root / "addons/necronomidom/runtime").glob("DocumentHost*.cs"))
        + list((root / "native/src").glob("DocumentHost*.cs"))
    )
    pattern = re.compile(
        r'^\s*\[(BindMethod|Signal)\(\s*Name\s*=\s*"([^"]+)"\s*\)\]\s*'
        r'public\s+(?:(delegate)\s+)?([\w.<>?]+)\s+(\w+)\s*\(([^)]*)\)',
        re.MULTILINE,
    )
    methods, signals = [], []
    inputs = []
    for path in paths:
        text = native_branch(path.read_text(encoding="utf-8"))
        declarations = list(pattern.finditer(text))
        annotations = re.findall(r'^\s*\[(?:BindMethod|Signal)\b', text, re.MULTILINE)
        if len(declarations) != len(annotations):
            raise ValueError("Unrecognized native binding declaration in " + path.relative_to(root).as_posix())
        if not declarations:
            continue
        relative = path.relative_to(root).as_posix()
        inputs.append(path.relative_to(root))
        for match in declarations:
            kind, name, delegate, result, managed, arguments = match.groups()
            if (kind == "Signal") != bool(delegate):
                raise ValueError("Native binding declaration kind mismatch: " + name)
            entry = {
                "name": name, "managedName": managed, "returnType": result,
                "parameters": " ".join(arguments.split()), "source": relative,
            }
            (signals if delegate else methods).append(entry)
    for entries in (methods, signals):
        names = [entry["name"] for entry in entries]
        if len(set(names)) != len(names):
            raise ValueError("Duplicate native API binding name")
        entries.sort(key=lambda entry: entry["name"])
    return {"methods": methods, "signals": signals}, inputs


def render_outputs(root):
    manifest, syntax = load_definitions(root)
    properties = {}
    for group in manifest["groups"]:
        for name in group["properties"]:
            properties[name] = {
                "name": name,
                "syntaxAccepted": True,
                "stage": group["stage"],
                "constraints": group["constraints"],
                "allowedKeywords": manifest["keywords"].get(name, []),
                "sources": group["sources"],
            }
    syntax_only = sorted(set(syntax["properties"]) - properties.keys())
    api, api_inputs = native_api(root)
    digests = {
        path.as_posix(): hashlib.sha256((root / path).read_bytes()).hexdigest()
        for path in (MANIFEST, SYNTAX_PROFILE, *api_inputs)
    }
    inventory = {
        "schemaVersion": 1,
        "generatedBy": "scripts/generate-capabilities.py",
        "sourceSha256": digests,
        "description": manifest["description"],
        "elements": manifest["elements"],
        "inlineElements": manifest["inlineElements"],
        "properties": [properties[name] for name in sorted(properties)],
        "pseudoClasses": manifest["pseudoClasses"],
        "stylesheetRuleTypes": manifest["stylesheetRuleTypes"],
        "conditional": manifest["conditional"],
        "rejections": manifest["rejections"],
        "syntaxOnlyProperties": syntax_only,
        "aliases": dict(sorted(syntax["aliases"].items())),
        "apiScope": manifest["apiScope"],
        "nativeApi": api,
    }
    css = {"version": 1.1, "properties": [], "pseudoClasses": []}
    for name in sorted(properties):
        prop = properties[name]
        # Inactive admission exceptions must not look like implemented visual effects.
        if prop["stage"].startswith("admission only"):
            continue
        description = prop["stage"] + ". Bounded renderer admission, not full CSS grammar support. " + prop["constraints"]
        entry = {"name": name, "description": description}
        if prop["allowedKeywords"]:
            entry["values"] = [{"name": value} for value in prop["allowedKeywords"]]
        css["properties"].append(entry)
    css["pseudoClasses"] = [
        {"name": ":" + name, "description": "Admitted by the bounded runtime selector validator."}
        for name in sorted(manifest["pseudoClasses"])
    ]
    html = {
        "version": 1.1,
        "tags": [
            {"name": name, "description": "Admitted rendered element. Bounded layout/paint rules still apply; see docs/capabilities.md."}
            for name in sorted(manifest["elements"])
        ],
    }
    lines = [
        "# Renderable capability reference",
        "",
        "<!-- Generated by scripts/generate-capabilities.py. Do not edit this file. -->",
        "",
        "## Regeneration and CI",
        "",
        "From the repository root, with Python 3.9+ (standard library only):",
        "",
        "```sh",
        "python3 scripts/generate-capabilities.py",
        "python3 scripts/generate-capabilities.py --check",
        "python3 -m unittest discover -s scripts -p 'test_generate_capabilities.py'",
        "```",
        "",
        "The check command never writes; missing or byte-different output exits 1. Invalid source definitions exit 2. Output is UTF-8/LF, ordered and timestamp-free. No engine/build/network required.",
        "",
        "## What support means",
        "",
        manifest["description"],
        "",
        "- **Syntax acceptance**: the DOM/CSSOM can represent much more than the renderer. The parser property-name profile is `addons/necronomidom/core/Css/Data/supported-properties.json`, not a renderer whitelist. Parser acceptance does not establish layout or paint support.",
        "- **Layout**: admitted boxes/values must also satisfy containing-block, text, flex and positioning solvers.",
        "- **Paint/input**: admitted computed values must have a supported paint/hit-test/resource path. Admission-only colors explicitly do not enable their associated effects.",
        "- The machine-readable runtime manifest is `addons/necronomidom/core/Css/Data/runtime-capabilities.json`. `RuntimeCapabilities`, `FlowLayout` and `RuntimeStyleProfile` consume it directly. Keyword restrictions below are executable shared definitions, not copied prose lists.",
        "- Context-dependent restrictions remain in the linked solvers; the notes summarize their bounded scope rather than duplicating their value grammars. This is not full CSS/WPT layout conformance.",
        "",
        "## HTML elements",
        "",
        "Rendered element admission: " + ", ".join("`" + name + "`" for name in sorted(manifest["elements"])) + ".",
        "",
        "Inline fragment admission: " + ", ".join("`" + name + "`" for name in sorted(manifest["inlineElements"])) + ". Inline edges and independent compositing are rejected.",
        "",
        "`head`, stylesheet metadata and other non-rendered DOM nodes are not additional rendered element capabilities. `display:none` skipping an element is not rendering support. Input admission additionally uses `HTMLInputElement.IsTextEditable`; attached shadow roots are rejected.",
        "",
        "## CSS longhand admission and constraints",
        "",
        "These are computed-property admission definitions. Shorthands/aliases are parsed and expanded before validation; a shorthand is renderable only when its resulting longhands and values satisfy this profile. No complete shorthand grammar is promised by this list.",
        "",
    ]
    for group in manifest["groups"]:
        lines.extend([
            "### " + group["name"], "",
            "**Stage:** " + group["stage"] + ".", "",
            ", ".join("`" + name + "`" for name in sorted(group["properties"])) + ".", "",
            group["constraints"], "",
        ])
        for name in sorted(group["properties"]):
            if name in manifest["keywords"]:
                lines.append("- `" + name + "`: " + ", ".join("`" + value + "`" for value in manifest["keywords"][name]) + ".")
        if any(name in manifest["keywords"] for name in group["properties"]):
            lines.append("")
        links = []
        for source in group["sources"]:
            target = (CORE / source).as_posix()
            links.append("[" + source + "](../" + target + ")")
        lines.extend(["Implementation: " + ", ".join(links) + ".", ""])
    lines.extend(["## Conditional and inert acceptance", ""])
    for item in manifest["conditional"]:
        lines.extend(["- **" + item["name"] + ":** " + item["constraint"]])
    lines.extend(["", "## Stylesheets and selectors", ""])
    lines.extend([
        "Admitted rule types: " + ", ".join("`" + name + "`" for name in sorted(manifest["stylesheetRuleTypes"])) + ".",
        "",
        "Admitted pseudo-classes (including recursive functional/nth arguments): " + ", ".join("`:" + name + "`" for name in sorted(manifest["pseudoClasses"])) + ".",
        "",
        "Normal type/class/ID/attribute selectors and combinators use the existing selector parser/matcher. Pseudo-elements are rejected.",
        "", "## Explicitly rejected scope", "",
    ])
    lines.extend("- " + item for item in manifest["rejections"])
    lines.extend(["", "## APIs and events", "", manifest["apiScope"], ""])
    lines.extend([
        "### Native/GDScript bound methods", "",
        "Generated from explicit `BindMethod(Name=...)` declarations in the NativeAOT compilation branch. Signatures show managed parameter/return types; the native binding generator remains responsible for Variant marshalling. This inventory is not a complete C# API reference.", "",
        "| GDScript method | Managed declaration | Source |",
        "| --- | --- | --- |",
    ])
    for entry in api["methods"]:
        signature = entry["returnType"] + " " + entry["managedName"] + "(" + entry["parameters"] + ")"
        lines.append("| `" + entry["name"] + "` | `" + signature + "` | [" + entry["source"] + "](../" + entry["source"] + ") |")
    lines.extend(["", "### Native/GDScript signals", "", "| Signal | Parameters | Source |", "| --- | --- | --- |"])
    for entry in api["signals"]:
        lines.append("| `" + entry["name"] + "` | `" + entry["parameters"] + "` | [" + entry["source"] + "](../" + entry["source"] + ") |")
    lines.extend([
        "",
        "Signal declarations do not imply every DOM event is automatically emitted by the host. `listen(type)` forwards requested DOM events via `dom_event`; arbitrary dispatchable event names are not a finite built-in input-event inventory. `clicked`, `cancel` and developer-tool signals follow their source contracts.",
        "",
        "The reference reader accepts the repository's explicit named binding declarations and NativeAOT `#if`/`#else` form. New declaration/conditional shapes fail generation rather than silently losing API entries.",
        "", "## Editor completion data", "",
    ])
    lines.extend([
        "Generated language-service custom data uses the existing HTML/CSS custom-data JSON formats, without an editor extension. Configure compatible clients (for example VS Code):",
        "",
        "```json",
        '{',
        '  "css.customData": ["./docs/generated/necronomidom.css-data.json"],',
        '  "html.customData": ["./docs/generated/necronomidom.html-data.json"]',
        '}',
        "```",
        "",
        "These files add bounded longhand/tag/pseudo-class descriptions; they do not disable an editor's built-in browser suggestions or implement a validator. Keyword completions are emitted only for shared exact-keyword restrictions. Shorthands, attributes, conditional-only properties and admission-only inactive colors are intentionally not suggested. Full machine-readable scope (including exceptions) is in `docs/generated/capabilities.json`.",
        "", "## Syntax-profile names outside unconditional renderer admission", "",
        "This list is **not renderable support** and is not a universal rejection list: it includes shorthands that may expand to supported values, conditional transitions and inert initial-value exceptions described above. Non-initial unsupported resulting longhands are rejected.", "",
        ", ".join("`" + name + "`" for name in syntax_only) + ".", "",
        "## Source fingerprints", "",
    ])
    lines.extend("- `" + path + "`: `" + digest + "`" for path, digest in digests.items())
    lines.append("")
    return dict(zip(OUTPUTS, ("\n".join(lines), json_text(inventory), json_text(css), json_text(html))))


def generate(root, check=False):
    outputs = render_outputs(root)
    stale = []
    for path, content in outputs.items():
        target = root / path
        encoded = content.encode("utf-8")
        if check:
            if not target.is_file() or target.read_bytes() != encoded:
                stale.append(path.as_posix())
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(encoded)
    return stale


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="fail on missing/stale output, without writing")
    args = parser.parse_args(argv)
    root = Path(__file__).resolve().parents[1]
    try:
        stale = generate(root, args.check)
    except (OSError, ValueError, KeyError, TypeError) as error:
        print("Capability generation failed: " + str(error), file=sys.stderr)
        return 2
    if stale:
        print("Stale capability output: " + ", ".join(stale), file=sys.stderr)
        print("Run: python3 scripts/generate-capabilities.py", file=sys.stderr)
        return 1
    print("Capability reference is current." if args.check else "Generated capability reference and editor data.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
