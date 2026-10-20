#!/usr/bin/env python3
"""M0.3 Subpage levels.

Creates three pages in a section "M0.3 Levels" (Weekly Overview, Mon Jan 4, Tue Jan 5, in that creation order),
then one UpdateHierarchy call with pageLevel 1, 2, 2 in a different order (Overview, Tue, Mon) to prove that
OneNote honors both the levels and the order given. Reads the section back with GetHierarchy hsPages and prints
the names with their levels in order. A second UpdateHierarchy adds a page element without an ID to learn whether
pages must exist (CreateNewPage) before levels are set, or whether UpdateHierarchy creates them.

    python m0_3_levels.py
"""
from __future__ import annotations

import sys

import spike_common as sc

SECTION = "M0.3 Levels"
CREATE_ORDER = ["Weekly Overview", "Mon Jan 4", "Tue Jan 5"]
DESIRED = [("Weekly Overview", 1), ("Tue Jan 5", 2), ("Mon Jan 4", 2)]


def listing(on: sc.OneNote, section_id: str, ns: str) -> list[tuple[str, str]]:
    return [(p["name"], p["pageLevel"] or "?") for p in sc.pages(on, section_id, ns)]


def print_listing(label: str, rows: list[tuple[str, str]]) -> None:
    print(f"\n{label}:")
    for name, level in rows:
        print(f"  {'    ' * (int(level) - 1 if level.isdigit() else 0)}{name}  (pageLevel {level})")


def main() -> int:
    parser = sc.base_parser("M0.3: UpdateHierarchy with pageLevel 1, 2, 2 in a chosen order, then read the levels back.")
    args = parser.parse_args()
    ctx = sc.Context(args)
    findings = ctx.findings("M0.3", "Subpage levels")
    on, _, _ = ctx.open_notebook()
    section_id = ctx.section(SECTION)

    for page in sc.pages(on, section_id, ctx.ns):
        on.delete_hierarchy(page["ID"], permanently=True)
    ids = {title: sc.ensure_page(on, section_id, title, fresh=True, ns=ctx.ns) for title in CREATE_ORDER}
    print_listing("After CreateNewPage (creation order, default levels)", listing(on, section_id, ctx.ns))

    xml = sc.section_xml(section_id, SECTION, [(ids[name], name, level) for name, level in DESIRED], ns=ctx.ns)
    print("\nUpdateHierarchy document:\n" + sc.pretty(xml))
    on.update_hierarchy(xml)
    rows = listing(on, section_id, ctx.ns)
    print_listing("After UpdateHierarchy (hsPages)", rows)
    ctx.save("m0_3-hierarchy.xml", on.get_hierarchy(section_id, sc.HS_PAGES))

    expected = [(name, str(level)) for name, level in DESIRED]
    findings.add("listing", [f"{name} (level {level})" for name, level in rows], "Page list shows the indentation and order")
    findings.add("orderAsRequested", [r[0] for r in rows] == [e[0] for e in expected], "Order as requested (Overview, Tue, Mon)")
    findings.add("levelsAsRequested", rows == expected, "Levels as requested (1, 2, 2)")

    # Can UpdateHierarchy create a page itself (no ID) with a level, or must CreateNewPage come first?
    xml_new = sc.section_xml(section_id, SECTION, [(ids[name], name, level) for name, level in DESIRED] + [(None, "Weekly Reflection", 1)], ns=ctx.ns)
    try:
        on.update_hierarchy(xml_new)
        after = listing(on, section_id, ctx.ns)
        created = any(name == "Weekly Reflection" for name, _ in after)
        print_listing("After UpdateHierarchy with a page element without ID", after)
        findings.add("updateHierarchyCreatesPages", created, "UpdateHierarchy creates a page given without ID")
        findings.add("pagesMustExistFirst", not created, "Pages must exist before levels are set")
        if created:
            findings.note("The page created by UpdateHierarchy has no content and no title object until UpdatePageContent sets one.")
    except sc.SpikeError as exc:
        findings.add("updateHierarchyCreatesPages", False, "UpdateHierarchy creates a page given without ID")
        findings.add("pagesMustExistFirst", True, "Pages must exist before levels are set")
        findings.note(f"UpdateHierarchy with an ID-less page failed: {exc}")

    on.navigate_to(ids["Weekly Overview"])
    findings.emit()
    return 0


if __name__ == "__main__":
    sys.exit(sc.run(main))
