# Final acceptance (M7.4)

Status of every item in the PRD's "Acceptance tests" section, plus the --previous check and the performance targets.
Automated items were run in the build environment (Linux, no OneNote, the real planner PDF at docs/SFP No Dates.pdf,
the bundled TeX Gyre Bonum fallback font). Manual items need the owner's Windows PC with OneNote desktop and the iPad;
they are listed unticked with the exact step to perform. A failed or deferred item becomes a card before release.

## Automated (run on every build, no Office needed)

| PRD item | Evidence | Status |
| --- | --- | --- |
| Date engine: Q1 2027 produces 14 weeks and 90 daily pages, week 1 Fri Jan 1 to Sun Jan 3, week 14 Mon Mar 29 to Wed Mar 31 | tests/Sfp.Core.Tests/Dates/DateEngineQuarterTests.cs (Q1_2027_runs_from_Jan_1_to_Mar_31_with_90_days_and_14_weeks, Q1_2027_week_1_holds_Jan_1_to_3_..., Q1_2027_week_2_is_the_full_week_...), tests/Sfp.Core.Tests/Plan/PlanBuilderTests.cs and the plan snapshot (19 sections, 144 pages) | pass |
| Q4 2026: 14 weeks with Dec 28 to 31 in week 14; Jan 1 to 3 greyed in December's grid | DateEngineQuarterTests.Q4_2026_has_14_weeks_and_week_14_holds_Dec_28_to_31; DateEngineGridTests (December 2026 row 5 InQuarter false); tests/Sfp.Render.Tests/CalendarPageTests.cs | pass |
| Q2 2024 (Apr 1 a Monday, 91 days) produces exactly 13 weeks | DateEngineQuarterTests.Q2_2024_starts_on_a_Monday_and_has_13_weeks; PlanBuilderTests.Q2_2024_has_18_sections_13_weeks_and_91_day_pages | pass |
| Q1 2024 (leap year) produces 91 daily pages | DateEngineQuarterTests.Q1_2024_leap_year_has_91_days_and_the_day_pages_total_91 | pass |
| November 2026 needs 6 rows and stacks 30 in the 23 box | tests/Sfp.Core.Tests/Dates/DateEngineGridTests.cs (November_2026_starts_on_a_Sunday_and_stacks_30_on_the_23_cell); CalendarPageTests (stacked box reads "23 / 30") | pass |
| Off-cycle start February 2027 labelled "Feb to Apr 2027" | DateEngineQuarterTests, PlannerTextTests | pass |
| Render golden images: every PNG of a fixed-date render-only matches the stored hash | tests/Sfp.Render.Tests/GoldenSuiteTests.cs: synthetic.linux-x64.sha256 and sfp.linux-x64.sha256 (real PDF) both match; a second render gives identical hashes | pass on linux-x64; win-x64 goldens are recorded on the owner's PC with SFP_RECORD_GOLDENS=1 (Bookman Old Style changes the glyphs) |
| Overlay placement by OCR: Fri Jan 1's date region reads "01 / 01 / 27"; the strip shows 14 numbers with the circle on 14 | tests/Sfp.Render.Tests/DateAndStripTests.cs (date fields OCR with links off, strip digit count and circle position by pixels), TimeBlockColumnTests (16 labels 6 to 9), CalendarPageTests ("January 2027", greyed 28, blank Jan 4), through python3 tesserocr | pass |
| Manifest validation: 131 pages, wrong page size, missing probe each fail with the right message | tests/Sfp.Core.Tests/Manifests/ManifestValidatorTests.cs; the real PDF passes validation (Real_planner_pdf_passes_validation) | pass |
| XML builder: generated page XML validates against the 2013 XSD | tests/Sfp.OneNote.Tests/Xml/PageXmlBuilderTests.cs (Mon_Jan_4_page_validates_against_the_2013_schema), SchemaStrictnessTests, PageXmlSnapshotTests | pass against the reconstructed schema subset in tests/Sfp.OneNote.Tests/schema; rerun with the official 0336.OneNoteApplication_2013.xsd on the owner's PC |

Whole suite in the build environment: `dotnet test --filter "Category!=COM"` = 1186 passed, 0 failed, 0 skipped (the real PDF present).
COM-category tests (ComNotebookWriterComTests, the OneNoteApp integration test, SidecarRecovery, Outlook) skip here and run on the owner's PC.

## Performance and size (M7.2, measured here)

