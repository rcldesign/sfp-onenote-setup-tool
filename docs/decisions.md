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

## Wave 1 (M0, M1.2 to M1.5, M2.1 to M2.3, M3.1, M3.2, M4.1, M5.1 to M5.3, M5.5)

### M1.2 and M1.3 manifest and validation

- M1.2: the JSON key stays "onenote" as the PRD and the shipped file write it; Manifest.OneNote carries [JsonPropertyName("onenote")] instead of renaming the key to the strict camelCase "oneNote". PropertyNameCaseInsensitive stays false so every other key must be canonical camelCase.
- M1.2: unknown keys are an error (UnmappedMemberHandling.Disallow) so a typo in a user manifest ("baslineY") fails at load naming the key and its JSON path instead of being silently ignored; "$comment" is stripped from the node tree before deserialization so it stays allowed anywhere. Sidecar and settings loaders that want forward tolerance should derive new JsonSerializerOptions(ManifestLoader.JsonOptions) { UnmappedMemberHandling = Skip }.
- M1.2: JsonOptions is read-only (MakeReadOnly with the default reflection resolver) so no module can mutate the shared instance, and uses JavaScriptEncoder.UnsafeRelaxedJsonEscaping so the manifest's non-ASCII characters (the middle dot and the single angle quotes in link textFormat) serialize as themselves; the files are never embedded in HTML.
- M1.2: merge semantics: objects merge recursively, arrays and scalars replace whole, an explicit null in the user file replaces the shipped value, and both inputs are deep-cloned, never mutated. A user manifest that changes one title must therefore replace the whole sections array (arrays are not merged element-wise).
- M1.2: token syntax is letters between braces; a stray or unbalanced brace ("{Month {YYYY}", "Week {NN", "{}") is a load error, and token names are case-sensitive ({mon} is not {Mon}).
- M1.2: structural checks go slightly beyond the card's list: weekly page names must be distinct, known and each have a probe; firstPdfPage, stride and count must be positive and the last weekly page inside pageCount; fixed-type pdfPages must be non-empty and inside 1..pageCount; page template types are checked against PageNames.TemplateTypes (day stands for mon..sun), levels must be 1 to 3, and notebook.namePattern is token-validated too.
- M1.2: PageNames is the single list of page names for appliesTo lists and page template types; later modules (plan builder, renderer) should use it rather than string literals.
- M1.3: the validator collects every mismatch (page count first, then sizes in page order, then probes in manifest order) instead of stopping at the first, so the UI can show them all; probes on pages beyond the actual page count are skipped because the count error already covers them.
- M1.3: probe text is the page's words joined by single spaces plus PdfPig's raw letter stream, whitespace collapsed, compared case-insensitively; a second pass with all whitespace removed covers headings whose letters PdfPig splits (Quartz kerning). ContainsProbe is public so the rule is unit-tested directly.
- M1.3: the validator catches every exception PdfPig raises on a damaged file (except OutOfMemory) and reports "Could not read PDF ..." because the contract says unreadable is an error, not an exception; CA1031 is already at suggestion level, no .editorconfig change.
- Measure: a word's x and right are the advance box (glyph origin to advance end, what pdfplumber reported for the PRD measurements), top and bottom are the glyph ink bounds, and Baseline is reported as an extra field because the manifest's baselineY values are baselines; words sort by baseline then x so one line prints together.
- Measure: stroked paths yield one segment per Line command plus the closing segment of a closed subpath, curves are skipped, clipping paths ignored; a fill-only rectangle no thicker than 2 pt (PdfGeometryDump.ThinFillPt) is reported as a rule with its thickness as the width because planner PDFs draw some rules as fills. A path that is both filled and stroked counts as stroked.
- Measure: DefaultPages {1, 13, 16, 17, 24} is a constant on PdfGeometryDump (page numbers, not measured geometry) so the standalone tool needs no templates folder; the CLI verb uses the same constant.
- Measure: PdfFixtures draws fill-only rectangles (background, test rules) through raw content-stream operations because PdfPig's DrawRectangle(fill: true) also strokes a hairline, which would surface as four segments in the dump. The fixture also draws the manifest's date slashes, strip label and numbers, month rule and day-grid boxes so the render tests can reuse it; headings use generated.heading and the cover text is centered on coverLabel.x.
- Tests: PdfGeometryDumpTests lives under tests/Sfp.Core.Tests/Manifests to stay inside the card's folders; JsonTrees is an internal test helper (subset and symmetric tree comparison ignoring $comment).

