#!/usr/bin/env python3
"""Shared helpers for the M0 spike scripts.

The M0 scripts (m0_1_com.py to m0_8_ink.py) run on a Windows PC with OneNote desktop and pywin32. This module holds
everything they share:

- the OneNote.Application wrapper: enum values, [out] parameters, the retry-once rule on RPC_E_CALL_REJECTED
  (0x80010001) after 2 s, and decoded HRESULTs;
- GetHierarchy helpers that parse the 2013 namespace;
- the scratch notebook "SFP Spike" under %USERPROFILE%\\Documents\\OneNote Notebooks (never inside OneDrive),
  plus section and page helpers;
- page XML builders in the exact shapes of prd.md ("Page XML" and "Mechanism");
- the findings printer: a JSON block and a Markdown block, merged into scratch/findings.json for m0_9_apply.py;
- the Pillow test sheet: the manifest page size at 220 DPI with a labelled 50 pt grid, so offsets can be read
  from a screenshot.

Nothing here ships. Measured values (page size, colors, font, anchor geometry) are read from
templates/sfp-quarterly.json, never typed into this file.

Self-check (no OneNote, no pywin32; this is what the build environment runs):
    python3 tools/spike/spike_common.py --selfcheck
"""
from __future__ import annotations

import argparse
import base64
import datetime as dt
import html
import io
import json
import os
import re
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
from pathlib import Path
from xml.dom import minidom
from xml.sax.saxutils import quoteattr

PROGID = "OneNote.Application"
NS = "http://schemas.microsoft.com/office/onenote/2013/onenote"
ET.register_namespace("one", NS)  # keeps the one: prefix when elements are serialized for display
SPIKE_DIR = Path(__file__).resolve().parent
REPO_ROOT = SPIKE_DIR.parents[1]
DEFAULT_MANIFEST = REPO_ROOT / "templates" / "sfp-quarterly.json"
DEFAULT_SCRATCH = SPIKE_DIR / "scratch"
NOTEBOOK_NAME = "SFP Spike"
FINDINGS_FILE = "findings.json"
DEFAULT_DPI = 220
DEFAULT_GRID_PT = 50

# OneNote 2013 COM enumerations (values from the OneNote type library, named here so no script carries bare numbers).
HS_SELF, HS_CHILDREN, HS_NOTEBOOKS, HS_SECTIONS, HS_PAGES = 0, 1, 2, 3, 4  # HierarchyScope
XS_2007, XS_2010, XS_2013 = 0, 1, 2  # XMLSchema
CFT_NONE, CFT_NOTEBOOK, CFT_FOLDER, CFT_SECTION = 0, 1, 2, 3  # CreateFileType
NPS_DEFAULT, NPS_BLANK_WITH_TITLE, NPS_BLANK_NO_TITLE = 0, 1, 2  # NewPageStyle
# PageInfo: piBasic returns the page without binary data (images come back as one:CallbackID), piBinaryData with it.
PI_BASIC, PI_BINARY_DATA, PI_SELECTION, PI_BINARY_DATA_SELECTION = 0, 1, 2, 3
PI_FILE_TYPE, PI_BINARY_DATA_FILE_TYPE, PI_SELECTION_FILE_TYPE, PI_ALL = 4, 5, 6, 7
SL_BACKUP_FOLDER, SL_UNFILED_NOTES_SECTION, SL_DEFAULT_NOTEBOOK_FOLDER = 0, 1, 2  # SpecialLocation

# OLE DATE 0 is 30 Dec 1899. OneNote treats it as "do not check the last-modified time"; it is what .NET sends for
# DateTime.MinValue. A datetime converts to that DATE under both early and late binding.
DATE_MIN = dt.datetime(1899, 12, 30)

RPC_E_CALL_REJECTED = 0x80010001
RPC_E_SERVERCALL_RETRYLATER = 0x8001010A
DISP_E_TYPEMISMATCH = 0x80020005
DISP_E_BADPARAMCOUNT = 0x8002000E
HR_LAST_MODIFIED_DATE_DID_NOT_MATCH = 0x80042013
RETRY_CODES = {RPC_E_CALL_REJECTED, RPC_E_SERVERCALL_RETRYLATER}
# If a binding marshals the DATE_MIN datetime to something other than OLE DATE 0, OneNote answers with one of these;
# the call is then repeated once with the plain float 0.0.
DATE_FALLBACK_CODES = {DISP_E_TYPEMISMATCH, HR_LAST_MODIFIED_DATE_DID_NOT_MATCH}
RETRY_DELAY_S = 2.0

# OneNote's own HRESULTs (hrMalformedXML onward), for readable failures.
ONENOTE_ERRORS = {
    0x80042000: "hrMalformedXML",
    0x80042001: "hrInvalidXML",
    0x80042002: "hrCreatingSection",
    0x80042003: "hrOpeningSection",
    0x80042004: "hrSectionDoesNotExist",
    0x80042005: "hrPageDoesNotExist",
    0x80042006: "hrFileDoesNotExist",
    0x80042007: "hrInsertingImage",
    0x80042008: "hrInsertingInk",
    0x80042009: "hrInsertingHtml",
    0x8004200A: "hrNavigatingToPage",
    0x8004200B: "hrSectionNotFound",
    0x8004200C: "hrPageNotFound",
    0x8004200D: "hrFileAlreadyExists",
    0x8004200E: "hrSectionReadOnly",
    0x8004200F: "hrPageReadOnly",
    0x80042010: "hrInsertingXHTML",
    0x80042011: "hrPageObjectDoesNotExist",
    0x80042012: "hrBinaryObjectDoesNotExist",
    0x80042013: "hrLastModifiedDateDidNotMatch",
    0x80042014: "hrGroupDoesNotExist",
    0x80042015: "hrPageDoesNotExistInGroup",
    0x80042016: "hrNoActiveSelection",
    0x80042017: "hrObjectDoesNotExist",
    0x80042018: "hrNotebookDoesNotExist",
    0x80042019: "hrInvalidName",
    0x8004201A: "hrFolderDoesNotExist",
    0x8004201B: "hrInvalidQuery",
    RPC_E_CALL_REJECTED: "RPC_E_CALL_REJECTED",
    RPC_E_SERVERCALL_RETRYLATER: "RPC_E_SERVERCALL_RETRYLATER",
    DISP_E_TYPEMISMATCH: "DISP_E_TYPEMISMATCH",
    DISP_E_BADPARAMCOUNT: "DISP_E_BADPARAMCOUNT",
}


