#!/usr/bin/env python3
"""M0.9 Manifest offsets.

Writes the spike decisions into the "onenote" block of templates/sfp-quarterly.json: titleOffsetPt, pageColor,
anchorPaddingPt and anchorUnderline. The values come from a findings JSON file (default tools/spike/scratch/
findings.json, the file the other scripts merge into), from --accept-proposals, or from --set KEY=VALUE on the
command line. Only those four values change: the block is patched in place so every other byte of the manifest keeps
its formatting; a key missing from the block is inserted before its closing brace at the block's own indent (2 spaces
per level, like the file). The result is parsed again and every value compared before the file is written.

findings.json schema (schemaVersion 1):

    {
      "schemaVersion": 1,
      "decisions": {                          the values the owner accepts; these are what gets written
        "titleOffsetPt": 36,                  number >= 0, points: y of the sheet outline on a titled page (M0.2)
        "pageColor": "#DED4C8",               "#RRGGBB" when OneNote accepted the hex PageSettings color, else "automatic" (M0.2)
        "anchorPaddingPt": { "x": 0, "y": 0 },  numbers, points: OneNote's internal padding inside a link outline (M0.4)
        "anchorUnderline": false              true when the underline on link anchors has to be accepted (M0.4)
      },
      "proposed": { ... same keys ... },      written by m0_2 and m0_4 from what they measured; --accept-proposals
                                              copies them into decisions for the keys the owner has not decided
      "cards": { "m0_1": { ... }, ... }       each script's full output (items, notes, proposed), informational
    }

Usage:

    python m0_9_apply.py [--findings scratch/findings.json] [--manifest ../../templates/sfp-quarterly.json]
                         [--accept-proposals] [--set titleOffsetPt=36] [--set pageColor=automatic]
                         [--allow-partial] [--dry-run]
    python m0_9_apply.py --selfcheck

Missing decisions stop the run (nothing is written) unless --allow-partial, which writes only the keys present.
--dry-run prints the diff and leaves the manifest alone. Runs anywhere with Python 3.11 or later; no pywin32.
"""
from __future__ import annotations

import argparse
import difflib
import json
import re
import shutil
import sys
import tempfile
from pathlib import Path

import spike_common as sc

KEYS = ("titleOffsetPt", "pageColor", "anchorPaddingPt", "anchorUnderline")
HEX_RE = re.compile(r"^#[0-9A-Fa-f]{6}$")
VALUE_RE = r"(\{[^{}]*\}|\"[^\"]*\"|[-+0-9.eE]+|true|false|null)"


def is_number(value) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def normalize(key: str, value):
    """Validate one decision and return it in the form the manifest record expects."""
    if key == "titleOffsetPt":
        if not is_number(value) or value < 0:
            raise sc.SpikeError(f"titleOffsetPt must be a number >= 0, got {value!r}")
        return value
    if key == "pageColor":
        if not isinstance(value, str) or not (value == "automatic" or HEX_RE.match(value)):
            raise sc.SpikeError(f"pageColor must be '#RRGGBB' or 'automatic', got {value!r}")
        return value.upper() if value != "automatic" else value
    if key == "anchorPaddingPt":
        if not isinstance(value, dict) or not all(is_number(value.get(axis)) for axis in ("x", "y")):
            raise sc.SpikeError(f"anchorPaddingPt must be an object with numeric x and y, got {value!r}")
        return {"x": value["x"], "y": value["y"]}
    if key == "anchorUnderline":
        if not isinstance(value, bool):
            raise sc.SpikeError(f"anchorUnderline must be true or false, got {value!r}")
        return value
    raise sc.SpikeError(f"unknown decision key {key}")


def number_text(value) -> str:
    return str(int(value)) if float(value).is_integer() else repr(float(value))


def render(key: str, value) -> str:
    """The JSON text for a value, in the style the manifest already uses."""
    if key == "anchorPaddingPt":
        return f"{{ \"x\": {number_text(value['x'])}, \"y\": {number_text(value['y'])} }}"
    if key == "titleOffsetPt":
        return number_text(value)
    return json.dumps(value)


