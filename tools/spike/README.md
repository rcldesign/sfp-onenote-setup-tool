# M0 spike scripts

Python scripts (pywin32) that answer the OneNote questions in the development plan's M0 table, one per card, plus
a shared module. Nothing here ships; the findings go into docs/spike-findings.md and, through m0_9_apply.py, into
the `onenote` block of templates/sfp-quarterly.json. Every measured value the scripts use (page size, colors, font,
anchor rectangles) is read from that manifest, never typed into a script.

## Requirements

- A Windows PC with OneNote desktop 16.x (Office 2016 or later, perpetual or Microsoft 365) installed and signed in
  with the Microsoft account that will own the notebook. OneNote for Windows 10 has no COM API and does not count.
- Python 3.11 or later, then `pip install pywin32 pillow` (Pillow 10.1 or later).
- The iPad with OneNote and an Apple Pencil for M0.7 and M0.8.
- Run from this folder (`cd tools\spike`) with OneNote open. The scripts talk to the running instance.

The scratch notebook "SFP Spike" is created under `%USERPROFILE%\Documents\OneNote Notebooks\SFP Spike`, never
inside OneDrive (OneNote refuses local notebooks there; the scripts refuse the path). Override the parent folder
with `--notebooks-folder`. Pages are recreated on every run, so a script can be repeated.

## Run order

| Step | Command | What it proves |
| --- | --- | --- |
| M0.1 | `python m0_1_com.py` | OneNote.Application resolves; version; open notebooks; the 2013 namespace comes back from GetHierarchy |
| M0.2 | `python m0_2_background.py` | The test sheet as a locked background at y = 0, 36 and 48; hex PageSettings color accepted or rejected; the outline position OneNote kept |
| M0.3 | `python m0_3_levels.py` | UpdateHierarchy sets pageLevel 1, 2, 2 and the order; whether pages must exist first |
| M0.4 | `python m0_4_anchors.py` | Three styled link anchors on the M0.2 page; which span attributes OneNote keeps; the padding offset |
| M0.5 | `python m0_5_hotspot.py` | A transparent image with a hyperlink attribute over a baked number |
| M0.6 | `python m0_6_metadata.py [--reopen]` | one:Page dateTime, one:Meta sfp.key and one:Section color after a sync |
| M0.7 | follow `m0_7_ipad.md` | Move the notebook to OneDrive and check it on the iPad (the exit gate) |
| M0.8 | `python m0_8_ink.py --sync` | After inking on the iPad: every one:InkWord and its recognizedText; saves the M4.1 fixture |
| M0.9 | `python m0_9_apply.py --dry-run`, then without `--dry-run` | Writes the decided values into the manifest's onenote block |

Every script accepts `--no-prompt` (never asks questions on the console), `--manifest`, `--scratch`, `--dpi` and
`--grid` (test sheet), `--regenerate` (redraw the test sheet) and `--help`.

## Where things go

- `tools/spike/scratch/` (gitignored): the test sheet `test-sheet.png`, every page XML read back
  (`m0_2-y36.xml`, `m0_4-page.xml`, `m0_8-page.xml` and so on), each card's findings as `m0_N-findings.json` and
  `m0_N-findings.md`, and the merged `findings.json`.
- Screenshots: save them in the same folder as `m0_2-y0.png`, `m0_2-y36.png`, `m0_2-y48.png`, `m0_4-anchors.png`,
  `m0_7-*.png`. Take them at 100 % zoom. The test sheet carries a 1 pt grid every 50 pt labelled with its
  coordinates (sheet origin top left), frames around the anchor rectangles and a baked "4" in the first calendar box,
  so offsets can be read straight off a screenshot: the gap between the page title and the sheet's top edge is the
  title offset; the inset of a link's first glyph from its frame corner is the anchor padding.

## How the scripts report and how to paste findings

Each script ends by printing its findings twice: a JSON block (also merged into `scratch/findings.json`, schema at
the top of `m0_9_apply.py`) and a Markdown block headed like the matching section of docs/spike-findings.md. Paste
the Markdown block over that section, replacing its `_pending_` lines. Where a script could only measure part of an
answer (the flush title offset, the padding, whether a link navigated) it asks on the console; answer `y`, `n`, a
value, or press Enter to skip and fill the line in by hand later.

Decisions for the manifest live under `"decisions"` in `scratch/findings.json`: `titleOffsetPt`, `pageColor`,
`anchorPaddingPt` and `anchorUnderline`. m0_2 and m0_4 write their measurements under `"proposed"`;
`python m0_9_apply.py --accept-proposals` takes those where you have not decided, and `--set KEY=VALUE` sets one
from the command line, for example `python m0_9_apply.py --set titleOffsetPt=36 --set pageColor=automatic --set "anchorPaddingPt={\"x\":2,\"y\":1}" --set anchorUnderline=false`.
Run with `--dry-run` first to see the diff; the patch touches only those four values and leaves every other byte of
the manifest as it is. Then record the M0 section in docs/decisions.md and commit the manifest.

## Self-check without OneNote

`python spike_common.py --selfcheck` draws the test sheet into a temp folder with Pillow, prints its size
(3398 x 2353 px for 1112 x 770 pt at 220 DPI) and checks the XML builders; `python m0_9_apply.py --selfcheck`
patches a temp copy of the manifest. Both run on any platform and are what the build environment exercises; the
other scripts stop with a message when pywin32 is absent.

`python smoke_fake.py` (and `python smoke_fake.py fallback`) runs every script end to end against the fake
OneNote in `fake/`, a small in-memory stand-in for pywin32 and OneNote.Application. It proves the scripts execute
and report, with the fallback paths (late binding, hex color rejected, partial updates, direct image, binary ink
read) covered by the second mode. It says nothing about what real OneNote does; only the PC run does.

## If something goes wrong

- "call rejected" (RPC_E_CALL_REJECTED 0x80010001 or 0x8001010A): OneNote was busy; every call is retried once after
  2 s. If it keeps happening, close dialogs in OneNote and run again.
- hrInvalidXML (0x80042001) or hrMalformedXML (0x80042000): OneNote rejected an element or attribute. The scripts
  record this as the finding (for example the hex page color) and fall back where the PRD says to.
- "pywin32 returned no [out] value": generate the wrappers once with
  `python -m win32com.client.makepy "OneNote.Application"` and run again.
- A page is missing ("run m0_2_background.py first"): the later scripts add to the M0.2 page "M0.2 background y=36";
  pass `--page` to use another one.
- Delete the scratch notebook when the spike is over: close it in OneNote, then remove the folder.
