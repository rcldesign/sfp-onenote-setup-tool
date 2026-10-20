# M0.7 iPad check (manual)

No script: this card is done with the iPad in hand. It proves the gate the whole design rests on: a locked
background image that the Apple Pencil inks over, with link anchors that a finger tap follows. Do it after
m0_2_background.py, m0_4_anchors.py and m0_5_hotspot.py have run, so the page "M0.2 background y=36" holds the
test sheet, three text anchors, two variants and the transparent hotspot.

## 1. Move the scratch notebook to OneDrive

1. In OneNote on the PC: File, Share, choose OneDrive, Move Notebook (the "SFP Spike" notebook).
2. Wait until the sync icon on the notebook is quiet (File, Info shows the OneDrive path).
3. On the iPad, open OneNote, sign in with the same account, open "SFP Spike", open the page "M0.2 background y=36".
   Let the page finish loading (the sheet is a 3398 x 2353 px PNG).

## 2. Checks

Record every result in the table; screenshots go to tools/spike/scratch/m0_7-*.png (AirDrop or OneDrive them over).

| # | Check | How | Result (pass / fail, notes) |
| --- | --- | --- | --- |
| 1 | Background image locked under pencil ink | With the Apple Pencil, draw a stroke across the grid. Then, with a finger, tap and long-press the sheet, and try to drag it. The ink must land on the page and the sheet must neither select (no handles) nor move. | |
| 2 | Links navigate on tap | With a finger, tap the "4" (calendar number anchor), the "01 / 04 / 27" date and "Monday". Each must open the page "M0.4 link target". Use Back (or the page list) to return. | |
| 3 | Pencil stroke over a link draws instead of navigating | In Draw mode, draw a stroke straight across "Monday". The stroke must appear and the page must stay. | |
| 4 | Image renders at the expected size | At 100 % zoom (pinch until the grid labels read at the same size as the page title), the labels "1100" (top edge) and "750" (left edge) are near the right and bottom edges and nothing is cropped. Note the label nearest each edge. Scroll to confirm the page is no wider than the sheet. | |
| 5 | Hotspot (M0.5) on iPad | Tap the baked "4" in the first calendar box (top-left day box of the grid). Does "M0.5 hotspot target" open? Does anything show at the spot (border, tint, selection)? | |
| 6 | Underline and color on iPad | Are the three anchors ink-colored and free of an underline, as on Windows? | |
| 7 | Title and sheet offset on iPad | Is the gap between the page title and the sheet's top edge the same as on Windows (compare screenshots at 100 %)? | |

## 3. Ink a word for M0.8

1. On the same page, in Draw mode, write the word "hello" with the Pencil in a clear cell (for example the cell
   labelled "300,300").
2. Wait until the iPad shows the sync as done, then on the PC run: python m0_8_ink.py --sync

## 4. Record

Paste the table into docs/spike-findings.md under M0.7 as bullets (background locked, links navigate, pencil over a
link draws, image size), and add a short JSON note to tools/spike/scratch/findings.json under cards.m0_7 if you want
the file complete, for example:

    "m0_7": { "backgroundLocked": true, "linksNavigate": true, "pencilOverLinkDraws": true, "sizeAsExpected": true, "hotspotTapFollows": false }

Exit gate: if check 1 fails (the sheet moves or selects under the Pencil), stop and report before M1.