### M1.4 and M1.5 date engine

- M1.4: Quarter length is not always 90 to 92. A February start in a common year is 28 + 31 + 30 = 89 days (Feb 2025, 2026, 2027). The PRD's "90, 91 or 92" describes on-cycle quarters; the engine takes the length from DateOnly math as the PRD requires (never by hand). Tests assert 89 to 92 over all 60 starts and 90 to 92 for on-cycle starts, with an explicit 89-day test. The PRD prose could get a one-line correction (integrator's call).
- M1.4: Week count follows the PRD formula N = ceil((o + L) / 7), not its prose summary "13 only when the quarter starts on a Monday". A 90-day quarter starting on a Tuesday (o = 1, o + L = 91) also has 13 weeks: December 2026, February 2028 and the on-cycle Q1 2030. The formula is the normative table entry and the card's named cases all hold under it. The plan builder must take WeekCount from the Quarter, never assume 14 for a non-Monday start. Tests assert N == 13 exactly when o + L <= 91, with explicit Tuesday-start tests.
- M1.4: Quarter label wording lives in PlannerText.QuarterLabel and the engine calls it, so the cover, the notebook name and the {Qn} token share one source. Off-cycle labels use the start year ("Dec to Feb 2026"), per docs/decisions.md.
- M1.4: PlannerText.DateSlot returns a small record DateSlotText(First, Second, Third) with a Fields tuple and a Text property rather than a bare four-tuple; fields are in drawing order left to right, so in DayMonthYear order the manifest's "month" position carries the day. The renderer places First/Second/Third at the manifest's first/second/third date field x.
- M1.4: DayCounter throws ArgumentException naming the token when the manifest dayCounter.format holds anything other than {dayOfQuarter}, {quarterLength}, {week}, {weekCount}; that format is not covered by the pattern token validator, so the check lives here.
- M1.4: Additive members only on the fixed contracts: IDateEngine gained WeekOf and GridWithMode, Quarter gained DayOfQuarter, DayCell gained StackedDate, and PlannerText offers TokenValues overloads for Quarter (Qn, YYYY) and Week (NN, start, end) so the plan builder does not re-derive them. Any fake IDateEngine written elsewhere must implement the two new methods.
- M1.5: Grid always fills all 35 cells with real dates and stacks overflow by default; the renderer decides from GenerateOptions (SixthRow, OutOfMonthDays) whether to draw stacked and out-of-month days. Grid validates year and month the same way Build does. Overflow is at most two days (30 and 31 for a Sunday start of a 31-day month) and lands on the row-5 cells of the same weekday (the 23 and 24 cells), matching the paper-planner convention.
- Git: the worktree was created at the pre-foundation commit; since 26e4080 is an ancestor of the foundation commit 055beba, the existing worktree branch was fast-forwarded onto 055beba instead of creating wave1/dates, keeping the GIT rule (branch existed, so it was kept) and the foundation code.

### M0 spike scripts

