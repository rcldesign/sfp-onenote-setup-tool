#!/usr/bin/env python3
"""M0.4 Link anchors.

On the M0.2 page (default "M0.2 background y=36") adds three one:Outline link anchors in the shape of prd.md
"Mechanism": a 10 pt calendar-style day number (centered), a 15 pt right-aligned daily date and a centered 15 pt day
name, each an <a href> from GetHyperlinkToObject to a target page, styled through a span with the manifest font
family, the size, the ink color and text-decoration:none. Positions are the manifest anchor rectangles offset by the
sheet outline's origin on the page. Two labelled variants (one without text-decoration:none, one without a span)
show what OneNote does differently with them. The page is read back and printed so the owner can see which style
attributes OneNote kept, and the actual outline positions are compared with the requested ones.

    python m0_4_anchors.py [--page "M0.2 background y=36"] [--partial] [--no-variants] [--no-prompt]

--partial sends only the new outlines in a one:Page element (OneNote adds them); the default sends the page's
existing XML (piBasic, image as CallbackID) plus the new outlines, the pattern the M4.5 writer intends to use. When
the chosen mode fails the other one is tried and both results are recorded.
"""
from __future__ import annotations

import html
import sys

import spike_common as sc

SECTION_TARGET = "M0.4 Targets"
TARGET_TITLE = "M0.4 link target"
DEFAULT_PAGE = "M0.2 background y=36"


def nearest_outline(outlines: list[dict], x: float, y: float, tolerance: float = 25.0) -> dict | None:
    best, best_distance = None, tolerance
    for outline in outlines:
        if outline["isBackgroundImage"] or outline["x"] is None or outline["y"] is None:
            continue
        distance = max(abs(outline["x"] - x), abs(outline["y"] - y))
        if distance < best_distance:
            best, best_distance = outline, distance
    return best


