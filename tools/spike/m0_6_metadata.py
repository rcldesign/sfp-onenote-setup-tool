#!/usr/bin/env python3
"""M0.6 Page metadata.

Three independent writes on a fresh page in section "M0.6 Metadata": one:Page dateTime (the planner date at the
manifest's pageDateTimeHour, local, written as UTC) through UpdatePageContent; one:Meta name="sfp.key" (first before
one:PageSettings, the 2013 schema order, then after it if that is rejected); and one:Section color through
UpdateHierarchy (the first manifest section tint). Then SyncHierarchy on the notebook, a re-read of the page and
the section, and a report of which of the three persisted. --reopen also closes and reopens the notebook before the
second read.

    python m0_6_metadata.py [--date 2027-01-04] [--reopen]
"""
from __future__ import annotations

import datetime as dt
import sys
import time

import spike_common as sc

SECTION = "M0.6 Metadata"
PAGE = "M0.6 metadata"
META_KEY = "sfp.key"


def parse_time(value: str | None) -> dt.datetime | None:
    if not value:
        return None
    try:
        return dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None


def same_instant(a: str | None, b: str | None) -> bool | None:
    pa, pb = parse_time(a), parse_time(b)
    if pa is None or pb is None:
        return None
    if pa.tzinfo is None:
        pa = pa.replace(tzinfo=dt.timezone.utc)
    if pb.tzinfo is None:
        pb = pb.replace(tzinfo=dt.timezone.utc)
    return abs((pa - pb).total_seconds()) < 60


def read_state(ctx: sc.Context, on: sc.OneNote, notebook_id: str, page_id: str) -> dict:
    page_xml = on.get_page_content(page_id, sc.PI_BASIC)
    summary = sc.page_summary(page_xml, ctx.ns)
    hierarchy_page = next((p for p in sc.pages(on, notebook_id, ctx.ns) if p["ID"] == page_id), None)
    section = next((s for s in sc.sections(on, notebook_id, ctx.ns) if s["name"] == SECTION), None)
    return {
        "pageDateTime": summary["dateTime"],
        "hierarchyDateTime": hierarchy_page["dateTime"] if hierarchy_page else None,
        "meta": summary["meta"],
        "sectionColor": section["color"] if section else None,
        "xml": page_xml,
    }


def main() -> int:
    parser = sc.base_parser("M0.6: page dateTime, sfp.key meta and section color; sync and re-read.")
    parser.add_argument("--date", default="2027-01-04", help="planner date for the page dateTime (YYYY-MM-DD)")
    parser.add_argument("--reopen", action="store_true", help="close and reopen the notebook before the second read")
    args = parser.parse_args()
    ctx = sc.Context(args)
    findings = ctx.findings("M0.6", "Page metadata")
    manifest = ctx.manifest
    tint = manifest["colors"]["sectionTints"][0]
    hour = int(manifest["onenote"].get("pageDateTimeHour", 6))
    planner_date = dt.date.fromisoformat(args.date)
    local = dt.datetime(planner_date.year, planner_date.month, planner_date.day, hour).astimezone()
    date_time = local.astimezone(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.000Z")
    meta_value = f"day:{planner_date.isoformat()}"

    on, notebook_id, notebook_path = ctx.open_notebook()
    section_id = ctx.section(SECTION)
    page_id = sc.ensure_page(on, section_id, PAGE, fresh=True, ns=ctx.ns)

    # 1. dateTime on one:Page
    try:
        on.update_page_content(sc.page_xml(page_id, PAGE, date_time=date_time, ns=ctx.ns))
        findings.add("dateTimeAccepted", True, "one:Page dateTime accepted by UpdatePageContent")
    except sc.SpikeError as exc:
        findings.add("dateTimeAccepted", False, "one:Page dateTime accepted by UpdatePageContent")
        findings.note(f"dateTime write failed: {exc}")
    findings.add("dateTimeSent", date_time, "dateTime sent")

    # 2. one:Meta sfp.key, before PageSettings (schema order) then after it. PageSettings is there only so the order
    #    can be tested; its color is "automatic", which is always valid, so a rejected hex color (M0.2) cannot mask this result.
    meta_result = None
    for first in (True, False):
        try:
            on.update_page_content(sc.page_xml(page_id, PAGE, color="automatic", meta={META_KEY: meta_value}, meta_first=first, ns=ctx.ns))
            meta_result = "before PageSettings" if first else "after PageSettings"
            break
        except sc.SpikeError as exc:
            findings.note(f"Meta {'before' if first else 'after'} PageSettings failed: {exc}")
    findings.add("metaAccepted", meta_result is not None, "one:Meta sfp.key accepted by UpdatePageContent")
    findings.add("metaPosition", meta_result, "Meta position that OneNote accepted")

    # 3. section color through UpdateHierarchy (existing pages echoed so nothing is dropped)
    page_rows = [(p["ID"], p["name"], int(p["pageLevel"] or 1)) for p in sc.pages(on, section_id, ctx.ns)]
    try:
        on.update_hierarchy(sc.section_xml(section_id, SECTION, page_rows, color=tint, ns=ctx.ns))
        findings.add("sectionColorAccepted", True, f"one:Section color=\"{tint}\" accepted by UpdateHierarchy")
    except sc.SpikeError as exc:
        findings.add("sectionColorAccepted", False, f"one:Section color=\"{tint}\" accepted by UpdateHierarchy")
        findings.note(f"section color write failed: {exc}")

    before = read_state(ctx, on, notebook_id, page_id)
    findings.add("beforeSync", {k: v for k, v in before.items() if k != "xml"}, "Read back before sync")

    on.sync_hierarchy(notebook_id)
    time.sleep(2)
    if args.reopen:
        on.close_notebook(notebook_id, force=False)
        time.sleep(2)
        notebook_id = on.open_hierarchy(notebook_path, "", sc.CFT_NONE)
        ctx.notebook_id = notebook_id
        findings.note("notebook closed and reopened before the second read")
    after = read_state(ctx, on, notebook_id, page_id)
    ctx.save("m0_6-page.xml", after["xml"])
    print("\n----- page XML after sync -----")
    print(sc.pretty(sc.elide_data(after["xml"])))

    findings.add("dateTimeReadBack", after["pageDateTime"], "dateTime read back (page XML)")
    findings.add("dateTimeInHierarchy", after["hierarchyDateTime"], "dateTime read back (hierarchy)")
    findings.add("dateTimePersisted", same_instant(after["pageDateTime"], date_time), "one:Page dateTime persisted after sync and reopen")
    findings.add("metaReadBack", after["meta"].get(META_KEY), "sfp.key read back")
    findings.add("metaPersisted", after["meta"].get(META_KEY) == meta_value, "one:Meta sfp.key persisted")
    findings.add("sectionColorReadBack", after["sectionColor"], "section color read back")
    color_back = (after["sectionColor"] or "").lower()
    findings.add("sectionColorPersisted", color_back == tint.lower(), "one:Section color persisted")
    if color_back and color_back != tint.lower():
        findings.note(f"OneNote stored {after['sectionColor']} for {tint}: it snaps hex colors to its palette (accepted per docs/decisions.md).")
    findings.emit()
    return 0


if __name__ == "__main__":
    sys.exit(sc.run(main))