- Branch base (M0): the worktree started at 26e4080 without src, templates or docs; the branch was fast-forwarded onto the foundation commit bf68bce (an ancestor relation, no merge commit) so the scripts read the real manifest and the task's read-first list could be followed.
- M0.2 read-back uses PageInfo piBasic (0), not 1: the card text says "piPageInfo=1 without binary data", but in the OneNote enum piBinaryData is 1 and piBasic (0) is the value without binary data; piBasic returns images as one:CallbackID so the outline position is readable without the bytes. The enum is spelled out in spike_common.py.
- Expected-last-modified DATE is sent as datetime(1899, 12, 30) (OLE DATE 0, what .NET sends for DateTime.MinValue) rather than pywintypes.Time(0) (which is 1970); if a binding marshals it wrongly, _call resends it as the float 0.0 on hrLastModifiedDateDidNotMatch or DISP_E_TYPEMISMATCH.
- Retry-once-after-2-s applies to every COM call, not only UpdatePageContent and GetPageContent, and covers RPC_E_SERVERCALL_RETRYLATER (0x8001010A) as well as RPC_E_CALL_REJECTED (0x80010001), since OneNote reports a busy instance with either.
- Binding: gencache.EnsureDispatch (makepy wrappers) first, dynamic Dispatch as fallback; [out] parameters are omitted and returned under both; a placeholder is inserted only if a binding raises TypeError or DISP_E_BADPARAMCOUNT, and a None result then names the makepy command.
- M0.4 default write mode is "merge": the page's piBasic XML (image as CallbackID, no bytes resent) plus the new outlines, the pattern the M4.5 writer intends; --partial sends only the outlines, and whichever mode fails the other is tried, both recorded, so the spike also answers how partial UpdatePageContent behaves.
- M0.4 adds two labelled variants beyond the three required anchors (one without text-decoration:none, one without a span) so the read-back shows which attribute OneNote strips; they are excluded from the kept/dropped tallies.
- M0.5 tries two schema shapes (image inside an outline, then image as a direct child of one:Page with its own Position) and records which was accepted; the hotspot is placed over a "4" the test sheet bakes into the first calendar box so there is a real target.
- M0.6 writes one:Meta before one:PageSettings first (the 2013 schema order) and after it if rejected; the PageSettings in that write uses color="automatic" so a rejected hex color (an M0.2 question) cannot mask the Meta result; the section color write echoes the section's existing pages so UpdateHierarchy drops nothing.
- Scratch notebook path guard: creation is refused under %OneDrive%, %OneDriveConsumer%, %OneDriveCommercial% or any path component containing "onedrive"; an already open notebook of that name is used wherever it lives, so M0.8 still works after the M0.7 move to OneDrive.
- Spike anchor geometry comes from the manifest links block (calendarDay at column 2 row 1, dailyDate, overviewDayHeader column 1, hotspot at column 1 row 1 of dayGrid), the page size, colors and font family from the manifest; the spike-only knobs (220 DPI, 50 pt grid, offsets 0/36/48, the planner date 2027-01-04) are script arguments with those defaults.
- M0.9 patches the manifest text in place instead of json.load/json.dump with indent 2, because a full re-dump would reflow every inline array and object in the file; the result is parsed and compared, and a missing key is inserted at the block's indent.
- Visual answers (flush offset, title overlap, link navigation, color, underline, padding, hotspot click) are collected by console prompts with --no-prompt to skip; the scripts record "not determined" and the proposals they can derive from XML (titleOffsetPt from where OneNote moved the y = 0 outline, pageColor from hex acceptance, anchorUnderline from whether text-decoration:none survived), which the owner's answers override.
- A fake OneNote harness (tools/spike/fake, smoke_fake.py) was added beyond the card's list so the scripts can be executed on Linux; it exercises code paths only and the README says so. A tools/spike/.gitignore ignores __pycache__ for the fake package (the root .gitignore covers only tools/spike/__pycache__).

### M2.1 to M2.3 render foundation