# ----------------------------------------------------------------------------------------------------------------------
# Errors, logging, small formatting helpers
# ----------------------------------------------------------------------------------------------------------------------


class SpikeError(Exception):
    """A failure the script reports instead of a traceback. Carries the HRESULT when a COM call raised it."""

    def __init__(self, message: str, hresult: int | None = None):
        super().__init__(message)
        self.hresult = hresult


def log(message: str) -> None:
    print(f"[{time.strftime('%H:%M:%S')}] {message}", flush=True)


def fmt(value: float) -> str:
    """Invariant number formatting for XML attributes: 36 -> '36', 36.5 -> '36.5', 121.25 -> '121.25'."""
    text = f"{float(value):.2f}".rstrip("0").rstrip(".")
    return text if text not in ("", "-0") else "0"


def format_hresult(hresult: int | None) -> str:
    if hresult is None:
        return "no HRESULT"
    name = ONENOTE_ERRORS.get(hresult)
    return f"0x{hresult:08X}" + (f" ({name})" if name else "")


def hresult_of(exc: BaseException) -> int | None:
    """The HRESULT of a pywin32 com_error, preferring the scode inside excepinfo (DISP_E_EXCEPTION wraps it)."""
    hr = getattr(exc, "hresult", None)
    excepinfo = getattr(exc, "excepinfo", None)
    args = getattr(exc, "args", ())
    if hr is None and args and isinstance(args[0], int):
        hr = args[0]
    if excepinfo is None and len(args) > 2:
        excepinfo = args[2]
    scode = None
    if excepinfo and len(excepinfo) > 5 and isinstance(excepinfo[5], int) and excepinfo[5] != 0:
        scode = excepinfo[5]
    value = scode if scode is not None else hr
    return None if value is None else value & 0xFFFFFFFF


def describe_com_error(exc: BaseException) -> str:
    hr = hresult_of(exc)
    parts = [format_hresult(hr)]
    strerror = getattr(exc, "strerror", None)
    if strerror:
        parts.append(str(strerror))
    excepinfo = getattr(exc, "excepinfo", None)
    if excepinfo and len(excepinfo) > 2 and excepinfo[2]:
        parts.append(str(excepinfo[2]))
    return "; ".join(parts)


def utc_now_text() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds")


# ----------------------------------------------------------------------------------------------------------------------
# OneNote.Application wrapper
# ----------------------------------------------------------------------------------------------------------------------


def import_pywin32():
    """Import pywin32 lazily so --selfcheck and m0_9_apply.py run without it (and off Windows)."""
    try:
        import pythoncom  # noqa: F401  (initializes COM for this thread)
        import pywintypes
        import win32com.client
        import win32com.client.gencache  # noqa: F401  (makes the gencache attribute certain)
    except ImportError as exc:
        raise SpikeError(
            "pywin32 is not installed or this is not Windows. The M0 scripts need a Windows PC with OneNote desktop "
            "and 'pip install pywin32 pillow'. Only 'spike_common.py --selfcheck' and m0_9_apply.py run elsewhere. "
            f"({exc})"
        ) from exc
    return win32com.client, pywintypes.com_error


class OneNote:
    """Thin wrapper around OneNote.Application.

    Every call goes through _call, which retries once after 2 s when OneNote rejects the call because it is busy
    (RPC_E_CALL_REJECTED 0x80010001, and the sibling RPC_E_SERVERCALL_RETRYLATER 0x8001010A), decodes OneNote's
    HRESULTs into names, and handles [out] parameters: pywin32 omits them from the argument list and returns them,
    under both the makepy (early) and the dynamic (late) binding, but if a binding insists on a placeholder the call is
    repeated with one.
    """

    def __init__(self, app, com_error, binding: str, logger=log):
        self.app = app
        self.com_error = com_error
        self.binding = binding
        self.log = logger

    @classmethod
    def connect(cls, logger=log) -> "OneNote":
        client, com_error = import_pywin32()
        try:
            app = client.gencache.EnsureDispatch(PROGID)
            binding = "early (makepy wrappers)"
        except Exception as exc:  # noqa: BLE001  (any makepy failure means: fall back to late binding)
            logger(f"makepy wrappers unavailable ({exc}); using late binding")
            app = client.Dispatch(PROGID)
            binding = "late (dynamic dispatch)"
        logger(f"{PROGID} created, binding: {binding}")
        return cls(app, com_error, binding, logger)

    def _call(self, name: str, *args, out_at: int | None = None):
        method = getattr(self.app, name)
        call_args = list(args)
        padded = False
        retried = False
        date_fallback = False

        def use_plain_date() -> bool:
            nonlocal date_fallback, call_args
            if date_fallback or not any(a is DATE_MIN for a in call_args):
                return False
            date_fallback = True
            call_args = [0.0 if a is DATE_MIN else a for a in call_args]
            self.log(f"{name}: resending the expected-last-modified DATE as 0.0")
            return True

        while True:
            started = time.perf_counter()
            try:
                result = method(*call_args)
            except TypeError as exc:
                if out_at is not None and not padded:
                    padded = True
                    call_args.insert(out_at, "")
                    continue
                if use_plain_date():
                    continue
                raise SpikeError(f"{name}: {exc}") from exc
            except self.com_error as exc:
                hr = hresult_of(exc)
                if hr in RETRY_CODES and not retried:
                    retried = True
                    self.log(f"{name}: call rejected ({format_hresult(hr)}); retrying once after {RETRY_DELAY_S:g} s")
                    time.sleep(RETRY_DELAY_S)
                    continue
                if hr == DISP_E_BADPARAMCOUNT and out_at is not None and not padded:
                    padded = True
                    call_args.insert(out_at, "")
                    continue
                if hr in DATE_FALLBACK_CODES and use_plain_date():
                    continue
                raise SpikeError(f"{name} failed: {describe_com_error(exc)}", hr) from exc
            self.log(f"{name} ok in {1000 * (time.perf_counter() - started):.0f} ms")
            if out_at is not None and padded and result is None:
                raise SpikeError(
                    f"{name}: pywin32 returned no [out] value. Generate the OneNote wrappers once with: "
                    f'python -m win32com.client.makepy "{PROGID}"'
                )
            return result

    # Hierarchy
    def get_hierarchy(self, start_id: str = "", scope: int = HS_PAGES, schema: int | None = XS_2013) -> str:
        if schema is None:  # let the type library default decide (shows the app's default schema)
            return self._call("GetHierarchy", start_id, scope, out_at=2)
        return self._call("GetHierarchy", start_id, scope, schema, out_at=2)

    def open_hierarchy(self, path: str, relative_to: str = "", create: int = CFT_NONE) -> str:
        return self._call("OpenHierarchy", path, relative_to, create, out_at=2)

    def update_hierarchy(self, xml: str, schema: int = XS_2013) -> None:
        self._call("UpdateHierarchy", xml, schema)

    def delete_hierarchy(self, object_id: str, permanently: bool = True) -> None:
        self._call("DeleteHierarchy", object_id, DATE_MIN, permanently)

    def close_notebook(self, notebook_id: str, force: bool = False) -> None:
        self._call("CloseNotebook", notebook_id, force)

    def sync_hierarchy(self, hierarchy_id: str) -> None:
        self._call("SyncHierarchy", hierarchy_id)

    def navigate_to(self, hierarchy_id: str, object_id: str = "", new_window: bool = False) -> None:
        self._call("NavigateTo", hierarchy_id, object_id, new_window)

    def get_special_location(self, which: int) -> str:
        return self._call("GetSpecialLocation", which, out_at=1)

    # Pages
    def create_new_page(self, section_id: str, style: int = NPS_BLANK_WITH_TITLE) -> str:
        return self._call("CreateNewPage", section_id, style, out_at=1)

    def update_page_content(self, xml: str, schema: int = XS_2013, force: bool = False) -> None:
        self._call("UpdatePageContent", xml, DATE_MIN, schema, force)

    def get_page_content(self, page_id: str, info: int = PI_BASIC, schema: int = XS_2013) -> str:
        return self._call("GetPageContent", page_id, info, schema, out_at=1)

    def get_hyperlink(self, hierarchy_id: str, content_object_id: str = "") -> str:
        return self._call("GetHyperlinkToObject", hierarchy_id, content_object_id, out_at=2)


