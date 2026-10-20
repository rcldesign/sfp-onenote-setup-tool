#!/usr/bin/env python3
"""Smoke-run every spike script against the fake OneNote in tools/spike/fake, on any platform.

The fake imitates pywin32 and OneNote.Application closely enough to execute the scripts end to end: an in-memory
hierarchy, GetPageContent in the 2013 namespace with images returned as one:CallbackID, outlines pushed below the
title area (y < 36 becomes 36), one simulated RPC_E_CALL_REJECTED, an inked word with recognizedText, and switches
for the fallback paths. It proves the scripts run, not what OneNote does; the real answers come from the owner's PC.

    python smoke_fake.py            happy path: early binding, hex color accepted, merge-mode anchors, one rejected call
    python smoke_fake.py fallback   late binding, hex color rejected, --partial anchors, --direct hotspot, --binary ink

Output goes to a temp folder (printed at the end); the manifest is only read (m0_9 runs with --dry-run).
"""
from __future__ import annotations

import importlib
import json
import os
import pathlib
import sys
import tempfile

HERE = pathlib.Path(__file__).resolve().parent


def main() -> int:
    mode = sys.argv[1] if len(sys.argv) > 1 else "happy"
    sys.path.insert(0, str(HERE / "fake"))
    sys.path.insert(0, str(HERE))
    tmp = pathlib.Path(tempfile.mkdtemp(prefix=f"spike-smoke-{mode}-"))
    scratch, notebooks = tmp / "scratch", tmp / "notebooks"
    os.environ["FAKE_INK"] = "1"
    if mode == "happy":
        os.environ["FAKE_REJECT_ONCE"] = "1"
    else:
        os.environ["FAKE_REJECT_HEX"] = "1"
        os.environ["FAKE_NO_MAKEPY"] = "1"

    common = ["--no-prompt", "--scratch", str(scratch), "--notebooks-folder", str(notebooks)]
    plan = [
        ("m0_1_com", []),
        ("m0_2_background", []),
        ("m0_3_levels", []),
        ("m0_4_anchors", [] if mode == "happy" else ["--partial"]),
        ("m0_5_hotspot", [] if mode == "happy" else ["--direct"]),
        ("m0_6_metadata", ["--reopen"]),
        ("m0_8_ink", ["--sync"] if mode == "happy" else ["--binary"]),
    ]
    for name, extra in plan:
        print(f"\n#################### {name} {' '.join(extra)} ####################")
        sys.argv = [name + ".py"] + common + extra
        rc = importlib.import_module(name).main()
        if rc != 0:
            print(f"FAIL: {name} returned {rc}")
            return 1

    merged = json.loads((scratch / "findings.json").read_text(encoding="utf-8"))
    expected_cards = {"m0_1", "m0_2", "m0_3", "m0_4", "m0_5", "m0_6", "m0_8"}
    checks = {
        "allCardsMerged": expected_cards <= set(merged["cards"]),
        "pageColorProposed": merged["proposed"].get("pageColor") == ("#DED4C8" if mode == "happy" else "automatic"),
        "titleOffsetProposed": merged["proposed"].get("titleOffsetPt") == 36.0,
        "levelsHonored": merged["cards"]["m0_3"]["items"]["levelsAsRequested"] is True,
        "anchorsFound": all(v["readBack"] is not None for v in merged["cards"]["m0_4"]["items"]["anchors"].values()),
        "inkFound": merged["cards"]["m0_8"]["items"]["recognitionPresent"] is True,
        "metadataPersisted": all(merged["cards"]["m0_6"]["items"][k] is True for k in ("dateTimePersisted", "metaPersisted", "sectionColorPersisted")),
    }
    import spike_common  # noqa: E402  (after sys.path is set)

    sys.argv = ["m0_9_apply.py", "--findings", str(scratch / "findings.json"), "--manifest", str(spike_common.DEFAULT_MANIFEST), "--accept-proposals", "--allow-partial", "--dry-run"]
    checks["m0_9DryRun"] = importlib.import_module("m0_9_apply").main() == 0
    print()
    for name, ok in checks.items():
        print(f"{'ok  ' if ok else 'FAIL'} {name}")
    print(f"output: {scratch}")
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    sys.exit(main())