- M2.1 CA1416: the PDFium calls are wrapped in `OperatingSystem.IsWindows() || IsLinux() || IsMacOS()` guards inside Rasterizer (a PlatformNotSupportedException otherwise) instead of SupportedOSPlatform attributes on the class, because attributes would propagate to every caller up to Program.cs and the tests; the guard set matches the platforms PDFtoImage ships PDFium for.
- M2.1 Cache is a bounded LRU (default 12 pages, about 384 MB at 220 DPI, configurable, 0 disables) rather than everything: 144 pages at 32 MB each would be 4.6 GB. The reuse patterns (week 14 reusing week 13's nine pages back to back, reviews reusing the reflection page, three calendar pages) fit in 12. Cached bitmaps are marked immutable and callers always get a copy.
- M2.1 One lock serializes PDFium (it is not thread-safe); PDF bytes are read once per path and PDFium reparses from memory per render (about 30 ms a page). A later parallel render (M7.2) should parallelize overlays and encoding, not rasterization.
- M2.1 Rasterizer takes the background SKColor in its constructor (from manifest colors.background through ColorParser) so the manifest stays the only source of the color.
- M2.1 The real-PDF check uses the dominant (most frequent) color of the page instead of a fixed patch, because an empty region of the real daily page cannot be chosen without the file; the synthetic check uses both a fixed 200 x 200 px patch and the dominant color.
- M2.1 Synthetic template: every modeled number is read from the manifest record. The three section labels on daily pages (Daily Goals, Plan, Time Blocks) are not modeled by any manifest slot, so their positions are fixture-local constants taken from the PRD's measured daily-page table rather than new manifest keys; the fixture is a stand-in for probes and geometry, and adding printed-art positions to the production manifest for a test fixture seemed wrong. Week run k circles number k, as printed.
- M2.1 TestManifest deserializes the Manifest record with System.Text.Json (web defaults, camelCase string enums, comments allowed) because the M1.2 loader is being written in another worktree; it is a two-line switch to the Core loader once merged.
- M2.1 The synthetic PDF is cached in the temp folder under a name hashed from SyntheticTemplate.Version and the manifest text, so a manifest edit or a fixture change regenerates it, and concurrent test processes share one file (write to a temp name, then move).
- M2.1 OCR helper order: tesseract CLI if on PATH, else python3 -I with tesserocr, else unavailable; small crops are upscaled to at least 48 px on a white canvas before OCR; default segmentation is single line (PSM 7), with SingleWord and Block available. Pixel assertions never depend on OCR.
- M2.1 ColorParser accepts only '#RRGGBB' (NumberStyles.AllowHexSpecifier, no whitespace, no alpha), since the manifest never carries anything else and a looser parser hid a bug in the first run.
- M2.2 IFontResolver returns an SKTypeface or null; FontService owns every typeface a resolver returns and verifies FamilyName (ordinal, ignore case) on every style, so a resolver may substitute and the service still refuses. DirectoryFontResolver scans file metadata once and opens a fresh face per Resolve so ownership is simple.
- M2.2 'With Bookman' is simulated with a renamed copy of TeX Gyre Bonum whose OpenType name table says Bookman Old Style (FontFixtures), because renaming a file does not change the family a font reports and the real font cannot be bundled. Only name table format 0 is handled (the bundled fonts use it).
- M2.2 A fallback file whose family differs from manifest font.fallback is refused as a substitution too, so a wrong bundled file is caught at load instead of producing silent glyph drift. Missing secondary styles (bold, italic) reuse Regular with a warning rather than failing.
- M2.2 SKFont gets LinearMetrics = true in addition to Hinting None, Subpixel true and Edging Antialias, so advances scale linearly with size and layout is deterministic across DPIs.
- M2.2 On Linux the SkiaSharp native that PDFtoImage brings is the NoDependencies build without fontconfig, so SKFontManager.Default.MatchFamily returns null for every family (verified with installed DejaVu); the production path there is always the fallback. Windows (DirectWrite) is the target and finds Bookman.
- M2.3 SheetCanvas takes fonts already at pixel size (IFontService.Font(sizePt, canvas.Scale)) rather than applying a canvas transform, so text rasterization matches a plain draw; a convenience overload takes the service and a point size to avoid scale mistakes.
- M2.3 Rect fills and erases are not antialiased so an erase covers whole pixels and leaves no fringe; text, circles and rules are antialiased. Rules clamp the stroke to at least one pixel, matching how PDFium draws the template's 0.12 to 0.5 pt hairlines at every DPI.
- M2.3 Blank sheets are Bgra8888 premultiplied like PDFium's output so overlays behave identically on rasterized and generated pages; PngEncoder converts to Rgb888x at the end (Skia encodes that layout as color type 2, no alpha).
- M2.3 Golden convention: one file per fixture and runtime identifier (<fixture>.<rid>.sha256, fixture synthetic or sfp) because PDFium and Skia antialiasing differ by a few pixels between platforms; recorded with SFP_RECORD_GOLDENS=1 and committed on their own with the reason in the commit message.

### M3.1, M3.2 and M4.1 OneNote wrapper, XML builder and page reader

- M3.1 Production class split: OneNoteApp is the public [SupportedOSPlatform("windows")] shell; the scheduler, dynamic call sites, retry and mapping live in the internal platform-neutral OneNoteSession driven by an internal IOneNoteActivator. Reason: CA1416 is an error under warnings-as-errors, and a windows-only class cannot be constructed from Linux tests; the session runs the real dynamic call sites against a fake target with the same method shapes as IApplication.
- M3.1 Version: the OneNote.Application COM interface has no version member, so Version is the file version of onenote.exe found through HKLM App Paths; "unknown" when unreadable. The property triggers activation (starts OneNote), which is what the startup prerequisite check wants.
- M3.1 Activation is lazy on the first call and happens on the worker thread (a COM object must be used on the apartment that created it); an activation failure maps to OneNoteUnavailableException and is retried on the next call.
- M3.1 Null IDs (startId, relativeToId, pageObjectId) are passed to COM as "" rather than null, because a null string through IDispatch can arrive as VT_EMPTY and fail with a type mismatch; OneNote treats "" as "no object" for these parameters.
- M3.1 XML-rejected set is hrMalformedXML 0x80042000, hrInvalidXML 0x80042001 and the inserting-content codes 0x80042007, 0x80042008, 0x80042009, 0x8004200D (the card asked for 0x80042001 plus the other invalid-XML codes). Unavailable set is REGDB_E_CLASSNOTREG 0x80040154, CO_E_SERVER_EXEC_FAILURE 0x80080005, RPC_S_SERVER_UNAVAILABLE 0x800706BA, RPC_E_DISCONNECTED 0x80010108, CO_E_OBJNOTCONNECTED 0x800401FD, MK_E_UNAVAILABLE 0x800401E3. The 0x8004xxxx names follow the OneNote 2013 developer reference error table.
- M3.1 Log levels: Information for every completed call with name and duration (so a default file sink satisfies M7.3), Debug for argument detail, Warning on the single retry, Error on the final failure. XML bodies are logged only after one:Data payloads are elided and then cut to 200 characters.
- M3.1 Every call passes the 2013 schema (xs2013 = 2) explicitly, and optional COM parameters (force, fNewWindow, dateExpectedLastModified) are always passed, because IDispatch late binding does not fill C# defaults.
- M3.2 PageXmlBuilder.Build takes the page ID as its first argument (string pageId) in addition to the plan's (PageSpec, RenderedPage, anchors, options): PageSpec carries no OneNote ID and PageHandle would drag section and notebook handles into a pure function.
- M3.2 one:Meta is written before one:PageSettings and one:Title, the order the 2013 schema sequence requires (QuickStyleDef, TagDef, Meta, PageSettings, Title, content), even though the card text listed Meta after Title.
- M3.2 HideTitle omits the one:Title element entirely (a npsBlankPageNoTitle page has no title area) and places the sheet and every anchor without the title offset.
- M3.2 PageScale scales anchors too: position, size and font size are multiplied by the scale along with the sheet, so a 0.72 Letter-width page keeps its links over the printed slots. The card's formula is the scale 1 case.
- M3.2 PageSizeMode.Custom writes one:Dimensions with the manifest page size unscaled (1112 x 770), as the card states, using Width/Height elements with a value attribute per the 2013 schema reading.
- M3.2 The dateTime zone is an explicit PageXmlOptions.TimeZone (default UTC); PageXmlOptions.From takes a TimeZoneInfo so the CLI passes TimeZoneInfo.Local and tests pass a fixed UTC-5 zone. No builder reads the system clock or zone. Format is yyyy-MM-ddTHH:mm:sszzz at the manifest pageDateTimeHour; lastModifiedTime is never written.
- M3.2 Propagated text color is the ink blended toward the page background by the placement opacity, rounded half away from zero per channel (ink 60% over #DED4C8 = #6B6763); links always use the ink and get text-decoration:none unless onenote.anchorUnderline is true. A link-kind anchor with an empty href is written as plain styled text.
- M3.2 Every anchor passed in is emitted, including a propagated slot with empty text, so the writer can record its objectID; the M3.6 writer decides what to pass.
- M3.2 Contents links are plain <a href> anchors without span styling and the outline has no one:Size, matching the PRD appendix; level 2 pages nest under the level 1 page before them, and a level 2 page with no parent before it is listed at the top level.
- M3.2 Numbers are written with up to three decimals ("0.###", invariant, negative zero normalized); font sizes as "10.0pt". Float placements such as 28.8f print as 28.8.
- M3.2 XML is assembled with System.Xml.Linq and serialized with LF line ends and two-space indentation; XCData was verified to split a ]]> terminator into two CDATA sections, and all HTML inside CDATA (hrefs, text, font family) is HTML-escaped with WebUtility.HtmlEncode.
- M3.2 The schema file tests/Sfp.OneNote.Tests/schema/OneNoteApplication_2013.xsd is a reconstruction (the official 0336.OneNoteApplication_2013.xsd is not downloadable here); its header says to replace it with the official file on the owner's PC and rerun the suite. Validation treats warnings as errors and a strictness test proves unknown attributes and misordered elements fail.
- M3.2 The tests deserialize templates/sfp-quarterly.json directly into Manifest (TestManifest) so every fixture coordinate comes from the manifest; this is test-only and can be swapped for the M1.2 loader by the integrator.
- M4.1 PageXml.cs is fixed, so ink drawings are exposed through extension methods (PageXmlInk.InkDrawings / InkDrawingsIn) that read the kept XDocument rather than a new record member.
- M4.1 InkWord elements are collected from anywhere beneath the outline (directly under one:OE or inside one:InkParagraph) because the exact 2013 nesting is unconfirmed until M0.8; the fixture puts them directly under the OE. InkWord.Bounds is null unless the element carries its own Position and Size; the enclosing outline's rectangle is what OutlinesIn matches.
- M4.1 PlainText keeps <br> as line breaks, strips other tags, decodes entities (nbsp to space) and trims; the parser takes the namespace from the document root so 2010 pages parse, and reports a non-Page root as FormatException while malformed XML surfaces as XmlException.
- M4.1 The Setup page fixture is hand-written in the GetPageContent shape (QuickStyleDefs, author attributes, objectIDs, CallbackIDs) and validates against the schema subset; it will be replaced by the capture from the M0.8 spike.