# ----------------------------------------------------------------------------------------------------------------------
# Hierarchy parsing (2013 namespace)
# ----------------------------------------------------------------------------------------------------------------------


def tag(local: str, ns: str = NS) -> str:
    return f"{{{ns}}}{local}"


def local_name(element: ET.Element) -> str:
    return element.tag.rsplit("}", 1)[-1]


def namespace_of(element: ET.Element) -> str:
    return element.tag[1:].split("}", 1)[0] if element.tag.startswith("{") else ""


def parse_xml(xml: str) -> ET.Element:
    return ET.fromstring(xml)


def find_all(xml: str, local: str, ns: str = NS) -> list[ET.Element]:
    return list(parse_xml(xml).iter(tag(local, ns)))


def notebooks(on: OneNote, ns: str = NS) -> list[dict]:
    return [
        {
            "name": e.get("name"),
            "nickname": e.get("nickname"),
            "ID": e.get("ID"),
            "path": e.get("path"),
            "isCurrentlyViewed": e.get("isCurrentlyViewed"),
            "lastModifiedTime": e.get("lastModifiedTime"),
        }
        for e in find_all(on.get_hierarchy("", HS_NOTEBOOKS), "Notebook", ns)
    ]


def sections(on: OneNote, notebook_id: str, ns: str = NS) -> list[dict]:
    return [
        {"name": e.get("name"), "ID": e.get("ID"), "path": e.get("path"), "color": e.get("color")}
        for e in find_all(on.get_hierarchy(notebook_id, HS_SECTIONS), "Section", ns)
    ]


def pages(on: OneNote, hierarchy_id: str, ns: str = NS) -> list[dict]:
    """Pages under a section (or a whole notebook), in OneNote's order, with their level."""
    return [
        {
            "name": e.get("name"),
            "ID": e.get("ID"),
            "pageLevel": e.get("pageLevel"),
            "dateTime": e.get("dateTime"),
            "lastModifiedTime": e.get("lastModifiedTime"),
        }
        for e in find_all(on.get_hierarchy(hierarchy_id, HS_PAGES), "Page", ns)
    ]


# ----------------------------------------------------------------------------------------------------------------------
# Scratch notebook, sections and pages
# ----------------------------------------------------------------------------------------------------------------------


def default_notebooks_folder() -> Path:
    home = os.environ.get("USERPROFILE") or str(Path.home())
    return Path(home) / "Documents" / "OneNote Notebooks"


def assert_not_onedrive(path: Path) -> None:
    """OneNote desktop refuses local notebooks under the OneDrive sync folder, and the card says never to put the
    scratch notebook there. Refuse any path under a OneDrive root or with a OneDrive component."""
    normalized = os.path.normcase(str(path.resolve()))
    for env in ("OneDrive", "OneDriveConsumer", "OneDriveCommercial"):
        root = os.environ.get(env)
        if root and normalized.startswith(os.path.normcase(str(Path(root).resolve())) + os.sep):
            raise SpikeError(f"{path} is inside OneDrive ({env}={root}); pass --notebooks-folder with a local path")
    if any("onedrive" in part.lower() for part in path.parts):
        raise SpikeError(f"{path} has a OneDrive folder in its path; pass --notebooks-folder with a local path")


