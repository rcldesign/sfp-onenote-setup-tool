# Decisions log

Judgment calls made while building sfp2onenote. The PRD's "Decisions made" list is settled and is not repeated here.
Each entry names the card that raised it, the choice, and why. Where the PRD left a value open, the assumed value is here.

## M0 (spike), assumed values pending the owner's PC

The M0 spike needs OneNote desktop and an iPad, which the build environment does not have. The scripts under
tools/spike are ready to run; until they are, the manifest carries these assumed values, all in the `onenote` block
of templates/sfp-quarterly.json so a spike result is a manifest edit.

| Question | Assumed | Where |
| --- | --- | --- |
| titleOffset for a titled page | 36 pt | onenote.titleOffsetPt |
| PageSettings color accepts a hex value | yes, "#DED4C8"; the writer falls back to "automatic" on COM error 0x80042001 | onenote.pageColor |
| Anchor internal padding | 0, 0 | onenote.anchorPaddingPt |
| Hyperlink span color and text-decoration honored | assumed yes; underline accepted if not | onenote.anchorUnderline |
| one:Page dateTime accepted by UpdatePageContent | assumed yes; dropped on rejection, per the PRD | GenerateOptions.SetPageDates |
| one:Meta sfp.key persists after sync | assumed yes (Meta is a documented 2013 element) | writer |
| one:Section color persists | assumed yes | writer |
| Transparent-image hotspots (M0.5) | not used; text anchors stand until the spike says otherwise | links block |

## Foundation

- **Build on Linux.** The solution builds and the non-COM tests run on Linux and Windows. WPF compiles anywhere with
  EnableWindowsTargeting; COM is late-bound through `dynamic` so Sfp.OneNote compiles without the PIA. COM tests carry
  `[Trait("Category", "COM")]` and skip off Windows.
- **Package pins.** PDFtoImage 5.4.0 ships PDFium for win, linux and macOS and pins SkiaSharp 4.150.1; SkiaSharp is pinned
  to the same version because a newer native libSkiaSharp is rejected at load. System.CommandLine 2.0.13 (the stable
  line) rather than the 3.0 pre-release. FluentAssertions 7.2.2 (Apache 2.0; 8.x needs a paid license for some uses).
- **OCR in tests.** Tesseract is not a NuGet-only dependency on Linux, so the OCR checks use whatever engine is present:
  the `tesseract` CLI, the Python `tesserocr` wheel (used in the build environment), or the Tesseract NuGet on Windows.
  Pixel-based assertions run everywhere; OCR tests skip with a message when no engine is found. TESSDATA_PREFIX points
  at a folder holding eng.traineddata.
- **Central package management** (Directory.Packages.props) so every project pins the same versions.
- **Calendar pages map one to one.** The PRD manifest sample lists `"calendar": {"pdfPages": [13]}` but the page map
  says PDF pages 13 to 15 are the three calendars. The manifest uses [13, 14, 15], month i rendered from page 13 + i.
- **Label of an off-cycle quarter that crosses a year.** "{Qn} {YYYY}" is the label everywhere, with {Qn} = "Dec to Feb"
  and {YYYY} = the start year, so a December 2026 start is "SFP Dec to Feb 2026". Consistent with the PRD's "Feb to Apr 2027".
- **Notes page title token.** The Notes section uses `{Note}` (the configured page name) as its title token. It is the
  only addition to the PRD token set and is valid only on a page with eachNote.
- **Page keys.** Fixed forms are documented on `PageKey`: cover, contents, setup, annual:1, annual:2, objectives,
  objective:n, calendar:YYYY-MM, week:NN:overview, day:YYYY-MM-DD, week:NN:reflection, scorecard, review:YYYY-MM,
  review:quarter, notes:name.
- **Title band layout on daily pages.** The band between the day name (ends y 47) and the section labels (start y 63)
  holds, left to right: prev link (x 23), next link (x 122), objective legend (x 230 to 870), all-day events (x 560 to 860,
  shares the legend's right half), day counter (right edge x 1000). The PRD gives the legend 1040 pt; it is narrowed to
  640 pt so the navigation links and the counter fit on the same line. Objective titles are truncated to fit.
- **Estimated geometry to calibrate.** The PRD measures the daily page, the calendar and the date slots. These slots
  are estimates placed from the page description and must be checked with `sfp-measure` against the real PDF:
  standingGoals rows, metricNames cells, overviewDayColumns, reflectionNext, progressScorecard,
  reflectionQuarterReview, annualObjective erase rectangles, overviewDayHeader erase band, and every generated page
  (Setup, Scorecard, Notes, Reviews) layout. Each is a manifest edit.
- **Section tints.** Three beige tints (#F0E6D8, #E4D6C4, #D8C8B2) cycle by month for the week sections. OneNote may
  snap a hex color to its palette; accepted.
- **Sixth-row stacking** writes "23 / 30" in the fifth-row box, and the stacked day gets no link anchor (one anchor per
  box); the Monday "Week NN" label on that row still links to week 5's overview.