### M5.1 to M5.5 calendar import

- Worktree base: the worktree started at 26e4080 (no src/ at all); the foundation lives at 055beba on claude/wonderful-hopper-0a420d, a descendant, so the branch was fast-forwarded with git merge --ff-only (no reset, no history rewrite) and kept its name worktree-wf_2daf9710-ce3-6 because `git branch --show-current` printed one.
- Text measure delegate is TextWidthMeasure(string text, double sizePt) rather than Func<string, double>: the slots use different sizes (8 pt rows and cells, 9 pt overview columns) so a width function without the size cannot fit them; tests pass a monospace estimate (0.55 em per character).
- The placement engine emits every calendar slot of every day it is asked about, empty ones included, as CalendarSlotText carrying the day's date. PlannedChange has no date, and the diff must know which slots are in scope and which day they belong to (an overview column's day is not derivable from its page key alone); PlaceChanges still gives the non-empty ones as PlannedChange for callers that only want writes.
- Weekly Overview order: all-day lines first (no time), then timed events latest start first, reading the PRD's "newest at top" literally; PlacementOptions.OverviewNewestFirst=false gives chronological order if the owner prefers it.
- Line caps: the "+n more" line counts toward maxLines, so an 18-line column shows 17 events plus the more line when there are more than 18, and a 2-line cell shows 1 event plus "+2 more" for 3 events; exactly maxLines events show in full.
- Daily rows: a timed event is listed only on the day it starts; it gets a continuation line at every later hour it extends into, including a partial hour (9:30 to 11:30 covers 10 and 11), capped at the window end; an event that starts outside the window is listed in the first row with its time and gets no continuation lines. Time format follows the PRD exactly: "9:00" on daily pages, "09:00" on the overview.
- All-day events spanning several days (DTEND exclusive) appear on every day they cover in the band, the overview column and the monthly cell; holidays are one day each.
- Event Uid is UID@localStart (yyyy-MM-ddTHH:mm, or the date for all-day); a RECURRENCE-ID override uses the recurrence id as the instance key so it shares the Uid of the instance it replaces. Ical.Net 5 already substitutes the override for the master instance and drops EXDATE instances (verified at runtime), and RecurrenceId is obsolete so RecurrenceIdentifier.StartTime is used.
- CLASS:CONFIDENTIAL is masked like CLASS:PRIVATE; an event without SUMMARY gets "(No title)"; floating times (no TZID, not UTC) are taken as already local; UTC and zoned times are converted through the injected TimeZoneInfo (AsUtc then ConvertTimeFromUtc), all results Kind Unspecified.
- Recurrence expansion starts 31 days before the window and ends 2 days after, then each instance is kept only if a local day it covers lies in [from, to]; this removes time zone edge cases at the window ends and catches long all-day events that started earlier.
- Setup line syntax: "Label | Location | ColorTag | CalendarName" (fields after the location optional), or a bare URL/path; keywords outlook[:folder name] and holidays[:list id]; the kind comes from the scheme (http, https, webcal), the .ics extension or the keyword; any other scheme (ftp etc.) is rejected rather than treated as a path; default labels are the URL host, the file name without extension, "Outlook" or "Holidays". ParseAll skips blanks, collects bad lines as problems and caps at 4.
- Holiday list resolution: a Holidays source's Location is a list id mapped to templates/holidays-{id}.json ("us" -> holidays-us.json, the default) or a path ending in .json; the file's own id (us-federal) is what goes into Uids. Observed shifts are evaluated for the years before and after the window because they can move a holiday across a year boundary.
- Outlook reader: Restrict filter uses the invariant "MM/dd/yyyy hh:mm tt" pattern Outlook's Restrict parses on an en-US machine; Sensitivity 2 maps to IsPrivate, MeetingStatus 5 or 7 to Cancelled, GlobalAppointmentID (else EntryID) plus local start forms the Uid; CA1416 is satisfied by an explicit OperatingSystem.IsWindows() guard and [SupportedOSPlatform] on the COM methods.
- IcsCalendarSource owns the HttpClient it creates (30 s timeout) and implements IDisposable; a shared client can be injected through the factory. The ICalendarSource interface was not extended (other engineers may have fakes), so labels travel in CalendarSourceBinding.
- FrozenThrough returned by the diff is max(previous FrozenThrough, yesterday): a clock that goes backwards never unfreezes a day. Days past today plus the horizon are not written either. Placing the same SlotRef twice throws, since it means an engine bug.
- Failure streak: only a run where every configured source failed counts; a partial failure (one of two down) resets the streak because events flowed. The toast is raised on every run while the streak is at or past 3 (once per daily run, so a broken feed reminds the user daily). Zero configured sources is neither failure nor success.
- WindowsToastNotifier shows the toast through a PowerShell WinRT script under Windows PowerShell's own AUMID because an unpackaged exe has no registered application id; it is clearly marked a stub for M5.6 verification. Title and message are XML-escaped (which also neutralizes apostrophes inside the single-quoted PowerShell literal). The fake notifier and the stub source live in the test project.
- The engine tests deserialize templates/sfp-quarterly.json directly with System.Text.Json (TestManifest) because the M1.2 loader is being built in another worktree; the integrator may swap it for the real loader.

