# SFP to OneNote Development Plan

Oct 20, 2026 · @Topher Williams

This plan turns the [SFP to OneNote Converter PRD](https://claude.ai/code/artifact/2c3b7482-ccb3-4127-ba53-8f97863a07ea) into an ordered set of milestones and task cards that an AI developer can execute one at a time, each with its inputs, files, steps and a verifiable done condition. The PRD holds the requirements and measurements; this plan holds the order of work, the code structure and the contracts between parts. Where the two disagree, the PRD wins and this plan gets corrected.

## Working rules for the AI developer

Read the PRD end to end before writing code, then this plan. Work the milestones in order and the task cards within a milestone in order; each card is sized to finish in one session and ends in a commit. Do not start a later milestone to avoid a hard task in the current one.

### How to decide

- The PRD's Decisions made list is settled. Do not reopen those choices.
- The PRD's Decisions to make list holds the assumptions still open; take the assumed value, note it in docs/decisions.md, and move on.
- Anything the PRD measures (coordinates, sizes, colors) goes into templates/sfp-quarterly.json, never into code. If a measurement proves wrong during the spike, fix the manifest and record why.
- When a OneNote behavior differs from what the PRD expects (an attribute rejected, an offset different), the spike findings file docs/spike-findings.md is the record, and the PRD gets a one-line correction in its Phase 0 section.

### When to stop and ask

Stop and ask the owner only for: a change to a settled decision; a licensing question about bundling fonts or the PDF; anything that would write outside the output folder, the log folder or %APPDATA%\\sfp2onenote; and a Phase 0 result that makes the background-image approach unworkable on the iPad. Everything else is a judgment call to make and log.

### Definition of done for a task card

- The card's done-when condition is met and demonstrated in the commit message or a test.
- dotnet build has zero warnings under the analyzers the solution enables, and dotnet test passes.
- New behavior has a unit test where the card lists one; renderer changes update the golden images deliberately with a note in the commit.
- No em dashes anywhere in code comments, docs or UI strings (owner's standing rule); use commas, colons or parentheses.
- docs/CHANGELOG.md gets one line.

### Environment

Development and testing happen on the owner's Windows PC, which has the perpetual OneNote from Office 2024 installed and the Bookman Old Style font. Milestones M1 and M2 and their tests run anywhere .NET 8 runs; M0, M3, M4, M5 and M6 need that PC. The SFP PDF is not in the repository; tests reference it by the path in SFP\_PDF\_PATH and skip with a clear message when it is absent.

## Stack and repository layout

C# on .NET 8, one solution, Windows x64 target for the app and writer, netstandard-free (everything net8.0). The Python prototype allowed by the PRD is used only in M0 and lives under tools/spike.

### Packages

| Need | Package | Notes |
| --- | --- | --- |
| PDF rasterization | PDFtoImage (bundles PDFium, returns SkiaSharp bitmaps) | pin the version; PDFium binaries ship inside |
| 2D drawing and text | SkiaSharp, SkiaSharp.NativeAssets.Win32 | overlay text, erase rectangles, generated pages |
| PDF text probes and geometry | PdfPig | manifest validation and the measure tool |
| OneNote COM | Microsoft.Office.Interop.OneNote (from the OneNote 2013 PIA, or late-bound through dynamic with Type.GetTypeFromProgID) | use the late-bound path to avoid PIA version pinning; wrap every call |
| ICS parsing | Ical.Net | calendar import |
| Outlook COM (optional) | late-bound Outlook.Application | only when Outlook is installed |
| CLI parsing | System.CommandLine | generate, refresh, render-only, measure |
| Logging | Serilog with a rolling file sink | one file per run |
| JSON | System.Text.Json | manifest, sidecar, settings |
| Tests | xUnit, FluentAssertions, Verify (snapshot tests), Tesseract (OCR checks on rendered PNGs) |  |
| UI | WPF with CommunityToolkit.Mvvm | M6 |

### Solution layout

```
sfp2onenote/
  Sfp2OneNote.sln
  Directory.Build.props          nullable on, warnings as errors, analyzers
  templates/
    sfp-quarterly.json           the manifest from the PRD, with offsets from M0
    fonts/TeXGyreBonum-*.otf     fallback typeface, GUST license file beside it
    holidays-us.json             federal holidays for the calendar import
  src/
    Sfp.Core/                    manifest model, date engine, notebook plan, tokens
    Sfp.Render/                  rasterizer, overlay engine, generated pages, fonts
    Sfp.OneNote/                 COM wrapper, page XML builder, hierarchy, hyperlinks, sidecar
    Sfp.Refresh/                 Setup reader, propagation, calendar import, scheduler registration
    Sfp.Cli/                     sfp2onenote.exe verbs: generate, refresh, render-only, measure
    Sfp.App/                     WPF window: Generate tab, Refresh tab
  tests/
    Sfp.Core.Tests/
    Sfp.Render.Tests/            golden PNG hashes under golden/, OCR checks
    Sfp.OneNote.Tests/           XML builder tests (no COM), plus [Trait("Category","COM")] integration tests
    Sfp.Refresh.Tests/
  tools/
    spike/                       M0 scripts and findings
    measure/                     dumps PDF geometry to compare against the manifest
  docs/
    decisions.md  spike-findings.md  CHANGELOG.md  README.md
```

### Dependency direction

Sfp.Core depends on nothing in the solution. Sfp.Render depends on Core. Sfp.OneNote depends on Core and Render (it needs PNG bytes and the plan). Sfp.Refresh depends on Core and OneNote. Cli and App depend on everything. Tests mirror their project. A build step (cargo-style layer check is not available in .NET; use a small test that reflects over assembly references) fails if Core gains a dependency.

## Core contracts

These types are the seams between milestones. Build them in M1 exactly as named so later milestones compose without renames. Records are immutable; services are interfaces with one production implementation and one fake for tests.

```csharp
// Sfp.Core
public sealed record QuarterSpec(int Year, int Month);                       // first month of the quarter
public sealed record Quarter(DateOnly Start, DateOnly End, int WeekCount, string Label /* "Q1 2027" or "Feb to Apr 2027" */);
public sealed record Week(int Number, DateOnly MondayStart, DateOnly SundayEnd, IReadOnlyList<DateOnly> QuarterDays, int WeeksRemaining);
public sealed record MonthGrid(int Year, int Month, IReadOnlyList<IReadOnlyList<DayCell>> Rows); // 5 rows, Monday first
public sealed record DayCell(DateOnly Date, bool InMonth, bool InQuarter, int? StackedDay);

public enum PageType { Cover, Contents, Setup, Annual, Objective, Calendar, WeeklyOverview, Day, WeeklyReflection, Scorecard, MonthlyReview, QuarterlyReview, Notes }

public sealed record PageKey(string Value);                                    // "day:2027-01-04", "week:02:overview", "objective:3", "calendar:2027-01"
public sealed record PageSpec(PageKey Key, PageType Type, string Title, int Level, int? PdfPage, DateOnly? Date, IReadOnlyDictionary<string,string> Tokens);
public sealed record SectionSpec(string Name, string? ColorHex, IReadOnlyList<PageSpec> Pages);
public sealed record NotebookPlan(string Name, Quarter Quarter, IReadOnlyList<SectionSpec> Sections, IReadOnlyList<LinkSpec> Links);
public sealed record LinkSpec(PageKey From, string AnchorId, PageKey To, string Text);  // AnchorId names a manifest anchor slot

public sealed record Manifest(...);                                            // mirrors templates/sfp-quarterly.json one to one
public interface IManifestValidator { ValidationResult Validate(Manifest m, string pdfPath); }
public interface IDateEngine { Quarter Build(QuarterSpec spec); IReadOnlyList<Week> Weeks(Quarter q); MonthGrid Grid(int year, int month, Quarter q); }
public interface IPlanBuilder { NotebookPlan Build(Manifest m, Quarter q, GenerateOptions o); }

// Sfp.Render
public sealed record RenderedPage(PageKey Key, byte[] Png, int WidthPx, int HeightPx, IReadOnlyList<AnchorPlacement> Anchors);
public sealed record AnchorPlacement(string AnchorId, float X, float Y, float W, float H, string Text, TextAlign Align, float SizePt);
public interface IPageRenderer { RenderedPage Render(PageSpec page, NotebookPlan plan, RenderOptions o); }

// Sfp.OneNote
public interface INotebookWriter
{
    NotebookHandle CreateNotebook(string folder, string name);
    SectionHandle CreateSection(NotebookHandle nb, SectionSpec s);
    PageHandle CreatePage(SectionHandle s, PageSpec p);
    void ApplyHierarchy(SectionHandle s, IReadOnlyList<(PageHandle, PageSpec)> pages);
    string HyperlinkTo(PageHandle p);
    void WritePage(PageHandle p, PageSpec spec, RenderedPage art, IReadOnlyList<ResolvedAnchor> anchors);
    PageXml ReadPage(PageHandle p);
    void Sync(NotebookHandle nb);
}
public sealed record ResolvedAnchor(AnchorPlacement Placement, string Href, bool ToolOwned);

// Sfp.Refresh
public sealed record SetupValues(IReadOnlyList<string?> Objectives, IReadOnlyList<string?> StandingGoals, IReadOnlyList<string?> MetricNames, IReadOnlyList<string?> Habits, IReadOnlyList<CalendarSource> Sources, bool AnyInked);
public interface ISetupReader { SetupValues Read(PageXml setupPage, Manifest m); }
public interface IPropagator { IReadOnlyList<PlannedChange> Plan(NotebookPlan plan, SetupValues v, Sidecar s); }
public interface ICalendarSource { IReadOnlyList<CalendarEvent> Fetch(DateOnly from, DateOnly to); }
public sealed record PlannedChange(PageKey Page, string SlotId, string? OldText, string NewText, ChangeKind Kind, bool Blocked, string? Reason);
```

### Sidecar file

sfp-state.json beside the notebook folder: tool version, manifest id, quarter spec, notebook name, and a map from PageKey to { pageId, sectionId, anchors: { slotId: objectId } }, plus lastRefresh and the frozen-through date for calendar writes. Rebuildable from the notebook alone by reading each page's sfp.key meta, which M4 implements as the recovery path.

### Tokens

Title and name patterns use exactly the PRD token set: {NN}, {n}, {start}, {end}, {Ddd}, {Mon}, {D}, {Month}, {YYYY}, {Qn}. One TokenExpander in Core; unknown tokens throw at manifest load.

## Milestone map

&#91;embedded content: milestone sequence · 8 milestones, 2 gates\]

The first gate is the only one that can stop the project: if the locked sheet does not behave on the iPad, the approach changes before any product code is written. The second gate is the owner's acceptance of a generated notebook on both devices; everything after it adds to a notebook that already works.

## M0: spike (2 to 3 days)

Goal: answer the questions the whole design rests on, with evidence, before product code exists. Python with pywin32 and PyMuPDF is allowed here; nothing from M0 ships.

| Card | Do | Done when |
| --- | --- | --- |
| M0.1 COM reachability | Script creates OneNote.Application by ProgID, calls GetHierarchy, prints the OneNote version string and the open notebooks. | docs/spike-findings.md records the version and that the 2013 schema namespace is accepted. |
| M0.2 Background image page | Create a scratch notebook, one section, one titled page; UpdatePageContent with a 1112 x 770 pt PNG as one:Image backgroundImage="true" at Position (0, titleOffset). Try titleOffset 0, 36, 48. Try PageSettings color="#DED4C8". | Findings record: the offset at which the sheet sits flush under the title, whether y=0 overlaps the title, whether the hex color is accepted or rejected, and screenshots. |
| M0.3 Subpage levels | Create three pages, UpdateHierarchy with pageLevel 1, 2, 2 and a chosen order. | Page list shows the indentation and order; findings note any need to create pages before setting levels. |
| M0.4 Link anchors | On the page from M0.2 add three one:Outline anchors: a 10 pt calendar-style number, a 15 pt right-aligned date, a centered 15 pt day name, each an a href from GetHyperlinkToObject to another page; style via span (font, size, color, text-decoration:none). | Findings record which style attributes OneNote honors (color, underline), the internal padding offset to add to the manifest, and that the links navigate. |
| M0.5 Hotspot alternative | Add a transparent 1 x 1 PNG scaled to 30 x 14 pt as a normal one:Image with the hyperlink attribute over a baked number. | Findings say whether a click on Windows follows it and whether it is visible or selectable; decision recorded: text anchors or hotspots. |
| M0.6 Page metadata | Set one:Page dateTime in UpdatePageContent; add one:Meta name="sfp.key"; set one:Section color in UpdateHierarchy. | Findings record which of the three persisted after a sync and reopen. |
| M0.7 iPad check | Move the scratch notebook to OneDrive (File, Share, Move), open on the iPad. | Findings record: background image locked under pencil ink, links navigate on tap, pencil stroke over a link does not navigate, image renders at expected size. |
| M0.8 Ink read-back | Ink a word on the scratch page on the iPad, sync, GetPageContent on Windows. | Findings quote the one:InkWord element and its recognizedText attribute as returned, or record that recognition text is absent. |
| M0.9 Manifest offsets | Apply the measured padding and title offsets to templates/sfp-quarterly.json. | Manifest committed with an M0 section in docs/decisions.md. |

Exit gate: M0.2, M0.3 and M0.7 pass. If M0.7 fails on the locked image, stop and report to the owner before M1.

## M1: core (3 to 4 days)

Goal: Sfp.Core builds a complete NotebookPlan from a manifest and a QuarterSpec, fully unit-tested, with no OneNote or rendering dependency.

| Card | Do | Done when |
| --- | --- | --- |
| M1.1 Solution skeleton | Create the solution, projects, Directory.Build.props (nullable, warnings as errors, analyzers), test projects, CI workflow running dotnet test on Windows and Linux. | dotnet test passes with one placeholder test per project; the dependency-direction test exists. |
| M1.2 Manifest model and loader | Records mirroring the PRD manifest JSON; loader with merge-over for a user manifest; token validation. | Round-trip test: load templates/sfp-quarterly.json, serialize, compare. Unknown token in a title pattern throws with the token named. |
| M1.3 PDF validation | IManifestValidator using PdfPig: page count, page size within 1 pt, text probe per page type. | Tests with a 131-page PDF, a wrong-size PDF and a missing-probe PDF (fixtures generated in-test with PdfPig or a tiny PDF writer) each fail with the PRD's message. |
| M1.4 Date engine: quarter and weeks | IDateEngine.Build and Weeks per the PRD formulas: S, E, L, offset, N, Monday start, QuarterDays, WeeksRemaining = N - k + 1, labels. | Tests: Q1 2027 gives 14 weeks, week 1 QuarterDays = Jan 1 to 3, week 14 = Mar 29 to 31, week 1 remaining 14; Q2 2024 gives 13; Q4 2026 week 14 holds Dec 28 to 31; Feb 2027 start labels "Feb to Apr 2027"; years 1900 and 2199 accepted, 1899 rejected. |
| M1.5 Date engine: month grids | MonthGrid with 5 rows Monday-first, InMonth, InQuarter, StackedDay for six-row months. | Tests: January 2027 row 1 has Dec 28 to 31 InMonth false; November 2026 has 30 stacked on the 23 cell; March 2027 row 5 has Apr 1 to 4 InQuarter false. |
| M1.6 Plan builder | IPlanBuilder walks the manifest notebook plan: Quarter, Objectives, Calendar, Week sections, Reviews, Notes; page keys, titles, levels, PdfPage mapping (day name to PDF page, 14th week reuses week 13 pages), tokens per page. | Snapshot test (Verify) of the full plan for Q1 2027: 19 sections, 144 pages, titles as the PRD naming table. |
| M1.7 Link graph | LinkSpec list per the PRD link map plus navigation: objective both ways, calendar day and week, daily date, overview day names, prev and next, reflection to next overview, reflection Progress % to scorecard, reviews. | Snapshot test counts: Q1 2027 has the PRD's anchor counts plus navigation; every LinkSpec.To resolves to a page in the plan. |
| M1.8 CLI plan dump | sfp2onenote plan --start 2027-01 prints the plan as a tree and as JSON. | Manual: output matches the snapshot. |

## M2: renderer (5 to 7 days)

Goal: every page in the plan renders to a PNG that matches a golden image, with the art the PRD describes and the link slots left blank for anchors.

| Card | Do | Done when |
| --- | --- | --- |
| M2.1 Rasterizer | PDFtoImage renders a PDF page at a DPI into an SKBitmap; cache by (pdfPage, dpi). | A 220 DPI render of page 17 is 3398 x 2353 px and the mean background color is within 2 of #DED4C8. |
| M2.2 Font service | Load Bookman Old Style by family; verify the face name; fall back to bundled TeX Gyre Bonum with a warning; expose SKFont at a size in points. | Test: a fake font directory without Bookman yields the fallback and the warning; with Bookman yields the face. |
| M2.3 Overlay primitives | Erase rectangle in background color; draw text at (x, baseline) with left, center or right alignment in points scaled by dpi/72; draw filled circle; draw rule lines. | Unit test draws onto a blank bitmap and asserts pixel colors at known points. |
| M2.4 Date and strip slots | Daily, overview and reflection date rendering per manifest (baked mode); weeks-remaining strip erase and redraw with N numbers and the circle; day counter line. | OCR test: Week 02 Monday (Mon Jan 4) reads "01 / 04 / 27", strip shows 14 numbers with the circle on 13, counter reads "Day 4 of 90". |
| M2.5 Calendar page | Month name, day numbers (baked for out-of-quarter at 40%, left blank where a link anchor will sit), stacked sixth-row days, habit legend placeholder band. | OCR test: January 2027 reads "January 2027"; the cell for Dec 28 shows a greyed 28; the cell for Jan 4 is blank (anchor slot). |
| M2.6 Cover and objectives | Cover label lines; Annual Objectives and Objective Details pages pass through with no overlay except anchor slots. | Golden hashes recorded. |
| M2.7 Time-block column rebuild | Erase x 870 to 1082, y 88 to 648; draw one row per hour in the window with 0.5 pt rules and 9 pt labels. | OCR test with window 06 to 22: 16 labels read 6 through 10 in the right order; golden hash. |
| M2.8 Generated pages | Setup, Scorecard, Monthly and Quarterly Review (reflection art with retitled heading), Notes pages, all drawn in template style from manifest geometry. | Golden hashes; a reviewer screenshot set is saved under docs/renders for the owner to eyeball once. |
| M2.9 Anchor placements | For each page, emit AnchorPlacement entries (position, size, text, align, size pt) for every link slot and propagated slot, nothing drawn for them. | Snapshot test: Mon Jan 4 has anchors for date, prev, next, objective legend band; January 2027 has 31 day anchors plus 5 week anchors. |
| M2.10 Golden suite and render-only | Deterministic rendering (fixed font hinting, no system time); sfp2onenote render-only --start 2027-01 --out dir writes 144 PNGs and pages.json; golden hash file under tests. | Full render under 60 s on a 4-core machine; hashes stable across two runs. |

## M3: COM writer and generate (5 to 7 days)

Goal: sfp2onenote generate produces the whole notebook in OneNote with hierarchy, locked sheets, link anchors, Contents page, sidecar and page keys. Runs only on the owner's PC.

| Card | Do | Done when |
| --- | --- | --- |
| M3.1 COM wrapper | OneNoteApp class: late-bound creation, STA thread, every call wrapped with the retry-once rule on RPC\_E\_CALL\_REJECTED, HRESULT to typed exceptions, timing logged. | Integration test (Category COM) opens OneNote and lists notebooks. |
| M3.2 Page XML builder | Pure function from (PageSpec, RenderedPage, anchors, options) to the page XML string per the PRD: PageSettings, Title, background Image outline, anchor outlines with alignment, sfp.key meta, dateTime if M0.6 allowed it. | Unit tests validate the XML against 0336.OneNoteApplication\_2013.xsd (checked into tests/schema) and snapshot the Mon Jan 4 XML with image data elided. |
| M3.3 Notebook and sections | CreateNotebook via OpenHierarchy cftNotebook, CreateSection via cftSection in plan order, section color. | COM test creates a scratch notebook with 3 sections in order, then deletes it. |
| M3.4 Pages and hierarchy | Create every page first (CreateNewPage with title), then ApplyHierarchy per section with names, order, levels. | COM test: a week section shows Overview, 7 days indented, Reflection. |
| M3.5 Hyperlinks | HyperlinkTo per page and section; cache in the sidecar. | COM test: links resolve and NavigateToUrl opens the page. |
| M3.6 Write pages | WritePage for all 144 pages with anchors resolved to hrefs; progress callback per page; memory released per page. | Full generate completes in under 3 minutes; notebook under 60 MB. |
| M3.7 Contents page | Native outline of links grouped by section with week subpages nested. | Manual: every link on Contents opens the right page. |
| M3.8 Sidecar | Write sfp-state.json with page and anchor object IDs after generation. | File validates against its JSON schema; a second generate into the same name is refused per the PRD. |
| M3.9 generate verb | Wire CLI: validation, render, write, sync, NavigateTo cover, summary with the OneDrive move instruction; exit codes; cancel deletes the partial notebook. | Manual Windows acceptance list in the PRD passes for Generate items. |
| M3.10 iPad acceptance | Owner moves the notebook to OneDrive and runs the PRD's iPad checklist. | Checklist results recorded in docs/acceptance-M3.md; any failure becomes a card before M4. |

## M4: refresh and propagation (5 to 7 days)

Goal: sfp2onenote refresh reads the Setup page and propagates objectives, standing goals, metric names and habits into tool-owned anchors without touching user content.

| Card | Do | Done when |
| --- | --- | --- |
| M4.1 Page reader | ReadPage returns a PageXml model: outlines with positions, text runs, ink words with recognizedText, images, meta. | Unit test parses a captured XML fixture from M0.8 and finds a typed run and an inked word by rectangle. |
| M4.2 Setup reader | ISetupReader maps slot rectangles from the manifest to values; typed text wins over ink; AnyInked set when any value came from ink. | Tests with fixtures: typed only, ink only, mixed, empty. |
| M4.3 Sidecar recovery | Open a notebook by name; if the sidecar is missing or an ID fails, walk GetHierarchy and each page's sfp.key meta to rebuild the map. | COM test: delete the sidecar, run refresh --dry-run, sidecar is rebuilt with every page key. |
| M4.4 Propagator | IPropagator computes PlannedChange for every target slot in the PRD's Setup table; blocked when a non-owned object overlaps the slot rectangle; idempotent when the text is unchanged. | Unit tests: 8 objectives produce legend changes on 14 overviews, 90 days, scorecard, 8 detail headings, 8 annual rows; a user outline in the band blocks that page's legend with a reason; second plan on the same values is empty. |
| M4.5 Anchor writer | Write or update tool-owned outlines by objectID through UpdatePageContent with the page's existing XML plus changes (never a full page replace). | COM test: update one anchor, re-read, only that outline changed; the background image bytes are not resent. |
| M4.6 Dry run and summary | --dry-run prints every PlannedChange; the real run prints counts and skips. | Manual: preview matches the PRD's Refresh test card. |
| M4.7 refresh verb | Wire CLI with exit codes; log file per run. | PRD Refresh acceptance items 1 and 2 pass on the owner's PC. |

## M5: calendar import and daily task (4 to 5 days)

Goal: refresh --calendar writes events from the Setup page's sources into daily, weekly and monthly pages for today through the horizon, frozen for past days, and runs unattended every morning.

| Card | Do | Done when |
| --- | --- | --- |
| M5.1 Sources | ICalendarSource for an .ics URL or file (Ical.Net, recurrence expanded over the window, time zones normalized to local), an Outlook COM reader behind a feature check, and the bundled US holiday list. | Tests with .ics fixtures: a weekly recurring meeting expands correctly across a DST change; an all-day event is flagged; a private event is masked. |
| M5.2 Placement engine | Map events to slots per the PRD placement table: hour rows on daily pages with continuation lines, weekly columns with the 18-line cap, monthly cells with the 2-line cap; truncation to fit. | Unit tests on a synthetic day with 4 events including a 3-hour one and an all-day one. |
| M5.3 Freeze and diff | Days before today are never changed; cancelled events are removed; unchanged events produce no write. | Tests: a run on day D then D plus 1 with one cancelled event yields exactly one removal and no writes to day D minus 1. |
| M5.4 Scheduled task | Register and remove a Windows Task Scheduler task (schtasks or the TaskScheduler COM) for 05:00 daily, run only when logged on, start OneNote if needed. | Manual: task appears in Task Scheduler and runs refresh --calendar from its log. |
| M5.5 Failure handling | Per-source failure skips that source; three consecutive total failures raise a toast; exit code 6. | Tests with a failing source stub. |
| M5.6 Acceptance | PRD Refresh acceptance items 3 and 4. | Recorded in docs/acceptance-M5.md. |

## M6: desktop app and packaging (4 to 5 days)

Goal: a single self-contained sfp2onenote.exe with a Generate tab and a Refresh tab over the same services the CLI uses. No logic in the UI layer.

| Card | Do | Done when |
| --- | --- | --- |
| M6.1 Shell and settings | WPF window, MVVM, settings persistence in %APPDATA%\\sfp2onenote\\settings.json, last-used values. | Opens, remembers values across restarts. |
| M6.2 Generate tab | Fields per the PRD UX table (month picker, PDF path, name, output folder, date format, weeks remaining, DPI, Advanced group), validation messages, size estimate. | Every validation row in the PRD table is exercised by a UI test or a manual script. |
| M6.3 Previews | Three thumbnails rendered through IPageRenderer on a background thread, refreshed on change. | Changing the month re-renders within 2 s. |
| M6.4 Generate flow | Phased progress bar, cancel, success summary with Open in OneNote and the OneDrive instruction, failure view with Save log. | Manual run matches the PRD Generate flow. |
| M6.5 Refresh tab | Notebook list from sidecars, Preview changes, Refresh now, daily task checkbox, run summary. | Manual run matches the PRD Refresh screen. |
| M6.6 Packaging | dotnet publish single-file, self-contained, win-x64, trimmed where safe; templates folder beside the exe; README with install-free instructions. | A clean Windows VM runs the exe and completes a render-only without installing anything. |

## M7: hardening and final acceptance (3 to 4 days)

| Card | Do | Done when |
| --- | --- | --- |
| M7.1 Error matrix | Walk the PRD error-handling table and the Refresh cases; each row has a test or a scripted manual check and the exact user-facing message. | docs/error-matrix.md maps every row to its test. |
| M7.2 Performance | Profile a full generate; parallel render, single STA write; memory under 1.5 GB. | Timings logged in CHANGELOG for Q1 2027: under 3 minutes end to end. |
| M7.3 Logging review | Every COM call logged with duration; failed page XML saved; no image bytes in logs. | Log of a full run reviewed and under 2 MB. |
| M7.4 Full acceptance | Run the PRD's automated suite, the Windows manual list and the iPad list on a fresh Q1 2027 notebook; then generate Q2 2027 with --previous and confirm the Next quarter link. | docs/acceptance-final.md with every item ticked or a filed card. |
| M7.5 Release | Tag v1.0, publish the exe and templates as a release zip, README and CHANGELOG current. | Owner can download, run and generate without reading the code. |

## Appendix

### Test data

- SFP\_PDF\_PATH points at the owner's SFP\_No\_Dates.pdf; never commit it.
- Reference quarters used throughout tests: Q1 2027 (14 weeks, partial first and last week), Q2 2024 (13 weeks, Monday start), Q4 2026 (year boundary, six-row November), Q1 2024 (leap year), February 2027 start (off-cycle label).
- Golden PNG hashes live in tests/Sfp.Render.Tests/golden/\*.sha256; the PNGs themselves are regenerated, not committed.
- .ics fixtures: weekly recurring meeting across the March DST change, an all-day holiday, a private event, a cancelled instance.

### Commands

```
dotnet build -warnaserror
dotnet test --filter "Category!=COM"           # anywhere
dotnet test --filter "Category=COM"            # owner's PC with OneNote
sfp2onenote plan --start 2027-01
sfp2onenote render-only --pdf %SFP_PDF_PATH% --start 2027-01 --out .\\out\\q1
sfp2onenote generate --pdf %SFP_PDF_PATH% --start 2027-01 --name "SFP Q1 2027"
sfp2onenote refresh --notebook "SFP Q1 2027" --dry-run
sfp2onenote refresh --notebook "SFP Q1 2027" --calendar --horizon 14
```

### Starting prompt for the AI developer

Use this as the first message of the build session, with both documents attached or linked:

> You are building sfp2onenote, a Windows tool described in the attached PRD and development plan. Read the PRD fully, then the plan. Work the milestones in order, one task card at a time, starting with M0.1. For each card: state the card ID, make the change, add the test the card names, run dotnet build and dotnet test, and finish with a commit whose message starts with the card ID and states how the done condition was met. Record every judgment call in docs/decisions.md and every spike result in docs/spike-findings.md. Never put measured coordinates in code; they belong in templates/sfp-quarterly.json. Never edit user content in a notebook. Never use em dashes in code, comments, docs or UI strings. Stop and ask only in the cases the plan lists under When to stop and ask. When a milestone's exit gate is met, summarize what was proven and continue to the next milestone.

### Milestone estimates

| Milestone | Days | Needs the owner's PC |
| --- | --- | --- |
| M0 spike | 2 to 3 | yes, plus the iPad |
| M1 core | 3 to 4 | no |
| M2 renderer | 5 to 7 | no (PDF path only) |
| M3 COM writer and generate | 5 to 7 | yes, plus the iPad |
| M4 refresh | 5 to 7 | yes |
| M5 calendar | 4 to 5 | yes |
| M6 desktop app | 4 to 5 | yes for the final check |
| M7 hardening | 3 to 4 | yes, plus the iPad |

About 31 to 42 working days in sequence. M1 and M2 can run on any machine in parallel with M0 if two sessions are available, since they share only the manifest.
