#!/usr/bin/env python3
"""M0.2 Background image page.

Creates the scratch notebook and a section "M0.2 Background", then one titled page per offset (default 0, 36, 48)
holding the test sheet as one:Image backgroundImage="true" inside a one:Outline at Position (0, offset), the page
size from the manifest. PageSettings color is the manifest page color on every page but the last, which uses
"automatic"; when OneNote rejects the hex color the page is rewritten with "automatic" and the rejection recorded.
Each page is read back with GetPageContent piBasic (no binary data) and printed, so the outline position OneNote
actually kept can be read next to the one requested.

    python m0_2_background.py [--offsets 0 36 48] [--no-prompt]

Then, on the PC: open each page at 100 % zoom, take a screenshot and save it as scratch/m0_2-y<offset>.png.
The grid on the sheet is every 50 pt from the sheet's own origin, so the gap between the title and the sheet's
top edge can be read in points.
"""
from __future__ import annotations

import sys

import spike_common as sc

SECTION = "M0.2 Background"


def page_title(offset: float) -> str:
    return f"M0.2 background y={sc.fmt(offset)}"


def write_page(ctx: sc.Context, on: sc.OneNote, page_id: str, title: str, png_b64: str, offset: float, color: str) -> dict:
    body = sc.background_outline_xml(png_b64, 0, offset, ctx.page_width_pt, ctx.page_height_pt)
    xml = sc.page_xml(page_id, title, body, color=color, ns=ctx.ns)
    try:
        on.update_page_content(xml)
        return {"ok": True, "error": None}
    except sc.SpikeError as exc:
        return {"ok": False, "error": str(exc), "hresult": sc.format_hresult(exc.hresult)}


def main() -> int:
    parser = sc.base_parser("M0.2: background image pages at several title offsets, with a hex page color and automatic.")
    parser.add_argument("--offsets", type=float, nargs="+", default=[0, 36, 48], help="outline y positions to try (points)")
    args = parser.parse_args()
    ctx = sc.Context(args)
    findings = ctx.findings("M0.2", "Background image page")
    hex_color = ctx.manifest["colors"]["background"]
    manifest_offset = ctx.manifest["onenote"]["titleOffsetPt"]

    on, notebook_id, _ = ctx.open_notebook()
    section_id = ctx.section(SECTION)
    sheet = ctx.test_sheet()
    png_b64 = sc.png_base64(sheet)
    sc.log(f"test sheet {sheet} ({len(png_b64)} base64 chars)")

    results = []
    hex_accepted: bool | None = None
    first_page_id = None
    for index, offset in enumerate(args.offsets):
        title = page_title(offset)
        page_id = sc.ensure_page(on, section_id, title, fresh=True, ns=ctx.ns)
        first_page_id = first_page_id or page_id
        want_hex = index < len(args.offsets) - 1 or len(args.offsets) == 1
        attempts = []
        used = None
        for color in ([hex_color, "automatic"] if want_hex else ["automatic"]):
            outcome = write_page(ctx, on, page_id, title, png_b64, offset, color)
            attempts.append({"color": color, **outcome})
            sc.log(f"{title}: color {color}: {'ok' if outcome['ok'] else outcome['error']}")
            if color == hex_color:
                hex_accepted = outcome["ok"] if hex_accepted is None else (hex_accepted and outcome["ok"])
            if outcome["ok"]:
                used = color
                break

        readback = on.get_page_content(page_id, sc.PI_BASIC)
        path = ctx.save(f"m0_2-y{sc.fmt(offset)}.xml", readback)
        summary = sc.page_summary(readback, ctx.ns)
        sheet_outline = next((o for o in summary["outlines"] if o["isBackgroundImage"]), None)
        print(f"\n----- page XML read back: {title} ({path}) -----")
        print(sc.pretty(sc.elide_data(readback)))
        record = {
            "title": title,
            "requestedY": offset,
            "attempts": attempts,
            "colorUsed": used,
            "colorReadBack": summary["pageColor"],
            "actualX": sheet_outline["x"] if sheet_outline else None,
            "actualY": sheet_outline["y"] if sheet_outline else None,
            "outlineWidth": sheet_outline["width"] if sheet_outline else None,
            "outlineHeight": sheet_outline["height"] if sheet_outline else None,
            "imageWidth": sheet_outline["imageWidth"] if sheet_outline else None,
            "imageHeight": sheet_outline["imageHeight"] if sheet_outline else None,
            "backgroundImageKept": bool(sheet_outline),
        }
        results.append(record)
        actual = "no background outline read back" if not sheet_outline else f"outline at ({sc.fmt(record['actualX'] or 0)}, {sc.fmt(record['actualY'] or 0)}), {sc.fmt(record['outlineWidth'] or 0)} x {sc.fmt(record['outlineHeight'] or 0)} pt"
        findings.add(
            f"y{sc.fmt(offset)}",
            f"update {'ok' if used else 'failed'}, color used {used}, read back color {summary['pageColor']}, {actual}",
            f"Requested y = {sc.fmt(offset)}",
        )

    findings.add("pages", results, "Per-page results")
    findings.add("hexColorAccepted", hex_accepted, f"PageSettings color=\"{hex_color}\": accepted")
    findings.propose("pageColor", hex_color if hex_accepted else "automatic")

    zero = next((r for r in results if r["requestedY"] == 0 and r["actualY"] is not None), None)
    if zero and zero["actualY"] > 0:
        findings.add("yZeroMovedTo", zero["actualY"], "OneNote moved the y = 0 outline down to")
        findings.note("OneNote keeps outlines below the title area: the title offset is the y it moved the y = 0 outline to.")
        findings.propose("titleOffsetPt", zero["actualY"])
    elif zero:
        findings.add("yZeroMovedTo", 0, "OneNote moved the y = 0 outline down to")
        findings.note("OneNote kept y = 0 as requested; whether the sheet overlaps the title is visible only in the screenshot.")

    if first_page_id:
        on.navigate_to(first_page_id)
    print("\nNow open each M0.2 page at 100 % zoom and save a screenshot as scratch/m0_2-y<offset>.png.")
    overlap = ctx.ask_yes_no("Does the y = 0 sheet overlap or hide the page title?")
    findings.add("yZeroOverlapsTitle", overlap, "Does y = 0 overlap the title")
    flush = ctx.ask(f"Which offset sits flush under the title? ({', '.join(sc.fmt(o) for o in args.offsets)})", tuple(sc.fmt(o) for o in args.offsets))
    findings.add("flushOffset", float(flush) if flush is not None else None, "Offset at which the sheet sits flush under the title")
    if flush is not None:
        findings.propose("titleOffsetPt", float(flush))
    elif "titleOffsetPt" not in findings.proposed:
        findings.note(f"No flush offset chosen; the manifest keeps titleOffsetPt = {manifest_offset} until the screenshots are read.")
    findings.add("screenshots", f"{ctx.scratch}/m0_2-y*.png", "Screenshots")
    findings.emit()
    return 0


if __name__ == "__main__":
    sys.exit(sc.run(main))