## Integration after wave 1

- **Date slashes are forward slashes.** The PRD lists each slash as "from (900.2, 16.9) to (918.8, 41.0)", which in
  top-left coordinates is a backslash. Those numbers read as pdfplumber bounding-box corners; printed date separators
  are forward slashes, so the manifest's slots.date.slashes now run from the lower-left corner to the upper-right
  corner of each box. Confirm with sfp-measure on page 17 of the real PDF. Only the synthetic fixture draws them.
- **Orchestrator seams.** Generate and Refresh each have one orchestrator interface shared by the CLI and the app:
  IGenerateOrchestrator with IGeneratePreflight (src/Sfp.OneNote/Generate/GenerateContracts.cs) and
  IRefreshOrchestrator with INotebookRegistry and IScheduledTaskService (src/Sfp.Refresh/RefreshContracts.cs).
  Settings persistence is AppSettings and ISettingsStore in Sfp.Core.Options so both front ends share it.
- **Two PDF fixture generators exist.** PdfFixtures (tests/Sfp.Core.Tests/Manifests) builds validator fixtures with
  variations (page count, size, missing probe); SyntheticTemplate (tests/Sfp.TestSupport) builds the full look-alike
  for rendering. Both read the manifest. They stay separate: the validator tests need the variations, the render
  tests need the geometry.