def main() -> int:
    parser = sc.base_parser("M0.4: three styled link anchors over the test sheet, read back to see what OneNote kept.")
    parser.add_argument("--page", default=DEFAULT_PAGE, help="title of the M0.2 page to add the anchors to")
    parser.add_argument("--partial", action="store_true", help="send only the new outlines instead of the page XML plus outlines")
    parser.add_argument("--no-variants", action="store_true", help="skip the two labelled style variants")
    args = parser.parse_args()
    ctx = sc.Context(args)
    findings = ctx.findings("M0.4", "Link anchors")
    manifest = ctx.manifest
    family = manifest["font"]["family"]
    ink = manifest["colors"]["ink"]

    on, notebook_id, _ = ctx.open_notebook()
    page = sc.find_page(on, notebook_id, args.page, ctx.ns)
    if page is None:
        raise sc.SpikeError(f"page '{args.page}' not found; run m0_2_background.py first or pass --page")
    page_id = page["ID"]
    target_id = sc.ensure_page(on, ctx.section(SECTION_TARGET), TARGET_TITLE, fresh=False, ns=ctx.ns)
    href = on.get_hyperlink(target_id)
    findings.add("href", href, "href from GetHyperlinkToObject")

    current = on.get_page_content(page_id, sc.PI_BASIC)
    origin_x, origin_y = sc.sheet_origin(current, ctx.ns)
    findings.add("sheetOrigin", {"x": origin_x, "y": origin_y}, "Sheet outline origin on the page")

    anchors = sc.spike_anchors(manifest)
    specs = [
        ("calendarNumber", anchors["calendarNumber"], True, False),
        ("dailyDate", anchors["dailyDate"], True, False),
        ("dayName", anchors["dayName"], True, False),
    ]
    if not args.no_variants:
        header = manifest["links"]["overviewDayHeader"]
        grid = manifest["slots"]["dayGrid"]
        day_name_2 = dict(anchors["dayName"], x=anchors["dayName"]["x"] + header["pitch"], text="Tuesday")
        number_2 = dict(anchors["calendarNumber"], x=grid["columnX"][2], text="5")
        specs.append(("dayName-underlined", day_name_2, True, True))
        specs.append(("calendarNumber-plainAnchor", number_2, False, False))

    fragments = []
    requested = {}
    for name, a, with_span, underline in specs:
        if with_span:
            inner = sc.anchor_html(href, a["text"], family, a["sizePt"], ink, underline=underline)
        else:
            inner = f"<a href=\"{html.escape(href, quote=True)}\">{html.escape(a['text'])}</a>"
        x, y = origin_x + a["x"], origin_y + a["y"]
        requested[name] = {"x": x, "y": y, "w": a["w"], "h": a["h"], "align": a["align"], "text": a["text"], "html": inner}
        fragments.append(sc.anchor_outline_xml(x, y, a["w"], a["h"], a["align"], inner, z=1))
    body = "".join(fragments)
    print("\nAnchor outlines sent:\n" + sc.pretty(f"<one:Page xmlns:one=\"{ctx.ns}\">{body}</one:Page>"))

    modes = ["partial", "merge"] if args.partial else ["merge", "partial"]
    mode_results = {}
    for mode in modes:
        xml = sc.page_xml(page_id, None, body, ns=ctx.ns) if mode == "partial" else sc.insert_before_page_end(current, body)
        try:
            on.update_page_content(xml)
            mode_results[mode] = "ok"
            break
        except sc.SpikeError as exc:
            mode_results[mode] = str(exc)
            sc.log(f"{mode} update failed: {exc}")
    findings.add("updateModes", mode_results, "UpdatePageContent mode results (merge = page XML plus outlines, partial = outlines only)")
    if "ok" not in mode_results.values():
        findings.emit()
        raise sc.SpikeError("no UpdatePageContent mode succeeded")

    readback = on.get_page_content(page_id, sc.PI_BASIC)
    path = ctx.save("m0_4-page.xml", readback)
    print(f"\n----- page XML read back ({path}) -----")
    print(sc.pretty(sc.elide_data(readback)))
    outlines = sc.page_summary(readback, ctx.ns)["outlines"]

    per_anchor = {}
    kept = {"fontFamily": [], "color": [], "textDecorationNone": [], "href": []}
    for name, req in requested.items():
        found = nearest_outline(outlines, req["x"], req["y"])
        if found is None:
            per_anchor[name] = {"requested": req, "readBack": None}
            findings.add(name, "not found in the read-back XML", f"Anchor {name}")
            continue
        text = " ".join(found["texts"])
        lowered = text.lower()
        checks = {
            "fontFamily": family.lower() in lowered,
            "color": ink.lower() in lowered,
            "textDecorationNone": "text-decoration:none" in lowered.replace(" ", ""),
            "href": "href=" in lowered,
        }
        if "underlined" not in name and "plain" not in name:
            for key, ok in checks.items():
                kept[key].append(ok)
        per_anchor[name] = {
            "requested": req,
            "readBack": {
                "objectID": found["objectID"],
                "x": found["x"], "y": found["y"], "width": found["width"], "height": found["height"],
                "alignment": found["alignment"], "html": text,
                "deltaX": None if found["x"] is None else round(found["x"] - req["x"], 2),
                "deltaY": None if found["y"] is None else round(found["y"] - req["y"], 2),
            },
            "kept": checks,
        }
        findings.add(
            name,
            f"outline at ({sc.fmt(found['x'] or 0)}, {sc.fmt(found['y'] or 0)}) delta ({sc.fmt(per_anchor[name]['readBack']['deltaX'] or 0)}, "
            f"{sc.fmt(per_anchor[name]['readBack']['deltaY'] or 0)}), alignment {found['alignment']}, kept: "
            + ", ".join(k for k, ok in checks.items() if ok) + "; dropped: " + (", ".join(k for k, ok in checks.items() if not ok) or "nothing")
            + f"; text as returned: {text}",
            f"Anchor {name}",
        )
    findings.add("anchors", per_anchor, "Per-anchor detail")
    findings.add("spanColorKeptInXml", all(kept["color"]) if kept["color"] else None, "span color kept in the XML")
    findings.add("textDecorationKeptInXml", all(kept["textDecorationNone"]) if kept["textDecorationNone"] else None, "text-decoration:none kept in the XML")
    findings.add("fontFamilyKeptInXml", all(kept["fontFamily"]) if kept["fontFamily"] else None, "font-family kept in the XML")

    on.navigate_to(page_id)
    print("\nOn the PC: click each anchor (single click), then zoom to 100 % and save a screenshot as scratch/m0_4-anchors.png.")
    print("The frames drawn on the sheet are the requested rectangles; the distance from a frame's top-left corner to the")
    print("first glyph's top-left is OneNote's internal padding, the anchorPaddingPt the manifest needs.")
    navigate = ctx.ask_yes_no("Did all three anchors navigate to the target page?")
    findings.add("linksNavigate", navigate, "Links navigate on click")
    color = ctx.ask_yes_no("Is the anchor text in ink color (not hyperlink blue)?")
    findings.add("spanColorHonored", color, "span color honored")
    underline = ctx.ask_yes_no("Is the anchor text underlined?")
    findings.add("textDecorationHonored", None if underline is None else not underline, "text-decoration:none honored")
    padding = ctx.ask_point("Padding measured from the screenshot as x,y in points (glyph inset from the frame corner)")
    findings.add("anchorPaddingPt", padding, "Internal padding offset to add to the manifest (x, y)")
    if padding is not None:
        findings.propose("anchorPaddingPt", padding)
    else:
        findings.note("anchorPaddingPt not measured yet: read it from scratch/m0_4-anchors.png and set decisions.anchorPaddingPt in scratch/findings.json.")
    if underline is not None:
        findings.propose("anchorUnderline", underline)
    elif kept["textDecorationNone"]:
        findings.propose("anchorUnderline", not all(kept["textDecorationNone"]))
    findings.add("screenshot", f"{ctx.scratch}/m0_4-anchors.png", "Screenshot")
    findings.emit()
    return 0


if __name__ == "__main__":
    sys.exit(sc.run(main))