def ensure_notebook(on: OneNote, notebooks_folder: Path, name: str = NOTEBOOK_NAME, ns: str = NS) -> tuple[str, str]:
    """Find the open notebook called name, or create it as notebooks_folder/name. Returns (ID, path)."""
    for nb in notebooks(on, ns):
        if nb["name"] == name:
            log(f"notebook '{name}' is open: {nb['path']}")
            return nb["ID"], nb["path"] or str(notebooks_folder / name)
    path = notebooks_folder / name
    assert_not_onedrive(path)
    notebooks_folder.mkdir(parents=True, exist_ok=True)
    notebook_id = on.open_hierarchy(str(path), "", CFT_NOTEBOOK)
    log(f"notebook '{name}' created at {path}")
    return notebook_id, str(path)


def ensure_section(on: OneNote, notebook_id: str, notebook_path: str, name: str, ns: str = NS) -> str:
    for section in sections(on, notebook_id, ns):
        if section["name"] == name:
            return section["ID"]
    section_id = on.open_hierarchy(os.path.join(notebook_path, name + ".one"), notebook_id, CFT_SECTION)
    log(f"section '{name}' created")
    return section_id


def ensure_page(
    on: OneNote, section_id: str, title: str, fresh: bool = True, style: int = NPS_BLANK_WITH_TITLE, ns: str = NS
) -> str:
    """Find a page by title in the section, or create it. With fresh=True an existing page of that title is deleted
    first so every run starts from a blank page."""
    existing = [p for p in pages(on, section_id, ns) if p["name"] == title]
    if existing and not fresh:
        return existing[0]["ID"]
    for page in existing:
        on.delete_hierarchy(page["ID"], permanently=True)
        log(f"deleted the previous page '{title}'")
    page_id = on.create_new_page(section_id, style)
    if style != NPS_BLANK_NO_TITLE:
        on.update_page_content(page_xml(page_id, title=title, ns=ns))
    log(f"page '{title}' created")
    return page_id


def find_page(on: OneNote, notebook_id: str, title: str, ns: str = NS) -> dict | None:
    for page in pages(on, notebook_id, ns):
        if page["name"] == title:
            return page
    return None


# ----------------------------------------------------------------------------------------------------------------------
# Page XML builders (shapes from prd.md "Page XML" and "Mechanism")
# ----------------------------------------------------------------------------------------------------------------------


def cdata(text: str) -> str:
    return "<![CDATA[" + text.replace("]]>", "]]]]><![CDATA[>") + "]]>"


def page_settings_xml(color: str) -> str:
    return (
        f"<one:PageSettings RTL=\"false\" color={quoteattr(color)}>"
        "<one:PageSize><one:Automatic/></one:PageSize>"
        "<one:RuleLines visible=\"false\"/>"
        "</one:PageSettings>"
    )


def page_xml(
    page_id: str,
    title: str | None = None,
    body: str = "",
    color: str | None = None,
    date_time: str | None = None,
    meta: dict[str, str] | None = None,
    meta_first: bool = True,
    lang: str = "en-US",
    ns: str = NS,
) -> str:
    """A one:Page document: optional one:Meta, PageSettings, Title, then body (outlines). In the 2013 schema one:Meta
    precedes one:PageSettings; meta_first=False puts it after, for M0.6 to try both."""
    head = f"<one:Page xmlns:one={quoteattr(ns)} ID={quoteattr(page_id)} lang={quoteattr(lang)}"
    if date_time:
        head += f" dateTime={quoteattr(date_time)}"
    parts = [head + ">"]
    meta_xml = "".join(f"<one:Meta name={quoteattr(k)} content={quoteattr(v)}/>" for k, v in (meta or {}).items())
    settings = page_settings_xml(color) if color else ""
    parts.extend([meta_xml, settings] if meta_first else [settings, meta_xml])
    if title is not None:
        parts.append(f"<one:Title><one:OE><one:T>{cdata(title)}</one:T></one:OE></one:Title>")
    parts.append(body)
    parts.append("</one:Page>")
    return "".join(parts)


def position_xml(x: float, y: float, z: int) -> str:
    return f"<one:Position x=\"{fmt(x)}\" y=\"{fmt(y)}\" z=\"{z}\"/>"


def size_xml(w: float, h: float) -> str:
    return f"<one:Size width=\"{fmt(w)}\" height=\"{fmt(h)}\" isSetByUser=\"true\"/>"


def background_outline_xml(png_base64: str, x: float, y: float, w: float, h: float, z: int = 0) -> str:
    """One outline holding one image marked as the page background (prd.md "Page XML")."""
    return (
        "<one:Outline>"
        + position_xml(x, y, z)
        + size_xml(w, h)
        + "<one:OEChildren><one:OE>"
        + "<one:Image format=\"png\" backgroundImage=\"true\">"
        + size_xml(w, h)
        + f"<one:Data>{png_base64}</one:Data>"
        + "</one:Image>"
        + "</one:OE></one:OEChildren>"
        + "</one:Outline>"
    )


def anchor_html(href: str, text: str, family: str, size_pt: float, color: str, underline: bool = False) -> str:
    """The HTML inside a link anchor's one:T (prd.md "Mechanism")."""
    style = f"font-family:{family};font-size:{size_pt:.1f}pt;color:{color}"
    if not underline:
        style += ";text-decoration:none"
    return f"<a href=\"{html.escape(href, quote=True)}\"><span style='{style}'>{html.escape(text)}</span></a>"


def anchor_outline_xml(x: float, y: float, w: float, h: float, align: str, inner_html: str, z: int = 1) -> str:
    """A link anchor: its own one:Outline with one one:OE (alignment left, center or right) and one one:T."""
    return (
        "<one:Outline>"
        + position_xml(x, y, z)
        + size_xml(w, h)
        + "<one:OEChildren>"
        + f"<one:OE alignment={quoteattr(align)}><one:T>{cdata(inner_html)}</one:T></one:OE>"
        + "</one:OEChildren>"
        + "</one:Outline>"
    )


def image_outline_xml(png_base64: str, x: float, y: float, w: float, h: float, hyperlink: str, z: int = 1) -> str:
    """A normal (not background) image with the one:Image hyperlink attribute, inside an outline (M0.5)."""
    return (
        "<one:Outline>"
        + position_xml(x, y, z)
        + size_xml(w, h)
        + "<one:OEChildren><one:OE>"
        + f"<one:Image format=\"png\" hyperlink={quoteattr(hyperlink)}>"
        + size_xml(w, h)
        + f"<one:Data>{png_base64}</one:Data>"
        + "</one:Image>"
        + "</one:OE></one:OEChildren>"
        + "</one:Outline>"
    )


