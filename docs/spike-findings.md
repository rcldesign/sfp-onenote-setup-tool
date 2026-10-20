# M0 spike findings

The M0 spike proves the OneNote behaviors the whole design rests on. It needs the owner's Windows PC with OneNote
desktop (Office 2024) and the iPad, which the build environment does not have. The scripts are under tools/spike;
run them in order and fill in each finding below. Until then, the manifest holds the assumed values listed in
docs/decisions.md.

How to run: see tools/spike/README.md. Each script prints its findings as a block that can be pasted here.

## M0.1 COM reachability

- OneNote version string: _pending_
- 2013 schema namespace accepted by GetHierarchy: _pending_
- Open notebooks listed: _pending_

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

## Exit gate

M0.2, M0.3 and M0.7 must pass. If M0.7 fails on the locked image, stop and report to the owner before M1.
