# Reviewer screenshot set

The renderer (M2.4 to M2.10) writes every page of a notebook as a PNG so the owner can eyeball the overlays once
before a notebook is generated, and so the overlay positions in `templates/sfp-quarterly.json` can be tuned without
OneNote. The PNGs themselves are not committed (`docs/renders/*.png` is in `.gitignore`); this page says how to
produce them and what to look at.

## Producing the set

From the repository root, with the planner PDF at hand:

```
sfp2onenote render-only --pdf "docs/SFP No Dates.pdf" --start 2027-01 --out docs/renders
```

or, from a source checkout:

```
dotnet run --project src/Sfp.Cli -- render-only --pdf "docs/SFP No Dates.pdf" --start 2027-01 --out docs/renders
```

The verb validates the PDF against the manifest (exit code 2 with every mismatch listed when it does not fit), builds
the plan, and writes one PNG per page with an image plus `pages.json`. It takes a few seconds on a four-core machine,
rendering at most four pages at once with at most 256 MB of rasterized pages cached, so a Q1 2027 run peaks under
1 GB at 220 and at 300 DPI. A page that fails to render stops the run with exit code 4 and `Render failed:` and the
reason on stderr; no `pages.json` is written. Options:

| Option | Meaning | Default |
| --- | --- | --- |
| `--pdf`, `--start`, `--out` | the planner PDF, the quarter start month as YYYY-MM, the output folder | required |
| `--dpi` | 144, 220 or 300 | 220 |
| `--date-format` | `mm/dd/yy`, `dd/mm/yy`, `m/d/yy` or `d/m/yy` | `mm/dd/yy` |
| `--weeks-remaining` | `corrected` (week k circles N - k + 1) or `as-printed` (circles k) | `corrected` |
| `--no-links` | bake every date and number; emit no link anchors | off |
| `--time-blocks` | the Time Blocks window as START-END in 24-hour hours | `6-22` |
| `--include-out-of-quarter-days` | add the days of week 1 and week N outside the quarter, dated at 40% ink | off |
| `--manifest` | a user manifest merged over the shipped one | none |
| `--templates` | the templates folder | beside the executable, else above it |
| `--quiet` | print nothing but errors | off |

A log of the run goes to `%LOCALAPPDATA%\sfp2onenote\logs` (or `SFP2ONENOTE_LOGDIR`).

The render tests can also dump what they render: set `SFP_RENDER_DEBUG_DIR` to a folder before running
`dotnet test tests/Sfp.Render.Tests` and every page a test renders lands there, named `<key>.<dpi>.png`, with every
OCR crop under `crops/` named by its region and what Tesseract read. Set `SFP_RENDER_OUT` to a folder to keep the
render-only verb test's full output instead of a deleted temp folder.

## Files

- `<key>.png`: the page key with every `:` replaced by `-`, for example `day-2027-01-04.png`,
  `week-02-overview.png`, `calendar-2027-01.png`, `review-quarter.png`, `notes-backlog.png`.
- `pages.json`: the notebook name, quarter label and DPI, then one entry per page in notebook order with `key`,
  `title`, `section`, `level`, `pdfPage` (null for a generated page), `png` (the file name, or null for a page without
  an image), `width` and `height` in pixels, and `anchors`: every link and propagated slot the writer will place over
  the sheet, in PDF points from the sheet's top-left corner (`anchorId`, `kind`, `x`, `y`, `w`, `h`, `text`,
  `align`, `sizePt`, `opacity`).

Two pages have no image and no PNG: `contents` (a native OneNote page of links, built by the writer) and
`objectives` (the empty "Objective Details" parent that holds the eight subpages). They are listed in `pages.json`
with `png: null` and a 0 x 0 size. A Q1 2027 run therefore writes 142 PNGs for 144 pages.

## What to look at

Nothing drawn in the bitmap is editable later, and nothing that becomes a link is drawn at all (the writer places a
native text object there), so an empty spot on a PNG is usually a link slot. Check these pages first:

- `day-2027-01-04.png` (Mon Jan 4, week 2): the date slashes at the top right are erased (the date is a link); the
  strip reads 14 down to 1 with 13 circled and the same air between every pair of neighbors, so "14 13 12 11 10"
  read apart (two-digit numbers take a wider pitch than one-digit ones, as on the printed 13-number strip); the
  white 13 sits in the middle of its 13.4 pt disc, clear of the rim (the disc is centered on the number's ink);
  "Day 4 of 90 · Week 2 of 14" sits under the date at 60% ink; the Time Blocks column is rebuilt for the window with
  9 pt labels on every hour rule, the closing one included (6, 7 ... 12, 1 ... 10 for 06:00 to 22:00).
- `week-02-overview.png`: the date is baked (01 / 04 / 27, the Monday); the seven day-name header cells are erased
  (they become links); the column rules between them survive.
- `week-14-reflection.png`: the Sunday date 04 / 04 / 27 is baked; the printed "Progress %" is erased (it becomes
  the link to the scorecard).
- `calendar-2027-01.png`: "January 2027" above the rule at the top right; Dec 28 to 31 greyed in row 1; every
  January box empty (links); the box of a stacked sixth-row day (November 2026 in a Q4 notebook) shows " / 30" baked
  beside the empty spot for the linked 23.
- `cover.png`: "Q1 2027" and "Jan 1, 2027 to Mar 31, 2027" centered under the logo.
- `setup.png`: the heading, six labels, numbered ruled slots, the window boxes with hint digits at 40% ink, and the
  hint lines.
- `scorecard.png`: eight ruled rows, 14 (or 13) week columns whose headers are link slots, and the Quarter column
  right after the last week column (one rule between them, no blank column for a 13-week quarter).
- With `--no-links` the text that would be a link is baked where the page needs it to read: the daily date, the
  calendar day numbers, the scorecard's week numbers 01 to 14 in the header cells, "Objective n" in each Annual
  Objectives block's label cell (so `annual-1.png` names objectives 1 to 4 and `annual-2.png` 5 to 8), and
  "Objective n" beside each Objective Details heading; the overview keeps its printed day names. Pure navigation
  (previous and next, the calendar's "Week NN" labels, the review, scorecard and contents links) is left out.
- `review-2027-01.png` and `review-quarter.png`: the week-13 reflection art retitled "January Review" and
  "Q1 2027 Review", the empty date slot erased; the quarterly review also has "Progress %" erased for its scorecard
  link.
- `notes-backlog.png`: a ruled sheet titled Backlog.

The synthetic template (`tests/Sfp.TestSupport/SyntheticTemplate.cs`) stands in for the PDF in the test suite. It
draws the planner's geometry in Times-Roman, so a render of it shows the overlays in the right places but not the
printed typography; use the real PDF for the set the owner reviews.

## Golden hashes

`tests/Sfp.Render.Tests/golden/<fixture>.<rid>.sha256` pins one SHA-256 per page for the synthetic template and, when
`SFP_PDF_PATH` points at the real planner, for it too. Set `SFP_RECORD_GOLDENS=1` and run the render tests to
re-record after a deliberate change to the art; see `tests/Sfp.Render.Tests/golden/README.md`.