def image_direct_xml(png_base64: str, x: float, y: float, w: float, h: float, hyperlink: str, z: int = 1) -> str:
    """The same image as a direct child of one:Page with its own Position (the other shape the schema allows)."""
    return (
        f"<one:Image format=\"png\" hyperlink={quoteattr(hyperlink)}>"
        + position_xml(x, y, z)
        + size_xml(w, h)
        + f"<one:Data>{png_base64}</one:Data>"
        + "</one:Image>"
    )


def section_xml(section_id: str, name: str, page_list: list[tuple[str | None, str, int]], color: str | None = None, ns: str = NS) -> str:
    """The UpdateHierarchy document for one section (prd.md appendix). A page with ID None asks OneNote to create it."""
    head = f"<one:Section xmlns:one={quoteattr(ns)} ID={quoteattr(section_id)} name={quoteattr(name)}"
    if color:
        head += f" color={quoteattr(color)}"
    rows = []
    for page_id, page_name, level in page_list:
        id_attr = f" ID={quoteattr(page_id)}" if page_id else ""
        rows.append(f"<one:Page{id_attr} name={quoteattr(page_name)} pageLevel=\"{level}\"/>")
    return head + ">" + "".join(rows) + "</one:Section>"


def insert_before_page_end(page_xml_text: str, fragment: str) -> str:
    """Append outlines to a page document as returned by GetPageContent, byte for byte otherwise (keeps CDATA)."""
    index = page_xml_text.rfind("</one:Page>")
    if index < 0:
        raise SpikeError("the page XML has no </one:Page> end tag")
    return page_xml_text[:index] + fragment + page_xml_text[index:]


# ----------------------------------------------------------------------------------------------------------------------
# Read-back helpers
# ----------------------------------------------------------------------------------------------------------------------

_DATA_RE = re.compile(r"(<one:Data>)([^<]{48,})(</one:Data>)")


def elide_data(xml: str) -> str:
    return _DATA_RE.sub(lambda m: f"{m.group(1)}[{len(m.group(2))} base64 chars elided]{m.group(3)}", xml)


def pretty(xml: str) -> str:
    try:
        text = minidom.parseString(xml).toprettyxml(indent="  ")
    except Exception:  # noqa: BLE001  (display only; fall back to the raw text)
        return xml
    return "\n".join(line for line in text.splitlines() if line.strip())


def _float_attr(element: ET.Element | None, name: str) -> float | None:
    if element is None or element.get(name) is None:
        return None
    try:
        return float(element.get(name))
    except ValueError:
        return None


def outline_summary(outline: ET.Element, ns: str = NS) -> dict:
    position = outline.find(tag("Position", ns))
    size = outline.find(tag("Size", ns))
    image = next(iter(outline.iter(tag("Image", ns))), None)
    texts = [(t.text or "") for t in outline.iter(tag("T", ns))]
    oe = next(iter(outline.iter(tag("OE", ns))), None)
    return {
        "objectID": outline.get("objectID"),
        "x": _float_attr(position, "x"),
        "y": _float_attr(position, "y"),
        "z": _float_attr(position, "z"),
        "width": _float_attr(size, "width"),
        "height": _float_attr(size, "height"),
        "alignment": oe.get("alignment") if oe is not None else None,
        "texts": texts,
        "isBackgroundImage": image is not None and image.get("backgroundImage") == "true",
        "imageHyperlink": image.get("hyperlink") if image is not None else None,
        "imageWidth": _float_attr(image.find(tag("Size", ns)) if image is not None else None, "width"),
        "imageHeight": _float_attr(image.find(tag("Size", ns)) if image is not None else None, "height"),
    }


def page_summary(xml: str, ns: str = NS) -> dict:
    root = parse_xml(xml)
    settings = root.find(tag("PageSettings", ns))
    title = root.find(tag("Title", ns))
    title_text = " ".join((t.text or "") for t in title.iter(tag("T", ns))) if title is not None else None
    return {
        "ID": root.get("ID"),
        "namespace": namespace_of(root),
        "dateTime": root.get("dateTime"),
        "lastModifiedTime": root.get("lastModifiedTime"),
        "pageColor": settings.get("color") if settings is not None else None,
        "title": title_text,
        "meta": {m.get("name"): m.get("content") for m in root.iter(tag("Meta", ns))},
        "outlines": [outline_summary(o, ns) for o in root.iter(tag("Outline", ns))],
        "directImages": [
            {
                "objectID": i.get("objectID"),
                "hyperlink": i.get("hyperlink"),
                "x": _float_attr(i.find(tag("Position", ns)), "x"),
                "y": _float_attr(i.find(tag("Position", ns)), "y"),
            }
            for i in root.findall(tag("Image", ns))
        ],
    }


def sheet_origin(xml: str, ns: str = NS) -> tuple[float, float]:
    """Position of the outline holding the background image, or (0, 0) when the page has none."""
    for outline in page_summary(xml, ns)["outlines"]:
        if outline["isBackgroundImage"]:
            return outline["x"] or 0.0, outline["y"] or 0.0
    return 0.0, 0.0


# ----------------------------------------------------------------------------------------------------------------------
# Findings printer
# ----------------------------------------------------------------------------------------------------------------------


def _md_value(value) -> str:
    if value is None:
        return "not determined"
    if isinstance(value, bool):
        return "yes" if value else "no"
    if isinstance(value, (dict, list)):
        return "`" + json.dumps(value, ensure_ascii=False) + "`"
    return str(value)


