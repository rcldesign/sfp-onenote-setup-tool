"""Fake win32com.client for smoke-testing the spike scripts on Linux. Imitates pywin32 early binding: [out]
parameters are omitted from the argument list and returned."""
from __future__ import annotations

import os
import re
import uuid
import xml.etree.ElementTree as ET

import pywintypes

NS = "http://schemas.microsoft.com/office/onenote/2013/onenote"
NS2010 = "http://schemas.microsoft.com/office/onenote/2010/onenote"
ET.register_namespace("one", NS)


def q(local):
    return f"{{{NS}}}{local}"


def com_error(hr, text):
    signed = hr - (1 << 32) if hr >= 1 << 31 else hr
    return pywintypes.com_error(-2147352567, "Exception occurred.", (0, "OneNote", text, None, 0, signed), None)


class FakeOneNote:
    def __init__(self):
        self.notebooks = {}
        self.sections = {}
        self.pages = {}
        self.rejected_once = os.environ.get("FAKE_REJECT_ONCE") == "1"
        self.reject_hex = os.environ.get("FAKE_REJECT_HEX") == "1"
        self.ink = os.environ.get("FAKE_INK") == "1"
        self.calls = []

    # ----- helpers
    @staticmethod
    def _id(kind):
        return "{" + str(uuid.uuid4()).upper() + "}{1}{" + kind + "}"

    def _page_el(self, pid):
        p = self.pages[pid]
        el = ET.Element(q("Page"), {"ID": pid, "name": p["name"], "pageLevel": str(p["level"]), "lastModifiedTime": "2026-10-20T10:00:00.000Z"})
        if p.get("dateTime"):
            el.set("dateTime", p["dateTime"])
        return el

    def _section_el(self, sid, with_pages):
        s = self.sections[sid]
        el = ET.Element(q("Section"), {"ID": sid, "name": s["name"], "path": s["path"]})
        if s.get("color"):
            el.set("color", s["color"])
        if with_pages:
            for pid in s["pages"]:
                el.append(self._page_el(pid))
        return el

    def _notebook_el(self, nid, with_sections, with_pages):
        n = self.notebooks[nid]
        el = ET.Element(q("Notebook"), {"ID": nid, "name": n["name"], "nickname": n["name"], "path": n["path"], "isCurrentlyViewed": "true"})
        if with_sections:
            for sid in n["sections"]:
                el.append(self._section_el(sid, with_pages))
        return el

    @staticmethod
    def _xml(el):
        return '<?xml version="1.0"?>' + ET.tostring(el, encoding="unicode")

    # ----- API
    def GetHierarchy(self, start_id, scope, schema=1):
        self.calls.append(("GetHierarchy", scope, schema))
        ns = NS if schema == 2 else NS2010
        if start_id == "":
            root = ET.Element(q("Notebooks"))
            for nid in self.notebooks:
                root.append(self._notebook_el(nid, scope >= 3, scope >= 4))
        elif start_id in self.notebooks:
            root = self._notebook_el(start_id, scope >= 3 or scope == 1, scope >= 4)
        elif start_id in self.sections:
            root = self._section_el(start_id, scope >= 4 or scope == 1)
        elif start_id in self.pages:
            root = self._page_el(start_id)
        else:
            raise com_error(0x80042017, "hrObjectDoesNotExist")
        text = self._xml(root)
        return text.replace(NS, ns) if ns != NS else text

    def OpenHierarchy(self, path, relative_to, cft):
        self.calls.append(("OpenHierarchy", path, cft))
        if cft == 1:
            name = os.path.basename(path.rstrip("\\/"))
            nid = self._id("B0")
            self.notebooks[nid] = {"name": name, "path": path, "sections": []}
            return nid
        if cft == 3:
            name = os.path.splitext(os.path.basename(path))[0]
            if relative_to not in self.notebooks:
                raise com_error(0x80042018, "hrNotebookDoesNotExist")
            sid = self._id("B0")
            self.sections[sid] = {"name": name, "path": path, "notebook": relative_to, "pages": [], "color": None}
            self.notebooks[relative_to]["sections"].append(sid)
            return sid
        for nid, n in self.notebooks.items():
            if n["path"] == path:
                return nid
        raise com_error(0x80042006, "hrFileDoesNotExist")

    def CreateNewPage(self, section_id, style=0):
        pid = self._id("E1")
        self.pages[pid] = {"name": "Untitled page", "level": 1, "doc": None, "section": section_id, "dateTime": None}
        self.sections[section_id]["pages"].append(pid)
        return pid

    def UpdateHierarchy(self, xml, schema=1):
        root = ET.fromstring(xml)
        if root.tag != q("Section"):
            raise com_error(0x80042000, "hrMalformedXML")
        sid = root.get("ID")
        if sid not in self.sections:
            raise com_error(0x80042004, "hrSectionDoesNotExist")
        s = self.sections[sid]
        if root.get("color"):
            s["color"] = root.get("color")
        order = []
        for page in root.findall(q("Page")):
            pid = page.get("ID")
            if not pid:
                pid = self._id("E1")
                self.pages[pid] = {"name": page.get("name"), "level": int(page.get("pageLevel", "1")), "doc": None, "section": sid, "dateTime": None}
            if pid not in self.pages:
                raise com_error(0x80042005, "hrPageDoesNotExist")
            self.pages[pid]["name"] = page.get("name") or self.pages[pid]["name"]
            self.pages[pid]["level"] = int(page.get("pageLevel", "1"))
            order.append(pid)
        s["pages"] = order + [p for p in s["pages"] if p not in order]

    def UpdatePageContent(self, xml, date=None, schema=1, force=False):
        if self.rejected_once:
            self.rejected_once = False
            raise pywintypes.com_error(-2147418111, "Call was rejected by callee.", None, None)
        try:
            root = ET.fromstring(xml)
        except ET.ParseError as exc:
            raise com_error(0x80042000, f"hrMalformedXML {exc}")
        if root.tag != q("Page"):
            raise com_error(0x80042001, "hrInvalidXML: root is not one:Page in the 2013 namespace")
        pid = root.get("ID")
        if pid not in self.pages:
            raise com_error(0x80042005, "hrPageDoesNotExist")
        settings = root.find(q("PageSettings"))
        if settings is not None and self.reject_hex and settings.get("color", "").startswith("#"):
            raise com_error(0x80042001, "hrInvalidXML: color")
        for child in root:
            if child.tag not in {q("Meta"), q("PageSettings"), q("Title"), q("Outline"), q("Image"), q("QuickStyleDef"), q("TagDef")}:
                raise com_error(0x80042001, f"hrInvalidXML: unexpected {child.tag}")
        page = self.pages[pid]
        doc = page["doc"]
        if doc is None:
            doc = ET.Element(q("Page"), {"ID": pid, "lang": "en-US"})
            page["doc"] = doc
        if root.get("dateTime"):
            page["dateTime"] = root.get("dateTime")
            doc.set("dateTime", root.get("dateTime"))
        for meta in root.findall(q("Meta")):
            for old in doc.findall(q("Meta")):
                if old.get("name") == meta.get("name"):
                    doc.remove(old)
            doc.insert(0, meta)
        if settings is not None:
            for old in doc.findall(q("PageSettings")):
                doc.remove(old)
            doc.insert(len(doc.findall(q("Meta"))), settings)
        title = root.find(q("Title"))
        if title is not None:
            for old in doc.findall(q("Title")):
                doc.remove(old)
            doc.append(title)
            page["name"] = "".join(t.text or "" for t in title.iter(q("T")))
        for outline in root.findall(q("Outline")) + root.findall(q("Image")):
            oid = outline.get("objectID")
            if oid:
                for old in list(doc):
                    if old.get("objectID") == oid:
                        doc.remove(old)
            else:
                outline.set("objectID", self._id("E1"))
            pos = outline.find(q("Position"))
            if pos is not None and float(pos.get("y", "0")) < 36:
                pos.set("y", "36.0")  # OneNote keeps outlines below the title area
            for data in outline.iter(q("Data")):
                data.set("fakeLen", str(len(data.text or "")))
                data.text = None
            doc.append(outline)

    def GetPageContent(self, page_id, info=0, schema=1):
        if page_id not in self.pages:
            raise com_error(0x80042005, "hrPageDoesNotExist")
        page = self.pages[page_id]
        doc = page["doc"]
        if doc is None:
            doc = ET.Element(q("Page"), {"ID": page_id, "lang": "en-US"})
        import copy

        out = copy.deepcopy(doc)
        out.set("lastModifiedTime", "2026-10-20T10:00:00.000Z")
        for data in list(out.iter(q("Data"))):
            if info in (0, 2, 4, 6):
                data.tag = q("CallbackID")
                data.set("callbackID", "{CB}")
                data.attrib.pop("fakeLen", None)
            else:
                data.text = "QUJD" * int(data.attrib.pop("fakeLen", "1") or 1)
        if self.ink:
            outline = ET.SubElement(out, q("Outline"), {"objectID": self._id("E1")})
            ET.SubElement(outline, q("Position"), {"x": "300", "y": "336", "z": "2"})
            children = ET.SubElement(outline, q("OEChildren"))
            oe = ET.SubElement(children, q("OE"), {"objectID": self._id("E1")})
            para = ET.SubElement(oe, q("InkParagraph"), {"objectID": self._id("E1")})
            word = ET.SubElement(para, q("InkWord"), {"objectID": self._id("E1"), "recognizedText": "hello", "inkOriginX": "300", "inkOriginY": "336", "width": "40", "height": "14"})
            ET.SubElement(word, q("CallbackID"), {"callbackID": "{INK}"})
        return self._xml(out)

    def GetHyperlinkToObject(self, hierarchy_id, content_id=""):
        if hierarchy_id in self.pages:
            p = self.pages[hierarchy_id]
            return f"onenote:#{p['name'].replace(' ', '%20')}&section-id={p['section']}&page-id={hierarchy_id}&end"
        if hierarchy_id in self.sections:
            return f"onenote:#section-id={hierarchy_id}&end"
        raise com_error(0x80042017, "hrObjectDoesNotExist")

    def SyncHierarchy(self, hierarchy_id):
        self.calls.append(("SyncHierarchy", hierarchy_id))

    def NavigateTo(self, hierarchy_id, object_id="", new_window=False):
        self.calls.append(("NavigateTo", hierarchy_id))

    def DeleteHierarchy(self, object_id, date=None, permanently=False):
        if object_id in self.pages:
            sid = self.pages[object_id]["section"]
            self.sections[sid]["pages"].remove(object_id)
            del self.pages[object_id]
        else:
            raise com_error(0x80042017, "hrObjectDoesNotExist")

    def CloseNotebook(self, notebook_id, force=False):
        self.calls.append(("CloseNotebook", notebook_id))

    def GetSpecialLocation(self, which):
        return "C:\\Users\\owner\\Documents\\OneNote Notebooks"


_APP = FakeOneNote()


def Dispatch(progid):
    assert progid == "OneNote.Application", progid
    return _APP
