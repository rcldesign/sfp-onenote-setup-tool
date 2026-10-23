# M0 spike findings

The M0 spike proves the OneNote behaviors the whole design rests on. It needs the owner's Windows PC with OneNote
desktop (Office 2024) and the iPad, which the build environment does not have. The scripts are under tools/spike;
run them in order and fill in each finding below. Until then, the manifest holds the assumed values listed in
docs/decisions.md.

How to run: see tools/spike/README.md. Each script prints its findings as a block that can be pasted here.

How the scripts report: every script ends by printing its findings twice, as a JSON block and as a Markdown block
headed like the matching section below, and writes both to tools/spike/scratch/m0_N-findings.json and .md while
merging the JSON into tools/spike/scratch/findings.json (schema at the top of tools/spike/m0_9_apply.py). Paste the
Markdown block over the matching section here, replacing its _pending_ lines; keep the screenshots named in the
block under tools/spike/scratch. Set the accepted values under "decisions" in findings.json (or accept the scripts'
proposals) and run m0_9_apply.py to write them into the manifest's onenote block. M0.7 has no script; its checklist
is tools/spike/m0_7_ipad.md, and M0.8 reads the inked page back afterwards.

## M0.1 COM reachability

- OneNote version string: _pending_
- 2013 schema namespace accepted by GetHierarchy: _pending_
- Open notebooks listed: _pending_
- Suspected on the owner's PC (fix round 5b, unconfirmed): late binding (this tool's dynamic calls, pywin32's Dispatch) and early binding (pywin32's EnsureDispatch, interop assemblies) both depend on OneNote's registered type library, `HKEY_CLASSES_ROOT\TypeLib\{0EA692EE-BB50-4E3C-AEF0-356D91732725}\1.1\0\win64` or `\win32`. When that registration points at a file that does not exist, the evidence fits OneNote starting, then failing every call; pywin32's EnsureDispatch failing with "This COM object can not automate the makepy process" (and late-bound pywin32 getting AttributeError for GetHierarchy) is the sign of a broken registration. The Generate preflight checks it before starting OneNote (docs/decisions.md, fix round 5b).

## M0.2 Background image page

- Offset at which the sheet sits flush under the title: _pending_ (tried 0, 36, 48)
- Does y = 0 overlap the title: _pending_
- PageSettings color="#DED4C8": accepted / rejected: _pending_
- Screenshots: tools/spike/scratch/m0-2-*.png

## M0.3 Subpage levels

- Page list shows the indentation and order: _pending_
- Pages must exist before levels are set: _pending_

## M0.4 Link anchors

- span color honored: _pending_
- text-decoration:none honored: _pending_
- Internal padding offset to add to the manifest (x, y): _pending_
- Links navigate on click: _pending_

## M0.5 Hotspot alternative

- Transparent image with hyperlink attribute: click follows on Windows: _pending_
- Visible or selectable: _pending_
- Decision: text anchors or hotspots: _pending_ (text anchors assumed)

## M0.6 Page metadata

- one:Page dateTime persisted after sync and reopen: _pending_
- one:Meta sfp.key persisted: _pending_
- one:Section color persisted: _pending_

## M0.7 iPad check

- Background image locked under pencil ink: _pending_
- Links navigate on tap: _pending_
- Pencil stroke over a link draws instead of navigating: _pending_
- Image renders at the expected size: _pending_

## M0.8 Ink read-back

- one:InkWord element and recognizedText as returned: _pending_

## M0.9 Manifest offsets

- Applied to templates/sfp-quarterly.json: _pending_

## Added by the wave 3 adversarial review (run with M0 on the owner's PC)

- Outline Meta round trip: generate a notebook, read Mon Jan 4 back with GetPageContent and confirm each tool outline still carries `<one:Meta name="sfp.anchor" content="...">` (the official 2013 schema allows Meta on Outline and OE; OneMore relies on it). Result: _pending_
- Custom page size: with "Custom page size" ticked, confirm OneNote accepts Orientation, Dimensions in points (1112 x 770) and Margins of 0, and whether landscape needs the paper order (770 x 1112) instead. Result: _pending_
- Empty one:Data: confirm the HRESULT OneNote returns for an image element with no data (the fake models hrInsertingImage). Result: _pending_
- dateExpectedLastModified: confirm an UpdatePageContent with the page's lastModifiedTime (UTC converted to local) succeeds on an unchanged page and is refused after a user edit. Result: _pending_
- Next quarter link after the move: after both notebooks move to OneDrive, the "Next quarter" link captured while Q2 was local must still open Q2's cover on Windows and the iPad. Result: _pending_

## Exit gate

M0.2, M0.3 and M0.7 must pass. If M0.7 fails on the locked image, stop and report to the owner before M1.