class Findings:
    """Collects one card's findings and prints them twice: a JSON block (merged into scratch/findings.json under
    cards.<key>, with proposed manifest values under proposed) and a Markdown block in the layout of
    docs/spike-findings.md, ready to paste."""

    def __init__(self, card: str, heading: str, scratch: Path):
        self.card = card
        self.key = card.lower().replace(".", "_")
        self.heading = heading
        self.scratch = scratch
        self.items: dict[str, object] = {}
        self.labels: dict[str, str] = {}
        self.notes: list[str] = []
        self.proposed: dict[str, object] = {}

    def add(self, key: str, value, label: str | None = None) -> None:
        self.items[key] = value
        if label:
            self.labels[key] = label

    def note(self, text: str) -> None:
        self.notes.append(text)

    def propose(self, key: str, value) -> None:
        self.proposed[key] = value

    def to_dict(self) -> dict:
        return {
            "card": self.card,
            "heading": self.heading,
            "generatedAt": utc_now_text(),
            "items": self.items,
            "notes": self.notes,
            "proposed": self.proposed,
        }

    def markdown(self) -> str:
        lines = [f"## {self.card} {self.heading}", ""]
        for key, value in self.items.items():
            lines.append(f"- {self.labels.get(key, key)}: {_md_value(value)}")
        for note in self.notes:
            lines.append(f"- Note: {note}")
        for key, value in self.proposed.items():
            lines.append(f"- Proposed manifest value onenote.{key}: {_md_value(value)}")
        return "\n".join(lines) + "\n"

    def emit(self) -> None:
        data = self.to_dict()
        print(f"\n===== findings {self.card} (JSON) =====")
        print(json.dumps(data, indent=2, ensure_ascii=False))
        print(f"\n===== findings {self.card} (Markdown, paste into docs/spike-findings.md) =====")
        print(self.markdown())
        self.scratch.mkdir(parents=True, exist_ok=True)
        (self.scratch / f"{self.key}-findings.json").write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        (self.scratch / f"{self.key}-findings.md").write_text(self.markdown(), encoding="utf-8")
        merged_path = self.scratch / FINDINGS_FILE
        merged = load_findings(merged_path)
        merged["cards"][self.key] = data
        merged["proposed"].update(self.proposed)
        merged_path.write_text(json.dumps(merged, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"written: {self.scratch / (self.key + '-findings.json')}, {self.scratch / (self.key + '-findings.md')}, merged into {merged_path}")


def load_findings(path: Path) -> dict:
    """The merged findings file (schema documented at the top of m0_9_apply.py), or an empty one."""
    data = {"schemaVersion": 1, "decisions": {}, "proposed": {}, "cards": {}}
    if path.exists():
        loaded = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(loaded, dict):
            raise SpikeError(f"{path} does not hold a JSON object")
        data.update(loaded)
        data.setdefault("decisions", {})
        data.setdefault("proposed", {})
        data.setdefault("cards", {})
    return data


# ----------------------------------------------------------------------------------------------------------------------
# Manifest access and the test sheet
# ----------------------------------------------------------------------------------------------------------------------


def load_manifest(path: Path = DEFAULT_MANIFEST) -> dict:
    if not path.exists():
        raise SpikeError(f"manifest not found: {path}")
    return json.loads(path.read_text(encoding="utf-8"))


def hex_rgb(value: str) -> tuple[int, int, int]:
    v = value.lstrip("#")
    return int(v[0:2], 16), int(v[2:4], 16), int(v[4:6], 16)


def blend(fg: tuple[int, int, int], bg: tuple[int, int, int], opacity: float) -> tuple[int, int, int]:
    return tuple(round(f * opacity + b * (1 - opacity)) for f, b in zip(fg, bg))  # type: ignore[return-value]


def fallback_font_path(manifest: dict) -> Path | None:
    files = manifest.get("font", {}).get("fallbackFiles", {})
    regular = files.get("regular")
    if not regular:
        return None
    path = REPO_ROOT / "templates" / regular
    return path if path.exists() else None


def spike_anchors(manifest: dict) -> dict[str, dict]:
    """The anchor rectangles the spike places, derived from the manifest links block (PDF points, sheet origin):
    a calendar day number (second column, first row), the daily page date, the Monday header of the Weekly Overview,
    and the hotspot slot (first column, first row) that the test sheet bakes a number into for M0.5."""
    links = manifest["links"]
    grid = manifest["slots"]["dayGrid"]
    day = links["calendarDay"]
    date = links["dailyDate"]
    header = links["overviewDayHeader"]
    return {
        "calendarNumber": {
            "x": grid["columnX"][1], "y": grid["rowY"][0] + day["yOffset"], "w": day["w"], "h": day["h"],
            "sizePt": day["sizePt"], "align": day["align"], "text": "4",
        },
        "dailyDate": {
            "x": date["rightX"] - date["w"], "y": date["y"], "w": date["w"], "h": date["h"],
            "sizePt": date["sizePt"], "align": date["align"], "text": "01 / 04 / 27",
        },
        "dayName": {
            "x": header["x0"], "y": header["y"], "w": header["w"], "h": header["h"],
            "sizePt": header["sizePt"], "align": header["align"], "text": "Monday",
        },
        "hotspot": {
            "x": grid["columnX"][0], "y": grid["rowY"][0] + day["yOffset"], "w": day["w"], "h": day["h"],
            "sizePt": day["sizePt"], "align": "center", "text": "4",
        },
    }


def render_test_sheet(manifest: dict, dpi: int = DEFAULT_DPI, grid_pt: int = DEFAULT_GRID_PT, font_path: Path | None = None):
    """The test sheet: page size and colors from the manifest, a 1 pt grid every grid_pt points labelled with its
    coordinates, frames around the spike anchor slots, and a baked day number in the hotspot slot."""
    from PIL import Image, ImageDraw, ImageFont

    width_pt, height_pt = manifest["pageSizePt"]
    scale = dpi / 72.0
    width_px, height_px = round(width_pt * scale), round(height_pt * scale)
    colors = manifest["colors"]
    bg, ink, box = hex_rgb(colors["background"]), hex_rgb(colors["ink"]), hex_rgb(colors["dayBox"])
    grid_color = blend(ink, bg, float(colors.get("greyedInkOpacity", 0.4)))

    def px(value: float) -> int:
        return round(value * scale)

    def font(size_pt: float):
        size = max(6, px(size_pt))
        if font_path is not None:
            try:
                return ImageFont.truetype(str(font_path), size)
            except OSError:
                pass
        try:
            return ImageFont.load_default(size)
        except TypeError:  # Pillow before 10.1 takes no size
            return ImageFont.load_default()

    image = Image.new("RGB", (width_px, height_px), bg)
    draw = ImageDraw.Draw(image)
    line_px = max(1, px(1))
    f_label, f_small, f_title = font(8), font(6), font(15)

    for x in range(0, int(width_pt) + 1, grid_pt):
        draw.line([(px(x), 0), (px(x), height_px)], fill=grid_color, width=line_px)
    for y in range(0, int(height_pt) + 1, grid_pt):
        draw.line([(0, px(y)), (width_px, px(y))], fill=grid_color, width=line_px)
    for x in range(grid_pt, int(width_pt), grid_pt):
        draw.text((px(x) + px(1.5), px(1)), str(x), fill=ink, font=f_label)
    for y in range(grid_pt, int(height_pt), grid_pt):
        draw.text((px(1.5), px(y) + px(1)), str(y), fill=ink, font=f_label)
    for x in range(2 * grid_pt, int(width_pt), 2 * grid_pt):
        for y in range(2 * grid_pt, int(height_pt), 2 * grid_pt):
            draw.text((px(x) + px(1.5), px(y) + px(1)), f"{x},{y}", fill=ink, font=f_small)
    draw.rectangle([0, 0, width_px - 1, height_px - 1], outline=ink, width=max(2, px(2)))
    draw.text((px(1.5), px(1)), "0,0", fill=ink, font=f_label)

    title = (
        f"SFP spike test sheet {fmt(width_pt)} x {fmt(height_pt)} pt at {dpi} DPI "
        f"({width_px} x {height_px} px), grid every {grid_pt} pt, origin top left"
    )
    try:
        draw.text((width_px / 2, height_px / 2), title, fill=ink, font=f_title, anchor="mm")
    except (ValueError, TypeError):
        draw.text((px(60), height_px / 2), title, fill=ink, font=f_title)

    anchors = spike_anchors(manifest)
    grid = manifest["slots"]["dayGrid"]
    draw.rectangle(
        [px(grid["columnX"][0]), px(grid["rowY"][0]), px(grid["columnX"][0] + grid["boxPt"]), px(grid["rowY"][0] + grid["boxPt"])],
        fill=box, outline=ink, width=line_px,
    )
    for name, a in anchors.items():
        x0, y0, x1, y1 = px(a["x"]), px(a["y"]), px(a["x"] + a["w"]), px(a["y"] + a["h"])
        draw.rectangle([x0, y0, x1, y1], outline=ink, width=line_px)
        label = f"{name} ({fmt(a['x'])},{fmt(a['y'])}) {fmt(a['w'])}x{fmt(a['h'])} pt"
        draw.text((x0, y1 + px(1)), label, fill=ink, font=f_small)
        if name == "hotspot":
            try:
                draw.text(((x0 + x1) / 2, (y0 + y1) / 2), a["text"], fill=ink, font=font(a["sizePt"]), anchor="mm")
            except (ValueError, TypeError):
                draw.text((x0 + px(2), y0), a["text"], fill=ink, font=font(a["sizePt"]))
    return image


def write_test_sheet(path: Path, manifest: dict, dpi: int = DEFAULT_DPI, grid_pt: int = DEFAULT_GRID_PT) -> tuple[Path, int, int]:
    image = render_test_sheet(manifest, dpi, grid_pt, fallback_font_path(manifest))
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(str(path), format="PNG", optimize=True)
    return path, image.width, image.height


def png_base64(path: Path) -> str:
    return base64.b64encode(path.read_bytes()).decode("ascii")


def transparent_png_base64() -> str:
    from PIL import Image

    buffer = io.BytesIO()
    Image.new("RGBA", (1, 1), (0, 0, 0, 0)).save(buffer, format="PNG")
    return base64.b64encode(buffer.getvalue()).decode("ascii")


# ----------------------------------------------------------------------------------------------------------------------
# Script context
# ----------------------------------------------------------------------------------------------------------------------


def base_parser(description: str) -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=description)
    parser.add_argument("--manifest", default=str(DEFAULT_MANIFEST), help="templates/sfp-quarterly.json (measured values)")
    parser.add_argument("--scratch", default=str(DEFAULT_SCRATCH), help="where PNGs, XML dumps and findings go (gitignored)")
    parser.add_argument("--notebooks-folder", default=str(default_notebooks_folder()), help="parent folder of the scratch notebook (never OneDrive)")
    parser.add_argument("--notebook-name", default=NOTEBOOK_NAME, help="scratch notebook name")
    parser.add_argument("--dpi", type=int, default=DEFAULT_DPI, help="test sheet DPI")
    parser.add_argument("--grid", type=int, default=DEFAULT_GRID_PT, help="test sheet grid pitch in points")
    parser.add_argument("--regenerate", action="store_true", help="redraw the test sheet even if scratch/test-sheet.png exists")
    parser.add_argument("--no-prompt", action="store_true", help="never ask the owner questions on the console")
    return parser