## Calibration against the real PDF (integrator, after wave 1)

The owner placed SFP_No_Dates.pdf on the main branch (docs/SFP No Dates.pdf). It validates against the manifest
(132 pages, 1112 x 770 pt, every probe found). sfp-measure on pages 1, 2, 3, 5, 13, 16, 17 and 24 gave the values
below, now in templates/sfp-quarterly.json. The PRD's "baseline y 47" figures turned out to be ink bottoms: the
32 pt page titles sit on baseline 39.4 (ink 16.5 to 46.6).

| Item | Measured (pt) | Manifest key |
| --- | --- | --- |
| Page title baseline (all pages) | x 23, baseline 39.4, 32 pt | generated.heading.baselineY |
| Weeks-remaining numbers | left edges as the PRD lists, baseline 39.4, ink 32.5 to 39.5; 25 hour labels | slots.weeksRemaining.baselineY |
| Date slashes | (900.2, 41.0) to (918.8, 16.9) and (952.1, 41.0) to (970.7, 16.9); center y 29 | slots.date.slashes |
| Date fields | 15 pt lining digits (about 10.5 pt tall) centered on the slashes: baseline 34.5 | slots.date.baselineY |
| Daily Goals box | x 28.1 to 349.1, rows 87 to 159, 159 to 231, 231 to 303; number column to x 107.6; row lines at 111, 135 (and so on, 24 pt apart) | slots.standingGoals rows at x 111, y 92, 164, 236, w 234, h 16 |
| Daily Metrics tables | x 29.5 to 371, 388.8 to 726.6, 745.6 to 1082.7; rows 690.9 to 714.3 and 714.3 to 738.3; name cells end at x 285.2, 640.8, 996.9 | slots.metricNames cells |
| Time Blocks column | 25 labels (12, 1 ... 12, 1 ... 12) right edge 894, baselines 97.3 + 22.6k; rules x 897.9 to 1082.7 at 95.5 + 22.6k | slots.timeBlocks columnX, columnRight, top 95.5, bottom 638, printedRows 25 |
| Weekly Overview day grid | header box y 309.2 to 338.9, columns at 29.5 + 150.43 i, bottom 724.9; names baseline 331.1 | links.overviewDayHeader, slots.overviewDayColumns |
| Weekly Reflection | Weekly Goal Review table x 574 to 1082.7, y 87.7 to 231.7, Progress % column from x 998.7; "Progress %" 10 pt at x 1012.9 to 1066.7, baseline 81.9; "Changes For Next Week" baseline 536.3 ending at x 759.8 | links.progressScorecard (x 1000, y 71, w 82, erase), links.reflectionNext and the review links beside the label (x 770, y 524) |
| Annual Objectives | block tops 61.0, 234.2, 407.5, 580.5; title band y top to top + 24 from x 28.2 to 711; label cell x 28.2 to 99.3 is blank (nothing to erase) | links.annualObjective erase false; the title slot (slots.annualTitle) goes in the band at x 34, y top + 4, w 670 |
| Objective Details | heading ends at x 285.2; Objective Description lines x 29.5 to 538.2, y 87.5 + 24k | links.objectiveBack at x 300; generated.notes rules 87.5 + 24k, x 29.5 to 1082.7, 0.5 pt |
| Monthly Calendar | day names baseline 103.4 (ink 92.7 to 106.8); grid columns and rows as the PRD; rule x 779.5 to 1082.7 at 50.9 | unchanged |
| Cover | logo image y about 320 to 420, x 261 to 850; copyright baseline 746.7 | coverLabel baselines 470 and 492 stay |

- **Overview header erase is per cell.** Erasing one band across the header would also erase the seven column rules
  inside the header box, so links.overviewDayHeader.eraseInsetX = 1 erases each cell separately (OverviewDayHeaderAnchor.EraseCell).
- **Progress % becomes the link text.** The printed label is erased and "Progress %" is written as the link to the
  scorecard, so the sheet reads the same. The quarterly review uses the same spot for "Scorecard".
- **Navigation beside "Changes For Next Week".** reflectionNext reads "Next week" (its target title would be the
  uninformative "Weekly Overview"); the quarterly review link keeps the target title ("Q1 2027 Review").
- Goldens recorded before this calibration (the synthetic fixture reads the manifest) are re-recorded deliberately.
