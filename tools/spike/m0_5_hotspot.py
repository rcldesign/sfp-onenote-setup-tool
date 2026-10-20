#!/usr/bin/env python3
"""M0.5 Hotspot alternative.

Places a transparent 1 x 1 PNG as a normal (not background) one:Image with the hyperlink attribute, sized to the
calendar day-number box from the manifest (30.3 x 14 pt), over the "4" the test sheet bakes into the first calendar
box. Two shapes are tried in order: the image inside a one:Outline, then the image as a direct child of one:Page
with its own Position. The page is read back to show whether the hyperlink attribute survived, and the owner answers
on the console whether a click follows the link on Windows and whether the hotspot is visible or selectable.

    python m0_5_hotspot.py [--page "M0.2 background y=36"] [--direct] [--no-prompt]
"""
from __future__ import annotations

import sys

import spike_common as sc

SECTION_TARGET = "M0.4 Targets"
TARGET_TITLE = "M0.5 hotspot target"
DEFAULT_PAGE = "M0.2 background y=36"


def main() -> int:
    parser = sc.base_parser("M0.5: a transparent image with a hyperlink attribute over a baked number.")
    parser.add_argument("--page", default=DEFAULT_PAGE, help="title of the M0.2 page to add the hotspot to")
    parser.add_argument("--direct", action="store_true", help="try the direct one:Image shape before the outline shape")
    args = parser.parse_args()
    ctx = sc.Context(args)
    findings = ctx.findings("M0.5", "Hotspot alternative")

    on, notebook_id, _ = ctx.open_notebook()
    page = sc.find_page(on, notebook_id, args.page, ctx.ns)
    if page is None:
        raise sc.SpikeError(f"page '{args.page}' not found; run m0_2_background.py first or pass --page")
    page_id = page["ID"]
    target_id = sc.ensure_page(on, ctx.section(SECTION_TARGET), TARGET_TITLE, fresh=False, ns=ctx.ns)
    href = on.get_hyperlink(target_id)

    current = on.get_page_content(page_id, sc.PI_BASIC)
    origin_x, origin_y = sc.sheet_origin(current, ctx.ns)
    spot = sc.spike_anchors(ctx.manifest)["hotspot"]
    x, y, w, h = origin_x + spot["x"], origin_y + spot["y"], spot["w"], spot["h"]
    png = sc.transparent_png_base64()
    findings.add("hotspotRect", {"x": x, "y": y, "w": w, "h": h}, "Hotspot rectangle on the page (points)")

    shapes = [("outline", sc.image_outline_xml(png, x, y, w, h, href)), ("direct", sc.image_direct_xml(png, x, y, w, h, href))]
    if args.direct:
        shapes.reverse()
    results = {}
    worked = None
    for name, fragment in shapes:
        print(f"\nShape {name}:\n" + sc.pretty(f"<one:Page xmlns:one=\"{ctx.ns}\">{sc.elide_data(fragment)}</one:Page>"))
        try:
            on.update_page_content(sc.insert_before_page_end(current, fragment))
            results[name] = "ok"
            worked = name
            break
        except sc.SpikeError as exc:
            results[name] = str(exc)
            sc.log(f"shape {name} failed: {exc}")
    findings.add("shapeResults", results, "UpdatePageContent per shape")
    findings.add("shapeAccepted", worked, "Shape accepted")
    if worked is None:
        findings.emit()
        raise sc.SpikeError("neither image shape was accepted")

    readback = on.get_page_content(page_id, sc.PI_BASIC)
    path = ctx.save("m0_5-page.xml", readback)
    summary = sc.page_summary(readback, ctx.ns)
    images = [o for o in summary["outlines"] if o["imageHyperlink"] and not o["isBackgroundImage"]]
    direct = [i for i in summary["directImages"] if i["hyperlink"]]
    print(f"\n----- page XML read back ({path}) -----")
    print(sc.pretty(sc.elide_data(readback)))
    found = images[0] if images else (direct[0] if direct else None)
    findings.add("hyperlinkKeptInXml", found is not None, "hyperlink attribute kept in the XML")
    if found:
        findings.add("hotspotReadBack", found, "Hotspot as read back")

    on.navigate_to(page_id)
    print("\nOn the PC: single-click the baked '4' in the first calendar box of the sheet.")
    follows = ctx.ask_yes_no("Did the click open the target page?")
    visible = ctx.ask_yes_no("Is the hotspot visible (any border, placeholder or tint)?")
    selectable = ctx.ask_yes_no("Can you select it (handles appear on click or drag)?")
    findings.add("clickFollowsOnWindows", follows, "Transparent image with hyperlink attribute: click follows on Windows")
    findings.add("visible", visible, "Visible")
    findings.add("selectable", selectable, "Selectable")
    viable = found is not None and follows is True
    findings.add("hotspotViableOnWindows", viable if follows is not None else None, "Hotspots viable on Windows (iPad pending M0.7)")
    findings.note(
        "Decision rule from the PRD: hotspots replace text anchors for calendar numbers and the Weekly Overview header only if a tap "
        "follows the link on both Windows and the iPad; otherwise text anchors stand (the manifest links block assumes text anchors)."
    )
    findings.emit()
    return 0


if __name__ == "__main__":
    sys.exit(sc.run(main))