class Context:
    """Everything a script needs: the manifest, the scratch folder, the OneNote session and the console prompt."""

    def __init__(self, args: argparse.Namespace):
        self.args = args
        self.manifest = load_manifest(Path(args.manifest))
        self.ns = self.manifest.get("onenote", {}).get("schemaNamespace", NS)
        self.scratch = Path(args.scratch)
        self.scratch.mkdir(parents=True, exist_ok=True)
        self.notebooks_folder = Path(args.notebooks_folder)
        self.notebook_name = args.notebook_name
        self.no_prompt = bool(getattr(args, "no_prompt", False))
        self.on: OneNote | None = None
        self.notebook_id: str | None = None
        self.notebook_path: str | None = None

    @property
    def page_width_pt(self) -> float:
        return float(self.manifest["pageSizePt"][0])

    @property
    def page_height_pt(self) -> float:
        return float(self.manifest["pageSizePt"][1])

    def findings(self, card: str, heading: str) -> Findings:
        return Findings(card, heading, self.scratch)

    def connect(self) -> OneNote:
        if self.on is None:
            self.on = OneNote.connect()
        return self.on

    def open_notebook(self) -> tuple[OneNote, str, str]:
        on = self.connect()
        if self.notebook_id is None:
            self.notebook_id, self.notebook_path = ensure_notebook(on, self.notebooks_folder, self.notebook_name, self.ns)
        return on, self.notebook_id, self.notebook_path or ""

    def section(self, name: str) -> str:
        on, notebook_id, notebook_path = self.open_notebook()
        return ensure_section(on, notebook_id, notebook_path, name, self.ns)

    def test_sheet(self) -> Path:
        path = self.scratch / "test-sheet.png"
        if self.args.regenerate or not path.exists():
            _, w, h = write_test_sheet(path, self.manifest, self.args.dpi, self.args.grid)
            log(f"test sheet drawn: {path} ({w} x {h} px)")
        return path

    def ask(self, question: str, choices: tuple[str, ...] = ("y", "n")) -> str | None:
        """Ask the owner on the console; None when prompting is off, stdin is not a terminal, or they skip."""
        if self.no_prompt or not sys.stdin.isatty():
            return None
        prompt = f"{question} [{'/'.join(choices)}/skip] "
        while True:
            answer = input(prompt).strip().lower()
            if answer in ("", "skip", "s"):
                return None
            if answer in choices:
                return answer

    def ask_yes_no(self, question: str) -> bool | None:
        answer = self.ask(question)
        return None if answer is None else answer == "y"

    def ask_text(self, question: str) -> str | None:
        """Free text from the owner; None when prompting is off, stdin is not a terminal, or they leave it empty."""
        if self.no_prompt or not sys.stdin.isatty():
            return None
        answer = input(f"{question} (Enter to skip) ").strip()
        return answer or None

    def ask_point(self, question: str) -> dict | None:
        """A pair 'x,y' in points from the owner, as {"x": .., "y": ..}; None when skipped or unparsable."""
        text = self.ask_text(question)
        if not text:
            return None
        parts = [p.strip() for p in re.split(r"[,; ]+", text) if p.strip()]
        try:
            if len(parts) != 2:
                raise ValueError(text)
            return {"x": float(parts[0]), "y": float(parts[1])}
        except ValueError:
            log(f"could not read '{text}' as x,y; skipped")
            return None

    def save(self, name: str, text: str) -> Path:
        path = self.scratch / name
        path.write_text(text, encoding="utf-8")
        return path