def find_block(text: str, name: str = "onenote") -> tuple[int, int]:
    """Indexes of the opening and closing braces of the top-level "name": { ... } block."""
    match = re.search(r"\"" + re.escape(name) + r"\"\s*:\s*\{", text)
    if not match:
        raise sc.SpikeError(f"no \"{name}\" block in the manifest")
    start = match.end() - 1
    depth = 0
    in_string = False
    escaped = False
    for index in range(start, len(text)):
        char = text[index]
        if in_string:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == "\"":
                in_string = False
            continue
        if char == "\"":
            in_string = True
        elif char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return start, index
    raise sc.SpikeError(f"the \"{name}\" block never closes")


def patch_block(text: str, decisions: dict) -> tuple[str, dict]:
    """Return the manifest text with the decisions applied inside the onenote block, and what changed."""
    changes = {}
    for key, raw in decisions.items():
        value = normalize(key, raw)
        start, end = find_block(text)
        block = text[start : end + 1]
        rendered = render(key, value)
        match = re.search(r"(\"" + re.escape(key) + r"\"\s*:\s*)" + VALUE_RE, block)
        if match:
            old = match.group(2)
            new_block = block[: match.start(2)] + rendered + block[match.end(2) :]
        else:
            old = None
            indent_match = re.search(r"\n([ \t]+)\"", block)
            indent = indent_match.group(1) if indent_match else "    "
            last = len(block) - 2
            while last > 0 and block[last] in " \t\r\n":
                last -= 1
            new_block = block[: last + 1] + f",\n{indent}\"{key}\": {rendered}" + block[last + 1 :]
        text = text[:start] + new_block + text[end + 1 :]
        changes[key] = {"old": old, "new": rendered}
    parsed = json.loads(text)["onenote"]
    for key, raw in decisions.items():
        expected = normalize(key, raw)
        actual = parsed.get(key)
        same = actual == expected if key != "anchorPaddingPt" else (
            isinstance(actual, dict) and all(float(actual.get(a, "nan")) == float(expected[a]) for a in ("x", "y"))
        )
        if key == "titleOffsetPt":
            same = is_number(actual) and float(actual) == float(expected)
        if not same:
            raise sc.SpikeError(f"after patching, onenote.{key} reads {actual!r} instead of {expected!r}")
    return text, changes


def parse_set(items: list[str]) -> dict:
    decisions = {}
    for item in items or []:
        if "=" not in item:
            raise sc.SpikeError(f"--set needs KEY=VALUE, got {item!r}")
        key, raw = item.split("=", 1)
        key = key.strip()
        if key not in KEYS:
            raise sc.SpikeError(f"--set key must be one of {', '.join(KEYS)}, got {key!r}")
        try:
            decisions[key] = json.loads(raw)
        except json.JSONDecodeError:
            decisions[key] = raw.strip()
    return decisions


def collect_decisions(findings: dict, accept_proposals: bool, overrides: dict) -> dict:
    decisions = dict(findings.get("decisions") or {})
    if accept_proposals:
        for key, value in (findings.get("proposed") or {}).items():
            decisions.setdefault(key, value)
    decisions.update(overrides)
    return {key: decisions[key] for key in KEYS if key in decisions}


def apply(manifest_path: Path, decisions: dict, dry_run: bool) -> dict:
    original = manifest_path.read_text(encoding="utf-8")
    patched, changes = patch_block(original, decisions)
    diff = difflib.unified_diff(original.splitlines(), patched.splitlines(), str(manifest_path), str(manifest_path) + " (patched)", lineterm="")
    diff_text = "\n".join(diff)
    print(diff_text or "no change: the manifest already holds these values")
    if not dry_run and patched != original:
        manifest_path.write_text(patched, encoding="utf-8")
        print(f"written: {manifest_path}")
    return changes