| Target (PRD) | Measured | Status |
| --- | --- | --- |
| Rendering under 60 s | render-only, 144 pages of Q1 2027 from the real PDF at 220 DPI: 21.4 s inside the verb (25.5 s wall with runtime start), 4 cores | pass |
| Notebook under 60 MB at 220 DPI | 142 PNGs total 20 MB (the sheet images are the notebook's bulk) | pass |
| Full generate under 3 minutes | not measurable without OneNote; the writer phase is one STA thread at 0.3 to 0.8 s per UpdatePageContent per the PRD, 144 pages gives 45 s to 2 min plus 21 s render overlapped | owner's PC |
| Memory under 1.5 GB peak | render-only peak RSS 2.34 GB (polled VmHWM) | fail: open item for the implementer (rasterizer cache size, bitmap disposal, parallel degree); re-measure after the fix |
| Determinism: byte-identical PNGs | GoldenSuiteTests.A_second_full_render_gives_identical_hashes | pass |

## Manual on Windows (once per release)

Run on the owner's PC with OneNote desktop signed in. Tick each box and note the date.

- [ ] Generate SFP Q1 2027 from the reference PDF; run completes under 3 minutes with no warnings.
- [ ] OneNote opens on the cover page; tab row reads Quarter, Objectives, Calendar, Week 01 ... Week 14 in order.
- [ ] Week 01 page list shows Weekly Overview, then three indented day pages Fri Jan 1 to Sun Jan 3, then Weekly Reflection; Week 02 shows all seven, Mon Jan 4 to Sun Jan 10.
- [ ] On Mon Jan 4: clicking the picture does not select it; Ctrl+A then Delete leaves the picture in place; typing at a click point creates text there.
- [ ] Dates read 01 / 04 / 27 on Mon Jan 4 and 01 / 10 / 27 on Week 02's Weekly Reflection; Mon Jan 4's strip shows 14 numbers with the circle on 13.
- [ ] Calendar section has January, February and March 2027 only; January shows Dec 28 to 31 at 40% in its first row and March shows Apr 1 to 4 at 40% in its last row.
- [ ] Contents page links jump to the right pages. On January 2027's calendar, clicking 4 opens Mon Jan 4 and clicking Week 02 opens that Weekly Overview; on Mon Jan 4 clicking the date opens Week 02's Weekly Overview; on that overview clicking Tuesday opens Tue Jan 5; Annual Objectives block 3 opens Objective 3 and its back link returns.
- [ ] Zoom to 100%: the page is 15.4 in wide and the 7 pt hour labels are legible (the tool redraws the hour labels at 9 pt).
- [ ] Move Notebook to OneDrive succeeds and the sync icon clears within 5 minutes.
- [ ] Type two objective titles and one standing goal on Setup, ink a third objective, run Preview: the inked value is flagged; run Refresh: the titles appear on Annual Objectives, in the legend on Mon Jan 4's page and on the scorecard, and the standing goal appears at 60% on every daily page's first goal line.
- [ ] Write a note on Mon Jan 4 over the legend band, run Refresh again: the note is untouched and the summary reports the skipped slot.
- [ ] Add an .ics source with a 9:00 meeting on Tue Jan 5, run Refresh: the meeting appears at the 9 row on Tue Jan 5 and in Tuesday's column on Week 02's overview; cancel it in the source, Refresh: it is gone from both.
- [ ] Enable the daily task, log out and in, confirm the task ran at 05:00 from its log.
- [ ] Generate Q2 2027 with --previous pointing at the Q1 notebook and confirm the "Next quarter" link on Q1's Quarterly Review opens the Q2 cover.

## Manual on iPad (once per release)

- [ ] Notebook appears in OneNote for iPad after sign-in; all 19 sections and 144 pages load.
- [ ] Open Wed Jan 6 in Draw mode; ink with the Apple Pencil across lines and over the date. The picture does not move, select or show handles.
- [ ] Lasso-select the ink and move it; the picture stays. A finger tap on the date link opens the Weekly Overview; a pencil stroke across it draws instead of navigating.
- [ ] Ink appears on the Windows page within 2 minutes.
- [ ] Text typed on Windows appears on the iPad in the right place relative to the lines.
- [ ] Fit-to-width in landscape: Daily Goals numbers and day name are readable without zoom; hour labels are readable at 150% zoom.

## Known risks carried into release

- The M0 spike has not run (docs/spike-findings.md): title offset 36 pt, hex page color, anchor padding 0, link styling and page dateTime are assumptions; the writer falls back on rejection, and tools/spike/m0_9_apply.py writes the measured values into the manifest.
- No COM call has executed against real OneNote; the late-bound invocation shapes (out parameters, DateTime.MinValue, enum arguments) are the first thing to confirm with `dotnet test --filter Category=COM`.
- Goldens, the 2013 schema subset and the Bookman Old Style face are verified only with the fallback font on Linux.
