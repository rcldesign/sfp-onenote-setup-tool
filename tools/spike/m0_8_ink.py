#!/usr/bin/env python3
"""M0.8 Ink read-back.

After the owner has inked a word on the scratch page on the iPad (m0_7_ipad.md) and the notebook has synced, reads
the page back on Windows with GetPageContent and prints every ink element (one:InkDrawing, one:InkParagraph,
one:InkWord and anything else whose name starts with Ink) with its attributes, quoting each one:InkWord element and
its recognizedText attribute, or recording that recognition text is absent. The full XML is saved as
scratch/m0_8-page.xml: it is the fixture the M4.1 page reader test parses, so keep it.

    python m0_8_ink.py [--page "M0.2 background y=36"] [--sync] [--binary]

--sync calls SyncHierarchy on the notebook and waits before reading; --binary asks for piBinaryData (ink strokes
included) instead of piBasic.
"""
from __future__ import annotations

import sys
import time
import xml.etree.ElementTree as ET

import spike_common as sc

DEFAULT_PAGE = "M0.2 background y=36"


def serialize(element: ET.Element) -> str:
    return sc.elide_data(ET.tostring(element, encoding="unicode"))


def main() -> int:
    parser = sc.base_parser("M0.8: read the inked page back and print every one:InkWord with its recognizedText.")
    parser.add_argument("--page", default=DEFAULT_PAGE, help="title of the page that was inked on the iPad")
    parser.add_argument("--sync", action="store_true", help="SyncHierarchy first and wait a few seconds")
    parser.add_argument("--binary", action="store_true", help="read with piBinaryData (ink stroke data included)")
    args = parser.parse_args()
    ctx = sc.Context(args)
    findings = ctx.findings("M0.8", "Ink read-back")

    on, notebook_id, _ = ctx.open_notebook()
    page = sc.find_page(on, notebook_id, args.page, ctx.ns)
    if page is None:
        raise sc.SpikeError(f"page '{args.page}' not found; pass --page with the title of the inked page")
    if args.sync:
        on.sync_hierarchy(notebook_id)
        time.sleep(5)

    xml = on.get_page_content(page["ID"], sc.PI_BINARY_DATA if args.binary else sc.PI_BASIC)
    fixture = ctx.save("m0_8-page.xml", xml)
    ctx.save("m0_8-page-elided.xml", sc.elide_data(xml))
    root = sc.parse_xml(xml)
    ink_elements = [e for e in root.iter() if sc.local_name(e).startswith("Ink")]
    counts: dict[str, int] = {}
    for element in ink_elements:
        counts[sc.local_name(element)] = counts.get(sc.local_name(element), 0) + 1
    words = list(root.iter(sc.tag("InkWord", ctx.ns)))
    recognized = [w.get("recognizedText") for w in words]
    with_text = [t for t in recognized if t]
    others = [(sc.local_name(e), e.get("recognizedText")) for e in ink_elements if sc.local_name(e) != "InkWord" and e.get("recognizedText")]

    print(f"\nInk elements on '{args.page}': {counts or 'none'}")
    for index, word in enumerate(words, 1):
        print(f"\n--- one:InkWord {index} ---")
        print(f"recognizedText: {word.get('recognizedText')!r}")
        print(f"attributes: {dict(word.attrib)}")
        print(serialize(word))
    for name, text in others:
        print(f"\n{name} carries recognizedText={text!r}")
    if not ink_elements:
        print("\nNo ink on the page yet. Ink a word on the iPad, wait for the sync, then run again (or with --sync).")

    findings.add("pageInfo", "piBinaryData" if args.binary else "piBasic", "GetPageContent page info")
    findings.add("inkElementCounts", counts, "Ink elements by name")
    findings.add("inkWordCount", len(words), "one:InkWord count")
    findings.add("recognizedTexts", recognized, "recognizedText per InkWord")
    findings.add("recognitionPresent", bool(with_text) if words else None, "Recognition text present")
    findings.add("otherRecognizedText", others, "recognizedText on other ink elements")
    findings.add("sampleInkWord", serialize(words[0]) if words else None, "one:InkWord element and recognizedText as returned")
    findings.add("fixture", str(fixture), "Fixture for M4.1")
    if words and not with_text:
        findings.note("InkWord elements exist but carry no recognizedText: recognition is absent in this read; try --binary, or after a Windows-side sync and reopen.")
    findings.emit()
    return 0


if __name__ == "__main__":
    sys.exit(sc.run(main))