def selfcheck(manifest_path: Path) -> int:
    folder = Path(tempfile.mkdtemp(prefix="sfp-m0_9-"))
    copy = folder / "sfp-quarterly.json"
    shutil.copy(manifest_path, copy)
    text = copy.read_text(encoding="utf-8")
    # Remove one key so the insertion path is exercised too.
    without = re.sub(r",\s*\"anchorUnderline\"\s*:\s*(true|false)", "", text, count=1)
    copy.write_text(without, encoding="utf-8")
    decisions = {"titleOffsetPt": 42.5, "pageColor": "automatic", "anchorPaddingPt": {"x": 1.5, "y": -0.5}, "anchorUnderline": True}
    changes = apply(copy, decisions, dry_run=False)
    result = json.loads(copy.read_text(encoding="utf-8"))
    block = result["onenote"]
    start_before, end_before = find_block(without)
    start_after, end_after = find_block(copy.read_text(encoding="utf-8"))
    patched_text = copy.read_text(encoding="utf-8")
    checks = {
        "titleOffsetPt": block["titleOffsetPt"] == 42.5,
        "pageColor": block["pageColor"] == "automatic",
        "anchorPaddingPt": block["anchorPaddingPt"] == {"x": 1.5, "y": -0.5},
        "anchorUnderlineInserted": block["anchorUnderline"] is True and changes["anchorUnderline"]["old"] is None,
        "outsideBlockUntouched": without[:start_before] == patched_text[:start_after] and without[end_before:] == patched_text[end_after:],
        "otherKeysKept": block["schemaNamespace"] == sc.NS and "contentsOutline" in block,
        "setParsing": parse_set(["titleOffsetPt=36", "pageColor=automatic", "anchorUnderline=true"]) == {"titleOffsetPt": 36, "pageColor": "automatic", "anchorUnderline": True},
        "integerStaysInteger": re.search(r"\"titleOffsetPt\": 36\b", patch_block(text, {"titleOffsetPt": 36.0})[0]) is not None,
    }
    try:
        normalize("pageColor", "#12345")
        checks["rejectsBadColor"] = False
    except sc.SpikeError:
        checks["rejectsBadColor"] = True
    for name, ok in checks.items():
        print(f"{'ok  ' if ok else 'FAIL'} {name}")
    print(f"patched copy: {copy}")
    return 0 if all(checks.values()) else 1


def main() -> int:
    parser = argparse.ArgumentParser(description="M0.9: write the spike decisions into the manifest's onenote block.")
    parser.add_argument("--findings", default=str(sc.DEFAULT_SCRATCH / sc.FINDINGS_FILE), help="findings JSON (schema at the top of this file)")
    parser.add_argument("--manifest", default=str(sc.DEFAULT_MANIFEST), help="templates/sfp-quarterly.json")
    parser.add_argument("--accept-proposals", action="store_true", help="use the scripts' proposed values where no decision is set")
    parser.add_argument("--set", action="append", metavar="KEY=VALUE", help="override or supply a decision (JSON value), repeatable")
    parser.add_argument("--allow-partial", action="store_true", help="write the keys present even if some are missing")
    parser.add_argument("--dry-run", action="store_true", help="print the diff, write nothing")
    parser.add_argument("--selfcheck", action="store_true", help="patch a temp copy of the manifest and verify it")
    args = parser.parse_args()
    manifest_path = Path(args.manifest)
    if args.selfcheck:
        return selfcheck(manifest_path)

    findings_path = Path(args.findings)
    findings = sc.load_findings(findings_path) if findings_path.exists() else {"decisions": {}, "proposed": {}}
    if not findings_path.exists() and not args.set:
        raise sc.SpikeError(f"{findings_path} not found; run the spike scripts first or pass --set KEY=VALUE")
    decisions = collect_decisions(findings, args.accept_proposals, parse_set(args.set or []))
    missing = [key for key in KEYS if key not in decisions]
    if missing and not args.allow_partial:
        raise sc.SpikeError(
            "missing decisions: " + ", ".join(missing) + ". Set them under \"decisions\" in " + str(findings_path)
            + ", pass --set KEY=VALUE, use --accept-proposals, or --allow-partial to write the rest."
        )
    if not decisions:
        raise sc.SpikeError("nothing to apply")
    print("decisions: " + json.dumps(decisions))
    changes = apply(manifest_path, decisions, args.dry_run)
    for key, change in changes.items():
        print(f"  onenote.{key}: {change['old']} -> {change['new']}")
    if missing:
        print("not written (undecided): " + ", ".join(missing))
    return 0


if __name__ == "__main__":
    sys.exit(sc.run(main))