def run(main_function) -> int:
    """Run a script's main under the SpikeError handler and return its exit code."""
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")  # type: ignore[union-attr]
    except (AttributeError, ValueError):
        pass
    try:
        return int(main_function() or 0)
    except SpikeError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("interrupted", file=sys.stderr)
        return 130


# ----------------------------------------------------------------------------------------------------------------------
# Self-check (Pillow only)
# ----------------------------------------------------------------------------------------------------------------------


def selfcheck(manifest_path: Path = DEFAULT_MANIFEST) -> int:
    manifest = load_manifest(manifest_path)
    folder = Path(tempfile.mkdtemp(prefix="sfp-spike-"))
    path, width, height = write_test_sheet(folder / "test-sheet.png", manifest)
    expected_w = round(manifest["pageSizePt"][0] * DEFAULT_DPI / 72)
    expected_h = round(manifest["pageSizePt"][1] * DEFAULT_DPI / 72)
    print(f"test sheet: {path} {width}x{height} px ({path.stat().st_size} bytes)")
    if (width, height) != (expected_w, expected_h):
        print(f"FAIL: expected {expected_w}x{expected_h}")
        return 1

    # The XML builders must produce well-formed 2013-namespace documents in the PRD shapes.
    sheet = background_outline_xml("QUJD", 0, manifest["onenote"]["titleOffsetPt"], *manifest["pageSizePt"])
    anchor = anchor_outline_xml(1, 2, 30, 14, "center", anchor_html("onenote:#a&b", "4", manifest["font"]["family"], 10, manifest["colors"]["ink"]))
    hotspot = image_outline_xml(transparent_png_base64(), 1, 2, 30, 14, "onenote:#x")
    doc = page_xml("{page}", "Title", sheet + anchor + hotspot, color=manifest["colors"]["background"], date_time="2027-01-04T06:00:00.000Z", meta={"sfp.key": "day:2027-01-04"})
    root = parse_xml(doc)
    checks = {
        "namespace": namespace_of(root) == NS,
        "backgroundImage": any(i.get("backgroundImage") == "true" for i in root.iter(tag("Image"))),
        "anchorText": any("text-decoration:none" in (t.text or "") for t in root.iter(tag("T"))),
        "hyperlinkImage": any(i.get("hyperlink") == "onenote:#x" for i in root.iter(tag("Image"))),
        "meta": root.find(tag("Meta")).get("content") == "day:2027-01-04",
        "sheetOrigin": sheet_origin(doc) == (0.0, float(manifest["onenote"]["titleOffsetPt"])),
        "elide": "elided" in elide_data(doc.replace("QUJD", "A" * 100)),
        "section": parse_xml(section_xml("{s}", "Week", [("{p}", "Overview", 1), (None, "New", 2)], color="#F0E6D8")).get("color") == "#F0E6D8",
        "insert": parse_xml(insert_before_page_end(page_xml("{p}", "T"), anchor)).find(tag("Outline")) is not None,
    }
    for name, ok in checks.items():
        print(f"{'ok  ' if ok else 'FAIL'} {name}")
    return 0 if all(checks.values()) else 1


def main() -> int:
    parser = argparse.ArgumentParser(description="M0 spike shared module; run with --selfcheck to draw the test sheet into a temp folder.")
    parser.add_argument("--selfcheck", action="store_true", help="draw the test PNG with Pillow and check the XML builders")
    parser.add_argument("--manifest", default=str(DEFAULT_MANIFEST))
    parser.add_argument("--sheet", help="also write the test sheet to this path")
    args = parser.parse_args()
    if args.sheet:
        path, w, h = write_test_sheet(Path(args.sheet), load_manifest(Path(args.manifest)))
        print(f"{path} {w}x{h}")
        return 0
    if args.selfcheck:
        return selfcheck(Path(args.manifest))
    parser.print_help()
    return 0


if __name__ == "__main__":
    sys.exit(run(main))
