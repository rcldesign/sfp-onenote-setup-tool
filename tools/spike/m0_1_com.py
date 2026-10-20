#!/usr/bin/env python3
"""M0.1 COM reachability.

Looks up the OneNote ProgIDs in the registry, creates OneNote.Application, reads the version of the OneNote
executable, calls GetHierarchy with the 2013 schema and reports the root element, its attributes, the namespace
OneNote answered with, and the open notebooks.

    python m0_1_com.py [--no-prompt]
"""
from __future__ import annotations

import re
import sys

import spike_common as sc

PROGIDS = ("OneNote.Application", "OneNote.Application.12", "OneNote.Application.14", "OneNote.Application.15", "OneNote.Application.16")


def registry_progid(progid: str) -> dict | None:
    """CLSID and LocalServer32 of a ProgID from HKEY_CLASSES_ROOT, or None when it is not registered."""
    try:
        import winreg
    except ImportError:
        return None
    try:
        with winreg.OpenKey(winreg.HKEY_CLASSES_ROOT, progid + r"\CLSID") as key:
            clsid = winreg.QueryValue(key, None)
    except OSError:
        return None
    server = None
    try:
        with winreg.OpenKey(winreg.HKEY_CLASSES_ROOT, rf"CLSID\{clsid}\LocalServer32") as key:
            server = winreg.QueryValue(key, None)
    except OSError:
        pass
    return {"clsid": clsid, "localServer32": server}


def exe_path_from_server(server: str | None) -> str | None:
    if not server:
        return None
    match = re.match(r'^\s*"?([^"]+?\.exe)"?', server, re.IGNORECASE)
    return match.group(1) if match else server


def file_version(path: str | None) -> str | None:
    if not path:
        return None
    try:
        import win32api

        info = win32api.GetFileVersionInfo(path, "\\")
        ms, ls = info["FileVersionMS"], info["FileVersionLS"]
        return f"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}"
    except Exception as exc:  # noqa: BLE001  (version is informational)
        sc.log(f"file version unavailable: {exc}")
        return None


def main() -> int:
    parser = sc.base_parser("M0.1: create OneNote.Application, print its version and the open notebooks, confirm the 2013 namespace.")
    args = parser.parse_args()
    ctx = sc.Context(args)
    findings = ctx.findings("M0.1", "COM reachability")

    progids = {p: registry_progid(p) for p in PROGIDS}
    findings.add("progIds", progids, "ProgIDs registered (CLSID and server)")
    main_entry = progids.get(sc.PROGID) or {}
    exe = exe_path_from_server(main_entry.get("localServer32"))
    version = file_version(exe)
    findings.add("oneNoteExe", exe, "OneNote executable")
    findings.add("oneNoteVersion", version, "OneNote version string")

    on = ctx.connect()
    findings.add("binding", on.binding, "pywin32 binding")

    # The 2013 schema, asked for explicitly.
    xml_2013 = on.get_hierarchy("", sc.HS_NOTEBOOKS, sc.XS_2013)
    root = sc.parse_xml(xml_2013)
    ns = sc.namespace_of(root)
    findings.add("rootElement", sc.local_name(root), "GetHierarchy root element")
    findings.add("rootAttributes", dict(root.attrib), "GetHierarchy root attributes")
    findings.add("namespace2013", ns, "Namespace returned for xs2013")
    findings.add("namespace2013Accepted", ns == ctx.ns, "2013 schema namespace accepted by GetHierarchy")
    ctx.save("m0_1-hierarchy-2013.xml", xml_2013)

    # The type library default, to show what OneNote answers when no schema is named.
    try:
        xml_default = on.get_hierarchy("", sc.HS_NOTEBOOKS, None)
        findings.add("namespaceDefault", sc.namespace_of(sc.parse_xml(xml_default)), "Namespace returned when xsSchema is omitted")
    except sc.SpikeError as exc:
        findings.add("namespaceDefault", f"call failed: {exc}", "Namespace returned when xsSchema is omitted")

    open_notebooks = sc.notebooks(on, ctx.ns)
    findings.add("openNotebooks", [{"name": n["name"], "path": n["path"], "isCurrentlyViewed": n["isCurrentlyViewed"]} for n in open_notebooks], "Open notebooks listed")
    findings.add("openNotebookCount", len(open_notebooks), "Open notebook count")
    print("\nOpen notebooks:")
    for notebook in open_notebooks:
        print(f"  {notebook['name']}  ({notebook['path']})  viewed={notebook['isCurrentlyViewed']}")

    for attr in root.attrib:
        if "version" in attr.lower():
            findings.note(f"root attribute {attr} = {root.attrib[attr]}")
    try:
        findings.add("defaultNotebookFolder", on.get_special_location(sc.SL_DEFAULT_NOTEBOOK_FOLDER), "OneNote default notebook folder")
    except sc.SpikeError as exc:
        findings.note(f"GetSpecialLocation failed: {exc}")

    findings.emit()
    return 0


if __name__ == "__main__":
    sys.exit(sc.run(main))
