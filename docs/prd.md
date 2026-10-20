# SFP to OneNote Converter PRD

Oct 19, 2026 · @Topher Williams

A small Windows app that takes the Structured Focus Planner quarterly PDF plus a start month and builds a complete, fully dated OneNote notebook: a tab per week plus five for the quarter, reviews and notes, about 144 pages, every page a locked background image you can type over on Windows and ink over on an iPad.

## Problem, goals and non-goals

The SFP quarterly planner is a 132-page landscape PDF with blank date slots (two slashes at the top of every weekly and daily page) and no OneNote structure. Using it today means either printing it into OneNote as flat pictures with no sections, or hand-writing 120-plus dates every quarter. Both lose the planner's organization and neither gives a page that stays fixed under keyboard text and pen ink.

### Goals

1. One input, one output. The user picks the quarter's first month, points at the PDF, and gets a new OneNote notebook ready to open.
2. Native organization. The planner's table of contents becomes OneNote section tabs; each week's nine pages become OneNote pages and subpages; nothing is a single long scroll of images.
3. Dates filled in. Every date slot is populated in the template's own typeface (Bookman Old Style), at the same size and position as the printed slashes, so it reads as part of the page.
4. Fixed page art. The printed page content (lines, labels, headings, dates) cannot be moved, selected or deleted by accident on any client. Typing and inking happen on top of it.
5. Works on both devices. Open on Windows (keyboard), open on iPad (Apple Pencil); edits sync through the same notebook with no per-device setup beyond signing in.
6. Repeatable. Run it again next quarter with a new date and get a second notebook with no edits to code.

### Non-goals

- Pixel-exact reproduction of the PDF is not required. Page art may be regenerated as long as the layout, labels and writing areas are kept.
- No editing of user content, ever. Generate creates a notebook; Refresh changes only objects the tool created in a notebook it built (see Refresh and propagation). Past quarters' notebooks are never touched.
- No support for OneNote for Windows 10 (retired 14 October 2025) or OneNote on the web as the creation target. The desktop OneNote app on Windows (Office 2016 or later, perpetual or subscription) is the only creation target in v1.
- No general PDF-to-OneNote converter. The tool knows this one template, described by a manifest file, and refuses PDFs that do not match it.
- No redistribution of the template. The PDF is the user's licensed copy and stays on the user's machine.

* No pre-placed text containers, tables or other editable objects on any page, apart from the small link anchors described under Page links. The user may type, write or draw anywhere, so the page carries the locked sheet image plus those anchors and nothing else. Every date, day number, month name and week indicator is drawn into that image by the software; nothing date-related is left for the user to fill in.

## Users and the two editing modes

There is one user: the planner's owner, who runs the tool once per quarter on a Windows PC and then lives in the notebook from two devices. The tool is a utility, not a product; it can assume Office is installed and the user can find a folder.

### Mode A: Windows, keyboard

OneNote desktop on Windows. The user clicks anywhere on a page and types. OneNote creates a text container at the click point, which is the behavior wanted. The page art must not be selectable, so a click never grabs the picture, and Delete never removes it. Text typed over a ruled line will not snap to that line; that is accepted. OneNote's default font applies to typed text; the tool does not try to force Bookman for user text.

### Mode B: iPad, Apple Pencil

OneNote for iPad, same notebook via OneDrive. The user opens the Draw tab and writes or draws anywhere. The page art stays put under the ink. Pencil strokes sync back to Windows as ink objects. The iPad renders the page at its own zoom; the page must stay legible when the full width is fitted to an iPad screen in landscape, which drives the DPI choice in the rendering section.

### Shared expectations

- The page list on both devices shows real names (for example "Mon Jan 4" or "Weekly Overview"), not "Untitled page".
- Section tabs follow the planner's table of contents so a user who knows the paper planner finds things where expected.
- Nothing the tool puts on a page shows up as an editable object. The only editable content is what the user adds afterward.

## Source template anatomy

The PDF (SFP\_No\_Dates.pdf, 132 pages, 1.2 MB, produced by iOS Quartz PDFContext) is entirely vector apart from the cover logo. Every page is 1112 x 770 points (15.44 x 10.69 in, landscape). All measurements below are in PDF points with the origin at the top-left corner, measured with pdfplumber on the supplied file.

### Page map

| PDF pages | Count | Page type | Date slots on the page |
| --- | --- | --- | --- |
| 1 | 1 | Cover (SFP logo, copyright line) | none |
| 2 | 1 | Contents (section list with page numbers) | none |
| 3 to 4 | 2 | Annual Objectives (4 objective blocks per page, 8 total) | none |
| 5 to 12 | 8 | Objective Details (one per objective) | none |
| 13 to 15 | 3 | Monthly Calendar (Monday-first, 7 x 5 grid) | month name line; 35 day-number boxes |
| 16, 25, 34 ... 124 | 13 | Weekly Overview (goals, plan, 7-day grid) | MM / DD / YY |
| 17 to 23, 26 to 32 ... | 91 | Daily page (Monday through Sunday) | MM / DD / YY; weeks-remaining circle |
| 24, 33, 42 ... 132 | 13 | Weekly Reflection | MM / DD / YY |

Each week is a fixed 9-page run: Overview, Mon, Tue, Wed, Thu, Fri, Sat, Sun, Reflection. Week N starts at PDF page 16 + 9(N - 1).

### Daily page regions

- Day name heading, 32 pt, top-left (x 23, baseline about y 47).
- "Weeks Remaining In Quarter" strip, 10 pt, x 386 to 723, y 30 to 42. Numbers 13 down to 1 at x 537, 556, 575, 594, 616, 628, 641, 654, 666, 679, 691, 704, 717. One number sits in a filled dark circle about 12 pt across with the digit in white.
- Date slashes, top-right: two 1 pt lines from (900.2, 16.9) to (918.8, 41.0) and from (952.1, 16.9) to (970.7, 41.0). Same on Weekly Overview and Weekly Reflection pages.
- Daily Goals block (3 numbered rows, 3 lines each), Follow Up list, Plan list with checkbox gutters, Time Blocks column with 24 hourly rows labelled 12, 1, 2 ... 11, 12, 1 ... 12 (7 pt), Daily Metrics with three two-row tables at the bottom.

### Monthly Calendar regions

- Month name line: a rule from x 779.5 to 1082.7 at y 50.9. Nothing is printed above it; this is where the month and year go.
- Day headers Monday through Sunday at y 92.
- Grid: 7 columns at x 28.8, 149.9, 271.1, 392.2, 513.3, 634.5, 755.6 (pitch 121.1); 5 rows with tops at y 113.2, 233.2, 353.1, 473.0, 592.9 (pitch 120). Each cell has a 30.3 pt square day-number box at its top-left, filled #F2E7DD, plus a strip of 16 x 15 pt tracker boxes along the cell bottom.
- A 26-line notes column from x 900.6 to 1082.7.
- The grid has 5 rows only. A Monday-first month can need 6 rows (November 2026 does). The date engine section says what to do.

### Typography and color

| Element | Value |
| --- | --- |
| Typeface | Bookman Old Style (TrueType, embedded subsets, WinAnsi) |
| Page titles | 32 pt |
| Section labels (Daily Goals, Plan, Weekly Goals ...) | 15 pt |
| Small labels (weeks remaining, Progress %) | 10 pt |
| Time block hour labels | 7 pt |
| Page background | RGB (0.871, 0.831, 0.784) = #DED4C8 |
| Ink (text and rules) | RGB (0.118, 0.118, 0.125) = #1E1E20 |
| Day-number box fill | RGB (0.949, 0.906, 0.867) = #F2E7DD |
| Rule weights | 0.12, 0.25, 0.42, 0.5, 0.78 and 1.0 pt |

Bookman Old Style ships with Microsoft Office on Windows, so it is present on the machine that runs the tool. The tool must still check for it and fall back to TeX Gyre Bonum (a free Bookman clone that can be bundled) if it is missing.

## Target notebook structure

One notebook per quarter. The planner's contents page becomes the section tab row; each week is its own section so the daily pages stay together and the tab row reads Week 01 ... Week 14 left to right (13 weeks only when the quarter starts on a Monday). Subpage levels (OneNote supports three) carry the Overview / days / Reflection hierarchy inside a week.

### Naming

| Object | Name pattern | Example (Q1 2027) |
| --- | --- | --- |
| Notebook | SFP {Qn} {YYYY} | SFP Q1 2027 |
| Section 1 | Quarter | Quarter |
| Section 2 | Objectives | Objectives |
| Section 3 | Calendar | Calendar |
| Sections 4 onward (13 or 14) | Week {NN} ({Mon D} to {Mon D}) | Week 01 (Jan 4 to Jan 10) |
| Weekly Overview page | Weekly Overview | Weekly Overview |
| Daily page | {Ddd} {Mon} {D} | Mon Jan 4 |
| Weekly Reflection page | Weekly Reflection | Weekly Reflection |
| Monthly Calendar page | {Month} {YYYY} | January 2027 |
| Objective Details page | Objective {n} | Objective 3 |

The quarter label {Qn} comes from the start month (January = Q1, April = Q2, July = Q3, October = Q4). A quarter starting in any other month is labelled by its month span instead, for example Feb to Apr 2027. The user can override the notebook name.

### Hierarchy

- Notebook: SFP Q1 2027
  - Section: Quarter
    - Cover (level 1), the SFP cover page with the quarter label and date range added below the logo
    - Contents (level 1), a native OneNote page of links to every section and page (see OneNote writing)
    - Setup (level 1), the slots the user fills for objectives, standing goals, metric names, habits and calendar sources (see Refresh and propagation)
  - Section: Objectives
    - Annual Objectives (level 1), PDF page 3
    - Annual Objectives, continued (level 2), PDF page 4
    - Objective Details (level 1), an empty parent page holding the eight subpages
    - Objective 1 ... Objective 8 (level 2), PDF pages 5 to 12
  - Section: Calendar
    - January 2027, February 2027, March 2027 (level 1), the three months that hold the quarter
  - Section: Week 01 (Jan 1 to Jan 3)
    - Weekly Overview (level 1)
    - Fri Jan 1, Sat Jan 2, Sun Jan 3 (level 2, one page per quarter day in the week, up to seven)
    - Weekly Reflection (level 1)
  - Sections Week 02 through Week 14 (13 or 14 weeks in all), same shape; Week 02 is Jan 4 to Jan 10 with all seven days
  - Section: Reviews
    - Quarter scorecard, January Review, February Review, March Review, Q1 2027 Review (level 1)
  - Section: Notes
    - Backlog, Ideas, Notes, Contacts (level 1), ruled sheets

Page order inside each section is the PDF order. All subpages are created expanded, not collapsed, so a fresh notebook shows every page in the list.

### Alternatives considered

- One "Weeks" section group holding the 13 week sections. Keeps the tab row short (3 tabs plus a group). Rejected for v1 because section groups add a click on iPad and hide the week tabs; offered as a config switch (groupWeeks: true).
- Days as level-1 pages with Overview and Reflection at the same level. Flatter, but the page list becomes 117 same-weight entries. Rejected.
- One section per month with weeks inside. Weeks cross month boundaries, so this breaks the 9-page run. Rejected.

## Date engine

All dates derive from one input: the quarter's first month M (a year and a month). The quarter is the three calendar months M, M+1 and M+2, from S (the 1st of M) through E (the last day of M+2), which is 90, 91 or 92 days. Weeks stay Monday to Sunday because the printed Weekly Overview grid and the day-name pages are built that way, so the first and last weeks of a quarter are normally partial. The engine is a pure function with no I/O so it can be unit-tested on its own.

### Start rules

- The input is a month, never a day. The UI shows a month picker defaulting to the first month of the next calendar quarter (January, April, July or October).
- Any month is accepted for anyone running off-cycle quarters. The {Qn} label is derived only when M is January, April, July or October; otherwise the label is the month span, for example "Feb to Apr 2027".
- Years 1900 to 2199. Leap years are handled by the platform date library, never by hand. Time zones do not apply.

### Week and day math

| Value | Formula | Q1 2027 example |
| --- | --- | --- |
| S, E | 1st of M, last day of M+2 | 2027-01-01 (a Friday), 2027-03-31 |
| Quarter length L | E - S + 1 | 90 |
| Offset o | weekday of S, Monday = 0 ... Sunday = 6 | 4 |
| Week count N | ceil((o + L) / 7), always 13 or 14 | 14 |
| Week k start | Monday on or before S, plus 7(k - 1) | week 1: 2026-12-28; week 14: 2027-03-29 |
| Week k end | week k start + 6 | week 1: 2027-01-03; week 14: 2027-04-04 |
| Quarter days in week k | the days of week k that fall in S to E | week 1: Jan 1 to 3; week 14: Mar 29 to 31 |
| Weekly Overview date | week k start (the Monday, even when it precedes S) | week 1: 12 / 28 / 26 |
| Weekly Reflection date | week k end (the Sunday, even when it follows E) | week 14: 04 / 04 / 27 |
| Weeks remaining shown on week k | N - k + 1 | week 1: 14; week 14: 1 |

N is 13 only when the quarter starts on a Monday and is at most 91 days (Q2 2024, Apr 1 to Jun 30, is one); every other quarter has 14 weeks. The PDF holds 13 week runs, so the 14th week reuses the art of the 13th.

### Daily pages

- A daily page is generated for every day from S to E and for no other day. Week 1 of Q1 2027 therefore has three daily pages (Fri Jan 1, Sat Jan 2, Sun Jan 3) and week 14 has three (Mon Mar 29 to Wed Mar 31); weeks 2 to 13 have seven.
- Each daily page uses the PDF page whose printed day name matches the date's weekday, so a Friday always gets the Friday art.
- Config includeOutOfQuarterDays (default false) adds the days of week 1 and week N that fall outside the quarter, with their date drawn at 40% ink so they read as outside the quarter. Off by default so two consecutive notebooks never both hold Dec 28 to 31.
- Week section names use the in-quarter span: "Week 01 (Jan 1 to Jan 3)", "Week 02 (Jan 4 to Jan 10)", "Week 14 (Mar 29 to Mar 31)".

### Weeks-remaining indicator

In the supplied PDF the dark circle sits on the number equal to the week number: week 1 circles "1", week 13 circles "13". Read against the label "Weeks Remaining In Quarter" that is backwards. The tool redraws the whole strip: it erases the printed numbers, draws N numbers (14 or 13) counting down from N across the same span, and circles N - k + 1 on week k. With 14 numbers the pitch tightens from 14.9 pt to about 13.8 pt, which still clears the label on the left. Config weeksRemainingMode: corrected (default) | asPrinted, where asPrinted circles k.

### Date slot format

The two printed slashes imply three fields. Format is MM / DD / YY with zero-padded fields and a two-digit year ("01 / 01 / 27"). Config allows unpadded fields and day-month-year order; the year stays two digits. The slashes are page art and are kept; only the three fields are drawn.

### Monthly calendar rules

- Exactly three calendar pages: M, M+1, M+2. The PDF's three calendar pages map one to one.
- Month name: "{MonthName} {YYYY}" drawn in 15 pt above the rule at x 779.5 to 1082.7, y 50.9, left-aligned at x 785 with the baseline at y 47.
- Day numbers: 10 pt, centered in each 30.3 pt day-number box, Monday-first grid.
- Leading days from the previous month and trailing days from the next are drawn in the ink color at 40% opacity so the week shape is visible (Dec 28 to 31 in January's first row, Apr 1 to 4 in March's last row). Config can leave them blank.
- Six-row months: the grid has 5 rows. When a Monday-first month needs 6 rows (November 2026 does; Nov 1 is a Sunday), the overflow days (30 and 31, or just 30) are stacked in the fifth-row box of the same weekday as "23 / 30", the way paper planners handle it. Config sixthRow: stack | drop.

### Cover and section labels

- Cover page text under the logo: "{Qn} {YYYY}" on one line and "{Mon D, YYYY} to {Mon D, YYYY}" on the next, 15 pt, centered, ink color. Example: "Q1 2027" and "Jan 1, 2027 to Mar 31, 2027".
- Week section names use the short in-quarter date span without years; the notebook name carries the year.

### Worked example

M = January 2027. S = Fri Jan 1, E = Wed Mar 31, L = 90, o = 4, N = 14. Week 01 runs Mon Dec 28 to Sun Jan 3 and holds three quarter days; its Weekly Overview is dated 12 / 28 / 26 and circles 14. Week 02 is Jan 4 to Jan 10 and circles 13. Week 14 runs Mar 29 to Apr 4, holds Mar 29 to 31, is dated 04 / 04 / 27 on its Reflection and circles 1. Calendar pages: January, February, March 2027. Notebook: SFP Q1 2027 with 19 sections (Quarter, Objectives, Calendar, Week 01 to Week 14, Reviews, Notes) and 144 pages (3 + 11 + 3 + 14 overviews + 90 days + 14 reflections + 5 reviews + 4 notes).

## Page rendering

Each OneNote page gets one PNG: the PDF page rasterized, with the date fields, calendar numbers and corrected weeks-remaining strip drawn on top before the bitmap is saved. Baking the dates into the image is what makes them immutable; OneNote has no way to lock a text object, but it does lock background images.

### Rasterization

- Source page rendered at 220 DPI by default (3398 x 2353 px for 1112 x 770 pt). Config allows 144, 220 or 300. 220 keeps 7 pt hour labels readable on an iPad Pro at fit-to-width and keeps a 144-page notebook near 45 MB.
- Output format PNG, 24-bit RGB, no alpha. The beige background is opaque in the PDF so there is nothing to preserve.
- Renderer: PDFium (via the PDFtoImage NuGet package, which returns SkiaSharp bitmaps) on .NET, or PyMuPDF if the Python build is chosen. Windows.Data.Pdf is a zero-dependency option but its text antialiasing is poor at high DPI and it cannot render to an offscreen bitmap without WinRT interop; it is the fallback, not the default.
- The same source page is rendered once and cached when it is reused (the 3 calendar pages, and any page type where the art repeats). Daily pages differ only by day name and position of the circle, but they are rendered from their own PDF page to avoid any drift; a 14th week reuses the 13th week's PDF pages.

### Overlay drawing

All overlay coordinates are in PDF points and scaled by DPI / 72 at draw time. Text is drawn with the template typeface at the template size, ink color #1E1E20, antialiased, no hinting changes.

| Slot | Pages | Font | Anchor | Position (pt) |
| --- | --- | --- | --- | --- |
| Date month (MM) | Overview, daily, Reflection | 15 pt | right-aligned | right edge x 895, baseline y 37 |
| Date day (DD) | same | 15 pt | centered | center x 935, baseline y 37 |
| Date year (YY) | same | 15 pt | left-aligned | left edge x 976, baseline y 37 |
| Weeks-remaining strip | daily | 10 pt | per-number left edges | x 537.3, 556.1, 574.9, 593.7, 615.7, 628.3, 640.9, 653.5, 666.1, 678.7, 691.3, 703.9, 716.5; baseline y 40 |
| Weeks-remaining circle | daily | 12 pt filled circle, digit 10 pt white | centered on the chosen number | same x as that number's center, center y 36 |
| Month name | calendar | 15 pt | left-aligned | x 785, baseline y 47 |
| Day numbers | calendar | 10 pt | centered in box | box x from the 7 column origins + 0, y from the 5 row origins + 0, 30.3 x 30.3 |
| Cover label | cover | 15 pt | centered | x 556, baselines y 470 and y 492 |

The date baseline of 37 puts the cap height of 15 pt Bookman (about 10 pt) centered on the slashes, which run from y 16.9 to 41.0. Engineering should confirm these numbers against a rendered page before locking them, then store them in the manifest, not in code.

### Weeks-remaining strip redraw

1. Paint a background-colored rectangle over x 530 to 728, y 28 to 44 to erase the printed numbers and circle.
2. Draw N numbers (14 or 13) counting down from N, evenly spaced across x 537 to 723, in 10 pt ink. With 13 the printed positions above are used exactly.
3. Draw a filled ink circle of 12 pt diameter centered on the target number, then that number again in white on top.

The label "Weeks Remaining In Quarter" at x 386 to 528 is outside the erased rectangle and stays as printed.

### Font handling

- Load Bookman Old Style from the Windows font directory by family name. Verify the loaded face reports the same family; a silent substitution (Arial) must be an error, not a warning.
- If missing, load the bundled TeX Gyre Bonum and log a warning that the date glyphs will differ slightly from the printed labels.
- Digit widths differ between the two faces, so field anchors are right/center/left aligned rather than fixed-start, as in the table.

### Image sizing in OneNote

The PNG is placed at exactly 1112 x 770 points regardless of DPI, so the page appears at its printed size and 1 image pixel maps to 72/DPI points. Config pageScale (default 1.0) can shrink the placed size, for example 0.72 to fit an 11 in wide Letter-landscape page; the image is still rendered at the chosen DPI.

## OneNote writing

The notebook is created through the OneNote desktop COM API (OneNote.Application, the 2013 interface, shipped with every OneNote desktop build since 2013, including the perpetual Office 2021 and Office 2024 editions). It is the only API that can create a notebook from a folder path, set page levels, and mark an image as a locked background. Microsoft Graph cannot do the last two, and there is no supported writer for the .one file format.

### Prerequisites checked at startup

- OneNote desktop (onenote.exe, version 16.x) is installed and the OneNote.Application ProgID resolves. The host runs the perpetual-license OneNote that ships with Office 2024; it is the same desktop binary as the subscription build, exposes the same COM API, and signs in to a Microsoft account for OneDrive notebooks. OneNote for Windows 10 (UWP) does not expose COM and is out of support, so its presence is ignored.
- The user is signed in to OneNote with the Microsoft account that will own the notebook, so a later move to OneDrive is one click.
- The output folder is writable and is not inside a OneDrive sync folder (OneNote desktop refuses local notebooks there).

### Call sequence

1. OpenHierarchy(outputFolder\\{NotebookName}, "", out notebookId, cftNotebook) creates the notebook on disk and opens it.
2. For each section: OpenHierarchy(notebookId path \\{SectionName}.one, notebookId, out sectionId, cftSection). Sections are created in display order; OneNote orders tabs by creation order within a notebook.
3. Create every page in the notebook first: CreateNewPage(sectionId, out pageId, npsBlankPageWithTitle) for each, recording the IDs, then UpdateHierarchy per section to set names, order and pageLevel. Next, GetHyperlinkToObject for every page and section, which the link anchors and the Contents page need. Only then UpdatePageContent(pageXml, DateTime.MinValue) for each page with the XML below plus its link outlines.
4. Write the Contents page as a native OneNote outline of links, using the hyperlinks gathered in step 3. This replaces the PDF's page-number contents with something that works in OneNote.
5. SyncHierarchy(notebookId) at the end, then NavigateTo(first page) so OneNote opens showing the cover.

### Page XML

One outline containing one image, marked as the page background, positioned at the page origin. Title is set so the page list shows a real name.

```xml
<one:Page xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote" ID="{pageId}" lang="en-US">
  <one:PageSettings RTL="false" color="#DED4C8">
    <one:PageSize><one:Automatic/></one:PageSize>
    <one:RuleLines visible="false"/>
  </one:PageSettings>
  <one:Title><one:OE><one:T><![CDATA[Mon Jan 4]]></one:T></one:OE></one:Title>
  <one:Outline>
    <one:Position x="0" y="{titleOffset}" z="0"/>
    <one:Size width="1112" height="770" isSetByUser="true"/>
    <one:OEChildren><one:OE>
      <one:Image format="png" backgroundImage="true">
        <one:Size width="1112" height="770" isSetByUser="true"/>
        <one:Data>{base64 PNG}</one:Data>
      </one:Image>
    </one:OE></one:OEChildren>
  </one:Outline>
</one:Page>
```

Notes on the XML:

- backgroundImage="true" is the attribute behind the UI command "Set Picture as Background". It makes the image unselectable and unmovable on Windows, Mac and iPad until a user explicitly unsets it. This is the mechanism that satisfies the immutability goal.
- Position and Size are in points, which is why the renderer places the image at 1112 x 770 regardless of pixel size.
- titleOffset is the height of the title area (about 36 pt when the title is one line). The first build must measure where OneNote actually places an outline at y = 0 on a titled page and adjust; if the title region cannot be overlapped, the image sits just below it. A config option hideTitle: true creates pages with npsBlankPageNoTitle and places the image at y = 0, at the cost of OneNote naming the page from the image (it shows as "Untitled page"), so the default is a visible title.
- PageSettings color is set to the page beige so the area outside the image, visible when scrolling, matches. If OneNote rejects a hex color here (the schema may only accept its fixed palette), fall back to "automatic".
- PageSize stays Automatic. A custom size would print well but iPad ignores it. Config pageSize: automatic | custom for people who print.
- The exact attribute set must be validated against the published 2013 schema file (0336.OneNoteApplication\_2013.xsd) during the first spike; UpdatePageContent rejects unknown attributes with a COM error rather than ignoring them.

### Page levels and ordering

UpdateHierarchy is called once per section with the section's full page list, each page element carrying pageLevel="1" or "2" and in the intended order. OneNote preserves the order given. Daily pages are level 2; Overview and Reflection are level 1. The Objective Details parent page is level 1 with its eight children at level 2.

### Notebook location and sync to iPad

v1 creates the notebook as a local folder (default: Documents\\OneNote Notebooks\\SFP Q1 2027). The desktop COM API cannot create a notebook directly in consumer OneDrive, and OneNote desktop blocks notebooks placed under the OneDrive sync client's folder. So the last screen of the app tells the user: in OneNote, File, Share, Move Notebook to OneDrive, then open the notebook on the iPad. The app can open OneNote at that dialog's page but cannot press the button.

v2 option: create the notebook in OneDrive first through Microsoft Graph (POST /me/onenote/notebooks), then OpenHierarchy with the notebook's OneDrive URL (the d.docs.live.net path OneNote returns in GetHierarchy) so the desktop app fills a cloud notebook directly. This removes the manual move but adds an Azure app registration and a sign-in flow; see Phasing.

### Throughput

UpdatePageContent with a 300 KB base64 image takes roughly 0.3 to 0.8 s on a modern PC. About 144 pages plus hierarchy calls should finish in under 3 minutes. COM calls are made from a single STA thread; the UI stays responsive by running that thread separately and posting progress.

## Page links

The notebook is cross-linked so the user can move between the planner's levels by clicking, not by hunting in the page list. A locked background image cannot carry per-region hyperlinks, so every link is a small native OneNote object placed over the sheet at the spot where the planner prints that element. Link anchors are the one exception to the rule that pages hold nothing editable; they are kept small and few.

### Link map

| From page | Anchor on the page | Links to | Count |
| --- | --- | --- | --- |
| Annual Objectives (two pages, four blocks each) | "Objective n" in the block's left label cell | Objective n detail page | 8 |
| Objective n detail page | "Objective n" beside the page heading | the Annual Objectives page that holds block n (page 1 for 1 to 4, page 2 for 5 to 8) | 8 |
| Monthly Calendar | the day number in each day box | that day's daily page | one per quarter day in the month |
| Monthly Calendar | "Week NN" in the Monday cell of each row, right of the day box | that week's Weekly Overview | 4 to 6 per month |
| Daily page | the date (MM / DD / YY) at the top right | the Weekly Overview of its week | 1 |
| Weekly Overview | each day name in the grid header (Monday ... Sunday) | that day's daily page | one per quarter day in the week |

Days outside the quarter (greyed in a calendar grid, or the empty days of a partial first or last week) get no link and stay baked into the image. The Contents page, described under OneNote writing, links to every section and page.

### Mechanism

Each link is its own one:Outline holding one one:OE with a single one:T whose CDATA is an HTML anchor around a styled span:

```xml
<one:Outline>
  <one:Position x="28.8" y="121.2" z="1"/>
  <one:Size width="30.3" height="14" isSetByUser="true"/>
  <one:OEChildren>
    <one:OE alignment="center">
      <one:T><![CDATA[<a href="onenote:#Mon%20Jan%204&section-id={...}&page-id={...}&end"><span style='font-family:Bookman Old Style;font-size:10.0pt;color:#1E1E20'>4</span></a>]]></one:T>
    </one:OE>
  </one:OEChildren>
</one:Outline>
```

- The href comes from GetHyperlinkToObject(targetPageId, "") and works on Windows, Mac and iPad within the same notebook.
- Anything that becomes a link is left out of the rendered bitmap (the manifest marks the slot mode: baked or link) so it is not drawn twice. For the daily-page date the printed slashes are erased and the full "01 / 04 / 27" string is the link text; for the Weekly Overview header the printed day names are erased and redrawn as links; calendar day numbers and week labels are simply never baked.
- Font, size and color match the template so a link reads as part of the sheet. OneNote underlines hyperlinks and may ignore the span color; the Phase 0 spike checks whether text-decoration:none and the color are honored. If not, underlined ink-colored text is accepted.
- z="1" puts link outlines above the background image. Outline width is set explicitly so text never wraps; OneNote adds a few points of internal padding, so positions are calibrated once in the spike and stored in the manifest as offsets.
- Alternative to validate in the spike: a transparent PNG placed as a normal (not background) image with the one:Image hyperlink attribute, sized to the hot-spot. It would keep the baked typography with no underline, at the cost of invisible selectable objects. If it works on both Windows and iPad it replaces text anchors for calendar numbers and the Weekly Overview header; if not, text anchors stand.

### Anchor geometry (PDF points)

| Anchor | Position | Size | Font |
| --- | --- | --- | --- |
| Annual Objectives block n label | x 33, y = block top + 29 (block tops 61.0, 234.2, 407.5, 580.5) | 62 x 14 | 10 pt |
| Objective detail back link | x 300, y 24 | 120 x 20 | 15 pt |
| Calendar day number | x = column origin (28.8, 149.9, 271.1, 392.2, 513.3, 634.5, 755.6), y = row origin (113.2, 233.2, 353.1, 473.0, 592.9) + 8 | 30.3 x 14, centered | 10 pt |
| Calendar week label | x = Monday column origin + 36, y = row origin + 8 | 60 x 14 | 10 pt |
| Daily page date | right edge at x 1010, y 22 | 110 x 20, right-aligned | 15 pt |
| Weekly Overview day header | x = 29.5 + 150.4 i, y 316 | 150.4 x 20, centered | 15 pt |

### Consequences elsewhere

- Page IDs must exist before any page content is written, because the hrefs embed them. The writer therefore creates every page in the notebook first, fetches all hyperlinks, and only then writes content. The call sequence under OneNote writing reflects this.
- Link anchors are native objects, so a user can move or delete one. The tool never repairs a notebook after the fact; a lost link is restored by regenerating the notebook into a new name. A config switch links: on | off removes all anchors for anyone who wants a purely locked sheet.
- In OneNote for iPad, a finger tap on link text follows the link; the Apple Pencil in Draw mode inks over it without triggering it. On Windows a single click follows the link; Ctrl+click is not needed.
- Each link adds roughly 400 bytes of XML. A Q1 2027 notebook gains about 340 anchors (90 calendar day numbers, 15 week labels, 90 daily dates, 90 overview day names, 16 objective links), which is negligible for size and adds a few seconds of UpdatePageContent time.

## Refresh and propagation

The tool has two operations. Generate builds a notebook once. Refresh reopens an existing notebook the tool built, reads what the user has written in a few known places, and updates only the objects the tool itself created. Refresh is what makes objectives, standing goals and calendar events appear across pages without being retyped, and it is what the daily scheduled task runs.

### Setup page

A page named Setup in the Quarter section, rendered in the template style, with ruled slots the user fills in OneNote (typed or inked) after the notebook is generated:

| Slot | Count | Where it is propagated |
| --- | --- | --- |
| Objective titles | 8 | Annual Objectives header rows; the objective legend on every Weekly Overview and daily page; the Objective n heading on each detail page; the Quarter scorecard rows |
| Standing daily goals | up to 3 | Faint pre-fill on the first line of Daily Goals rows 1 to 3 on every daily page |
| Daily metric names | 3 | The name cell of the three Daily Metrics tables on every daily page |
| Habit labels | up to 16 | A habit legend under each monthly calendar's tracker strips |
| Calendar sources | up to 4 | Calendar import (next section) |
| Time-block window | start and end hour | Daily page Time Blocks column at the next Generate; a Refresh cannot re-render baked art |

The Setup page is the single source. As a fallback, if a Setup objective slot is empty but the matching Annual Objectives header row holds typed text, that text is used.

### Reading what the user wrote

- GetPageContent on the Setup page returns its XML. Typed text is read from one:T runs inside outlines whose Position falls within a slot's rectangle.
- Inked text is read from one:InkWord recognizedText attributes in the same rectangles. Recognition confidence is not exposed, so inked values are shown back to the user in the Refresh summary for a glance before they spread to 100 pages; config can require confirmation for inked values.
- A slot with nothing in it propagates nothing. Refresh never invents text.

### Ownership rules

- Every object the tool writes after Generate (a link anchor, a legend, a pre-fill, a calendar event line) is tool-owned. The tool records each one's objectID and its page key in a sidecar file next to the notebook (sfp-state.json) and also writes a page-level one:Meta name="sfp.key" on every page so pages are found by key, not by title, even after the notebook moves to OneDrive and IDs change.
- Refresh rewrites tool-owned objects in place by objectID; if an ID no longer resolves it re-locates the slot by page key and position, and if a user object now sits in that rectangle the tool leaves it alone and reports it.
- Refresh never edits, moves or deletes anything the user created. It never changes the background image. It is idempotent: running it twice produces no second change.
- A dry run (--dry-run or the Preview button) lists every change without writing.

### Propagated text style

Propagated text is written as native text in Bookman Old Style at the slot's size, ink color at 60% for pre-fills (standing goals) and 100% for names and legends, so a pre-fill reads as a suggestion the user can write over. The objective legend is one line of 8 pt text in the free band under each page title (y 50 to 62) in the form "O1 Ship the connector  O2 Hire two PMs ...", truncated per objective to fit 1040 pt.

## Calendar import

Refresh pulls events from the calendar sources on the Setup page and writes them as tool-owned text into the places the planner has for them. Run daily, it keeps the next two weeks of pages current.

### Sources

- An .ics URL (Outlook published calendar, Google secret address, any CalDAV export) or a local .ics file path. Up to four sources, each with an optional label and a color tag.
- Local Outlook via its COM object (MAPI) when Outlook is installed, read-only.
- Microsoft Graph calendar is Phase 3 with the cloud-native option.
- Holidays: a bundled US federal holiday list, switchable, plus any all-day events from the sources.

### Placement

| Page | What is written | Where | Style |
| --- | --- | --- | --- |
| Daily page | timed events for that day | one line per event in the Time Blocks column at the row of its start hour, "9:00 Staff meeting"; events longer than an hour also get a thin tool-drawn line of text at each later hour, "... Staff meeting" | 8 pt, 70% ink |
| Daily page | all-day events and holidays | the free band under the date at the top right | 8 pt, 70% ink |
| Weekly Overview | all events for each day | the day's column in the grid, newest at top, "09:00 Staff meeting", up to 18 lines then "+n more" | 9 pt, 70% ink |
| Monthly Calendar | all-day events and holidays only | under the day-number box, up to 2 lines | 8 pt, 70% ink |

Rules: events marked private are written as "Private" unless config says otherwise; a source can be filtered by calendar name; event titles are cut to fit the column; cancelled events are removed on the next Refresh; a day the user has already passed (before today) is frozen and never changed again, so the record of what was planned stays as it was.

### Daily scheduled run

- The app offers "Refresh every day at 05:00" which registers a Windows Task Scheduler task running sfp2onenote.exe refresh --notebook "SFP Q1 2027" --calendar --horizon 14, set to run only when the user is logged on (OneNote is a per-user application) and to start OneNote if it is not running.
- The run syncs the notebook, reads sources, writes changes for today through today plus the horizon, calls SyncHierarchy and exits. Typical run: under 30 seconds.
- Failures (source unreachable, OneNote busy) are logged and retried at the next run; three consecutive failures raise a Windows toast.
- The iPad sees the result through OneDrive sync, so the notebook must already have been moved to OneDrive; Refresh works on a cloud notebook through the same COM API.

## Navigation and page metadata

- Previous and next links on every daily page ("‹ Sun Jan 3" and "Tue Jan 5 ›" at 10 pt in the band under the title), on every Weekly Overview and Weekly Reflection (previous and next week), and on each monthly calendar.
- Each Weekly Reflection links forward to the next week's Weekly Overview beside the "Changes For Next Week" label, so what was decided is one click from where it is planned.
- Each page's creation timestamp (one:Page dateTime) is set to the planner date at 06:00 local, so OneNote's recent-pages list, date sorting and search by date follow the planner, not the generation run. Whether UpdatePageContent accepts dateTime is a Phase 0 check; if not, lastModifiedTime is left alone and the feature is dropped.
- Week sections are colored by month with one:Section color (three alternating template-compatible tints), so the tab row shows month boundaries.

## Quarter scorecard

One page in a new Reviews section: objectives down (8 rows, titles propagated from Setup), weeks across (13 or 14 columns), one cell per week for the Progress % the user writes on that week's reflection, and a final column for the quarter. Column headers link to each Weekly Reflection; every Weekly Reflection's Progress % label links back to the scorecard. Rendered by the tool in the template's style (same rules, ink and typeface), not taken from the PDF.

## Monthly and quarterly reviews

In the Reviews section: Monthly Review for each of the three months and one Quarterly Review, all reusing the Weekly Reflection art with the heading erased and redrawn ("January Review", "Q1 2027 Review"). The month page of the calendar links to its review; the last Weekly Reflection links to the Quarterly Review; the Quarterly Review links to the scorecard. The quarterly page also carries a "Next quarter" link slot that a later Generate fills in when the next notebook is created with --previous pointing at this one.

## Time-block window

The Setup page (or the Generate screen) sets the Time Blocks window, default 06:00 to 22:00. The renderer rebuilds the column rather than rasterizing it: it erases x 870 to 1082, y 88 to 648, draws one row per hour in the window with the same 0.5 pt rules and the hour label at 9 pt instead of 7 pt. Sixteen rows give a 34.7 pt pitch, which is pen-friendly on an iPad. Events outside the window are listed at the top row prefixed with their time. Because this is baked art it takes effect at Generate; changing it later means regenerating.

## Free-form pages

A Notes section with ruled pages generated in the template style: Backlog, Ideas, Notes and Contacts by default; the list is config. Each is a plain ruled sheet (the Objective Description line pitch of 24 pt, full width) with the page name as its printed heading, and a link back to Contents. They are ordinary locked sheets like every other page.

## Day counter

Every daily page shows "Day 4 of 90 · Week 2 of 14" in 8 pt at 60% ink, right-aligned under the date at y 54, baked into the image. The weeks-remaining strip stays; this is the finer-grained companion to it.

## Application UX

A single-window Windows app (WPF on .NET 8, published as one self-contained .exe) with one screen and a progress state. No installer; the user double-clicks the file. Everything below also works headless from the command line for scripting.

### Main screen fields

| Field | Control | Default | Validation |
| --- | --- | --- | --- |
| Template PDF | file picker | last used path | must exist; page count and page sizes must match the manifest or the Generate button stays disabled with the mismatch shown |
| Quarter start month | date picker | first month of the next calendar quarter | any month, years 1900 to 2199; the day is always the 1st and is not shown |
| Notebook name | text box | SFP {Qn} {YYYY} | 1 to 64 chars; none of \\ / : \* ? " < > \| # % ; a name already present in the output folder blocks Generate |
| Output folder | folder picker | Documents\\OneNote Notebooks | writable; warns if under the OneDrive sync folder |
| Date format | dropdown | MM / DD / YY | from the list in the date engine section |
| Weeks remaining | dropdown | Corrected (14 - N) | Corrected, As printed |
| Calendar months | dropdown | Three | fixed at three; spill days are greyed in the adjacent month grid |
| Render DPI | dropdown | 220 | 144, 220, 300 |
| Advanced (collapsed) | checkboxes |  | group weeks in a section group; page links on or off; hide page titles; custom page size; page scale; six-row month handling; out-of-month day style; Reflection date = week start |

A read-only preview strip under the fields shows three thumbnails rendered from the current settings: the first daily page of the quarter, the first calendar month, and the cover. They re-render when the date or format changes, which is how the user confirms the dates look right before committing 132 pages.

### Refresh screen

A second tab on the same window. It lists the notebooks the tool has built (from their sidecar files), shows when each was last refreshed, and offers: Preview changes (dry run, listing every propagated value and calendar event about to be written, with inked values marked), Refresh now, and a checkbox "Refresh every day at 05:00" that registers or removes the scheduled task. The summary after a run shows values read from Setup, pages touched, events written, and anything skipped because a user object was in the way.

### Generate flow

1. Click Generate. Fields lock. A progress bar shows phases: Checking OneNote, Rendering pages (n of 132), Creating notebook, Writing pages (n of 132), Building contents, Finishing.
2. Rendering runs in parallel on all cores; writing runs on one STA thread. Expected total under 3 minutes.
3. A Cancel button stops after the current page. On cancel the partial notebook is deleted from disk so a retry does not hit the name collision rule.
4. On success: a summary (pages written, notebook path, elapsed time), an Open in OneNote button, and the one-paragraph instruction for moving the notebook to OneDrive with a Copy button for the path.
5. On failure: the phase, the page that failed, the COM HRESULT or exception text, and a Save log button. The partial notebook is left in place for inspection but flagged in the summary.

### Command line

sfp2onenote.exe generate --pdf "C:\\path\\SFP\_No\_Dates.pdf" --start 2027-01 \[--name "SFP Q1 2027"\] \[--out "D:\\Notebooks"\] \[--date-format mm/dd/yy\] \[--weeks-remaining corrected|as-printed\] \[--dpi 220\] \[--render-only "D:\\png"\] \[--manifest custom.json\] \[--quiet\]

--render-only writes the PNGs and a pages.json index without touching OneNote, which is how the overlay positions are tuned and how the test suite runs on a machine without Office. The second verb is sfp2onenote.exe refresh --notebook "SFP Q1 2027" \[--calendar\] \[--horizon 14\] \[--dry-run\] \[--quiet\], which the scheduled task calls. Exit codes: 0 success, 2 validation error, 3 OneNote unavailable, 4 render error, 5 write error, 6 refresh source unreachable.

### Settings persistence

Last-used values are saved to %APPDATA%\\sfp2onenote\\settings.json. Nothing else is written outside the output folder and the log folder (%LOCALAPPDATA%\\sfp2onenote\\logs).

## Template manifest

Everything the tool knows about the PDF lives in one JSON file shipped beside the executable, so a new edition of the planner (different page count, moved date slots) is a manifest edit, not a code change. The tool validates the PDF against the manifest before rendering: page count, page size within 1 pt, and a text probe per page type (for example the word "Monday" present on page 17).

```json
{
  "templateId": "sfp-quarterly-2023",
  "pageSizePt": [1112, 770],
  "pageCount": 132,
  "font": { "family": "Bookman Old Style", "fallback": "TeX Gyre Bonum" },
  "colors": { "background": "#DED4C8", "ink": "#1E1E20" },
  "pageTypes": {
    "cover":      { "pdfPages": [1],  "probe": "Structured Focus Planner" },
    "contents":   { "pdfPages": [2],  "probe": "Contents" },
    "annual":     { "pdfPages": [3, 4], "probe": "Annual Objectives" },
    "objective":  { "pdfPages": [5, 6, 7, 8, 9, 10, 11, 12], "probe": "Objective Details" },
    "calendar":   { "pdfPages": [13], "probe": "Monthly Calendar" },
    "weekly":     { "firstPdfPage": 16, "stride": 9, "count": 13,
                    "pages": ["overview", "mon", "tue", "wed", "thu", "fri", "sat", "sun", "reflection"] }
  },
  "slots": {
    "date": {
      "appliesTo": ["overview", "mon", "tue", "wed", "thu", "fri", "sat", "sun", "reflection"],
      "sizePt": 15, "baselineY": 37,
      "fields": [ { "name": "month", "align": "right",  "x": 895 },
                  { "name": "day",   "align": "center", "x": 935 },
                  { "name": "year",  "align": "left",   "x": 976 } ]
    },
    "weeksRemaining": {
      "appliesTo": ["mon", "tue", "wed", "thu", "fri", "sat", "sun"],
      "erase": [530, 28, 728, 44], "sizePt": 10, "baselineY": 40,
      "numberX": [537.3, 556.1, 574.9, 593.7, 615.7, 628.3, 640.9, 653.5, 666.1, 678.7, 691.3, 703.9, 716.5],
      "circleDiameterPt": 12, "numberSpan": [537, 723]
    },
    "monthName": { "appliesTo": ["calendar"], "x": 785, "baselineY": 47, "sizePt": 15 },
    "dayGrid": {
      "appliesTo": ["calendar"], "sizePt": 10,
      "columnX": [28.8, 149.9, 271.1, 392.2, 513.3, 634.5, 755.6],
      "rowY": [113.2, 233.2, 353.1, 473.0, 592.9], "boxPt": 30.3
    },
    "coverLabel": { "appliesTo": ["cover"], "x": 556, "baselines": [470, 492], "sizePt": 15, "align": "center" }
  },
  "notebook": {
    "sections": [
      { "name": "Quarter",    "pages": [ { "type": "cover", "title": "Cover", "level": 1 }, { "type": "links", "title": "Contents", "level": 1 } ] },
      { "name": "Objectives", "pages": [ { "type": "annual", "index": 0, "title": "Annual Objectives", "level": 1 },
                                          { "type": "annual", "index": 1, "title": "Annual Objectives, continued", "level": 2 },
                                          { "type": "blank", "title": "Objective Details", "level": 1 },
                                          { "type": "objective", "each": true, "title": "Objective {n}", "level": 2 } ] },
      { "name": "Calendar",   "pages": [ { "type": "calendar", "eachMonth": true, "title": "{Month} {YYYY}", "level": 1 } ] },
      { "name": "Week {NN} ({start} to {end})", "eachWeek": true,
        "pages": [ { "type": "overview", "title": "Weekly Overview", "level": 1 },
                   { "type": "day", "each": true, "title": "{Ddd} {Mon} {D}", "level": 2 },
                   { "type": "reflection", "title": "Weekly Reflection", "level": 1 } ] }
    ]
  }
}
```

Rules for the manifest:

- Coordinates are PDF points, top-left origin, matching the measurement section. The renderer owns the DPI conversion.
- Title and section name patterns use a small token set: {NN}, {n}, {start}, {end}, {Ddd}, {Mon}, {D}, {Month}, {YYYY}, {Qn}. Unknown tokens are an error at load time.
- A user manifest passed with --manifest is merged over the shipped one key by key, so a tweak to one baseline is a three-line file. A links block (not shown above) lists each anchor type with its position, size, font, alignment, erase rectangle and target rule, following the Page links section; slots that become links carry mode: link so the renderer skips them.

## Architecture

&#91;embedded content: generation pipeline · 3 inputs, 3 stages, 1 manual step\]

The date engine and renderer are plain libraries with no OneNote dependency, which is what lets the golden-image tests run on a machine without Office. Only the COM writer touches OneNote, behind an INotebookWriter interface so a Graph-based writer can be added in Phase 3 without changing the rest.

| Module | Responsibility | Depends on |
| --- | --- | --- |
| Manifest | Load, merge and validate the template description; probe the PDF | PDF text extraction |
| Date engine | Quarter, week, day and calendar math; name and label formatting | nothing |
| Renderer | Rasterize a PDF page, draw overlays, emit PNG bytes | PDFium, SkiaSharp, Manifest, Date engine |
| COM writer | Create notebook, sections, pages; set levels; build Contents links | OneNote.Application interop |
| Orchestrator | Walk the manifest's notebook plan, drive renderer and writer, report progress | all of the above |
| UI / CLI | Collect inputs, show previews and progress, persist settings | Orchestrator |

### NoteCraft as a second Windows host

[NoteCraft](https://github.com/AlienTechSoftware/notecraft) is an open-source, clean-room reimplementation of OneNote in Rust that may replace OneNote on the Windows PC. As of 19 October 2026 it is version 0.0.1 with no published release, Windows x64 only, no iOS or iPad build, and no sync service of its own (it stores notebooks as plain folders and leaves sync to OneDrive, Dropbox or Syncthing). Its .one support is a read-only importer, and the README says nothing about ink or locked background images.

What that means for this tool:

- NoteCraft cannot serve the iPad half of the goal today. Until it writes .one files or ships on iOS, the iPad still needs a real OneNote notebook on OneDrive, which only the COM writer can produce. The COM writer therefore stays the primary target.
- NoteCraft is the cheapest writer to add. Its format is a folder tree: a notebook folder with \_node.toml, section folders, and one folder per page holding index.html plus an assets folder, with sub-pages nested to any depth. A NoteCraftWriter behind the same INotebookWriter interface emits that tree directly: no COM, no XML, one PNG per page referenced by an img tag, and metadata in front matter or meta tags. It also has a CLI and an MCP server, so the tool can ask NoteCraft to open or validate the result.
- Open questions to settle before building it: whether NoteCraft can lock a page image the way OneNote's background image does, whether pen input exists at all, and whether its page rendering keeps a 1112 x 770 pt image at a fixed size. These go into Phase 0 as a second spike if NoteCraft is adopted.
- The manifest's notebook plan (sections, page titles, levels) is writer-independent, so both writers produce the same hierarchy from the same plan.

## Non-functional requirements

| Area | Requirement |
| --- | --- |
| Platform | Windows 10 22H2 or Windows 11, x64. .NET 8 self-contained build, no runtime install. OneNote desktop 16.x present (perpetual Office 2024 on the host; Microsoft 365 also works). |
| Performance | Full run under 3 minutes on a 4-core laptop; rendering under 60 s; memory under 1.5 GB peak (pages are rendered, written and released one at a time, never held all at once). |
| Notebook size | Under 60 MB at 220 DPI for 144 pages. A size estimate is shown in the UI before Generate based on DPI. |
| Determinism | Same PDF, same date, same settings produce byte-identical PNGs. The test suite relies on this. |
| Reliability | Any COM failure is retried once after 2 s (OneNote occasionally returns RPC\_E\_CALL\_REJECTED while busy). A second failure aborts with the page identified. |
| Logging | One log file per run with timestamps, every COM call name and duration, every rendered page, and the full page XML for any page that failed. No image data in the log. |
| Security | No network calls in v1. Nothing leaves the machine. v2 Graph sign-in uses the Microsoft Authentication Library with the token cached in the Windows credential store. |
| Accessibility | Standard WPF controls with labels, keyboard navigation and high-contrast support; no custom-drawn controls apart from the preview thumbnails. |
| Licensing | The SFP PDF is copyrighted by Structured Focus Planner (2020-2023). The tool is for the owner's personal use with a legally obtained copy; the executable ships without the PDF and the README says so. Bundled fonts must be under a license that allows redistribution (TeX Gyre Bonum is under the GUST Font License). |
| Code | C# with nullable enabled, analyzers on, xUnit tests, one solution, MIT license for the tool itself. A Python prototype (PyMuPDF plus pywin32) is acceptable for the first spike but the shipped tool is the .NET build. |

## Error handling

| Condition | Detection | What the user sees | Outcome |
| --- | --- | --- | --- |
| OneNote desktop not installed, or only the UWP app | ProgID lookup fails | "OneNote desktop (Office 2016 or later) is required. OneNote for Windows 10 is not supported." with a link to install OneNote | Exit 3 |
| OneNote not signed in | GetHierarchy returns no notebooks and the first OpenHierarchy fails | "Open OneNote and sign in once, then retry." | Exit 3 |
| PDF does not match manifest | page count, page size or text probe mismatch | The specific mismatch ("Expected 132 pages, found 130") | Generate disabled |
| Start month outside 1900 to 2199 | date picker | Inline message under the month picker | Blocked until fixed |
| Notebook name already exists in output folder | folder check | "A notebook named X already exists here. Rename or choose another folder." | Blocked |
| Output folder under OneDrive sync | path compare against the OneDrive env var | Warning with a one-line reason; allowed to proceed | Warn only |
| Font missing | font enumeration | Warning banner: fallback font in use | Proceeds |
| Render error on a page | exception from PDFium or Skia | Phase, page type, PDF page number, exception text | Exit 4, nothing written to OneNote |
| UpdatePageContent rejects XML | COM HRESULT (typically 0x80042001, invalid XML) | Page name, HRESULT, log saved with the XML | Exit 5, partial notebook kept |
| OneNote busy (RPC\_E\_CALL\_REJECTED) | HRESULT 0x80010001 | Nothing; retried once | Continues or Exit 5 |
| Cancel pressed | user | Progress stops after the current page; partial notebook deleted | Exit 0 with "Cancelled" |
| Disk full | IOException | Message with free space needed | Exit 4 or 5 |

Refresh adds its own cases: a notebook whose sidecar is missing (re-locate every page by its sfp.key meta and rebuild the sidecar, warn once); a Setup slot holding only inked text (shown in the preview, written only after confirmation unless config allows it); a calendar source that fails (that source skipped, others written, exit 6 when all fail); a tool-owned object that a user moved (left where it is, reported, not re-placed); OneNote not running during the scheduled task (started by the COM call, and the run waits up to 60 s for the notebook to open).

## Acceptance tests

### Automated (run on every build, no Office needed)

- Date engine: Q1 2027 produces 14 weeks and 90 daily pages, week 1 holding Fri Jan 1 to Sun Jan 3 and week 14 holding Mon Mar 29 to Wed Mar 31; Q4 2026 produces 14 weeks with Dec 28 to 31 in week 14 and Jan 1 to 3 greyed in December's grid; Q2 2024 (Apr 1 is a Monday, 91 days) produces exactly 13 weeks; Q1 2024 (leap year) produces 91 daily pages; November 2026 needs 6 rows and stacks 30 in the 23 box; an off-cycle start such as February 2027 gets the label Feb to Apr 2027.
- Render golden images: --render-only against the reference PDF for one fixed date; every PNG must match the stored golden hash. Goldens are regenerated deliberately when the manifest changes.
- Overlay placement: for the first daily page of Q1 2027 (Fri Jan 1), crop the date region and run OCR (Tesseract) expecting "01 / 01 / 27"; crop the strip and expect 14 numbers with the circle on 14.
- Manifest validation: a PDF with 131 pages, a PDF with wrong page size, and a PDF with the probe word missing each fail with the right message.
- XML builder: generated page XML validates against the 2013 XSD.

### Manual on Windows (once per release; Refresh cases follow the Generate list)

- [ ] Generate SFP Q1 2027 from the reference PDF; run completes under 3 minutes with no warnings.
- [ ] OneNote opens on the cover page; tab row reads Quarter, Objectives, Calendar, Week 01 ... Week 14 in order.
- [ ] Week 01 page list shows Weekly Overview, then three indented day pages Fri Jan 1 to Sun Jan 3, then Weekly Reflection; Week 02 shows all seven, Mon Jan 4 to Sun Jan 10.
- [ ] On Mon Jan 4: clicking the picture does not select it; Ctrl+A then Delete leaves the picture in place; typing at a click point creates text there.
- [ ] Dates read 01 / 04 / 27 on Mon Jan 4 and 01 / 10 / 27 on Week 02's Weekly Reflection; Mon Jan 4's strip shows 14 numbers with the circle on 13.
- [ ] Calendar section has January, February and March 2027 only; January shows Dec 28 to 31 at 40% in its first row and March shows Apr 1 to 4 at 40% in its last row.
- [ ] Contents page links jump to the right pages. On January 2027's calendar, clicking 4 opens Mon Jan 4 and clicking Week 02 opens that Weekly Overview; on Mon Jan 4 clicking the date opens Week 02's Weekly Overview; on that overview clicking Tuesday opens Tue Jan 5; Annual Objectives block 3 opens Objective 3 and its back link returns.
- [ ] Zoom to 100%: the page is 15.4 in wide and the 7 pt hour labels are legible.
- [ ] Move Notebook to OneDrive succeeds and the sync icon clears within 5 minutes.
- [ ] Type two objective titles and one standing goal on Setup, ink a third objective, run Preview: the inked value is flagged; run Refresh: the titles appear on Annual Objectives, in the legend on Mon Jan 4's page and on the scorecard, and the standing goal appears at 60% on every daily page's first goal line.
- [ ] Write a note on Mon Jan 4 over the legend band, run Refresh again: the note is untouched and the summary reports the skipped slot.
- [ ] Add an .ics source with a 9:00 meeting on Tue Jan 5, run Refresh: the meeting appears at the 9 row on Tue Jan 5 and in Tuesday's column on Week 02's overview; cancel it in the source, Refresh: it is gone from both.
- [ ] Enable the daily task, log out and in, confirm the task ran at 05:00 from its log.

### Manual on iPad (once per release)

- [ ] Notebook appears in OneNote for iPad after sign-in; all 19 sections and 144 pages load.
- [ ] Open Wed Jan 6 in Draw mode; ink with the Apple Pencil across lines and over the date. The picture does not move, select or show handles.
- [ ] Lasso-select the ink and move it; the picture stays. A finger tap on the date link opens the Weekly Overview; a pencil stroke across it draws instead of navigating.
- [ ] Ink appears on the Windows page within 2 minutes.
- [ ] Text typed on Windows appears on the iPad in the right place relative to the lines.
- [ ] Fit-to-width in landscape: Daily Goals numbers and day name are readable without zoom; hour labels are readable at 150% zoom.

## Phasing and open decisions

### Phase 0: spike (2 to 3 days)

Prove the two risky calls before writing any UI: UpdatePageContent with backgroundImage="true" on a 1112 x 770 image, checked on iPad after a manual move to OneDrive; and UpdateHierarchy setting pageLevel="2". Also measure titleOffset, confirm whether PageSettings accepts a hex color, check that a hyperlink one:T styled in Bookman and ink color renders without the default blue underline, confirm an onenote: page link placed by the writer opens the right page on the iPad, and test whether a transparent one:Image with a hyperlink attribute is clickable on both devices. Output: a Python or C# script and a one-page findings note that updates this PRD.

### Phase 1: MVP (1 to 2 weeks)

CLI only. Manifest, date engine, renderer, COM writer, golden tests, Contents links page, page links, day counter, time-block window, previous and next navigation, page metadata and section colors. Produces a usable notebook; the move to OneDrive is manual.

### Phase 2: desktop UI, Refresh and the added pages (3 to 4 weeks)

WPF window, previews, progress, cancel, settings persistence, self-contained publish. Then the Refresh operation: Setup page, propagation of objectives, standing goals, metric names and habits, the sidecar and page keys, dry run, and the Refresh tab. Then calendar import with the daily scheduled task. Then the Reviews section (scorecard, monthly and quarterly reviews) and the Notes section.

### Phase 3: cloud-native (optional)

Two independent options. 3a: Graph sign-in, notebook created directly in OneDrive, desktop writer fills it by URL, which removes the manual move. 3b: a NoteCraftWriter that emits NoteCraft's folder format for the Windows side, if NoteCraft is adopted there. Also the point to add a second template (an annual planner) if one appears.

### Decisions made (19 Oct 2026)

- Approach: locked background images written through the OneNote desktop COM API (strategy 1). Native-table pages, pre-seeded text containers and the Graph-only path are not pursued.
- Host: the perpetual OneNote from Office 2024 is installed on the build machine. It carries the same COM API as the subscription build, so the COM dependency is accepted.
- Dates: the software populates every date-bearing element (date slots, calendar month names and day numbers, cover label, weeks-remaining circle). The user enters only planning content.
- Editing model: free placement. Keyboard text on Windows and pencil ink on iPad go anywhere on the sheet; the tool adds no guides, boxes or snap targets. The only native objects on a page are the link anchors.
- Quarter alignment: a quarter starts on the 1st of a month and covers three calendar months. Weeks stay Monday to Sunday, so the first and last weeks are partial and most quarters have 14 week sections.
- Weeks-remaining circle: corrected, week k circles N - k + 1 over a strip of N numbers.
- Weekly Reflection date: the Sunday that ends the week.
- Year format: two digits in every date slot.
- Calendar: exactly three monthly pages, the quarter's months; neighboring-month days are greyed in the grid.

* Enhancements in scope (19 Oct): Setup-page propagation of objectives, standing daily goals, metric names and habits; calendar import with a daily scheduled refresh; previous and next navigation plus page dates and section colors; quarter scorecard; monthly and quarterly reviews; configurable time-block window; free-form Notes pages; day counter. A Refresh operation that edits only tool-owned objects is accepted as the mechanism.

### Decisions to make

- [ ] Decided: weeks-remaining circle is corrected (week N circles 14 - N).
- [ ] Decided: Weekly Reflection carries the Sunday date.
- [ ] Section layout: 16 or 17 flat tabs (assumed) or a Weeks section group.
- [ ] Decided: exactly three calendar pages; spill days are greyed in the adjacent grid.
- [ ] Page title visible above the sheet (assumed) or hidden with the sheet at y = 0.
- [ ] Placed size: full 1112 x 770 pt (assumed) or scaled to Letter-landscape width.
- [ ] Decided: two-digit year in every date slot (01 / 04 / 27).
- [ ] Keep the SFP cover and logo page in the notebook (assumed yes, with the quarter label added) or drop it.
- [ ] Build language: .NET 8 (assumed) or Python packaged with PyInstaller.

### Risks

- OneNote may clamp or reject a 1112 pt wide outline on a titled page, forcing the hidden-title path or a scale factor. Phase 0 answers this.
- iPad rendering of large background PNGs can be slow on older iPads; the DPI option exists for this reason.
- The COM API is old and Microsoft has not promised its future. It ships in every current OneNote desktop build, perpetual and subscription; if it is ever removed, the Graph path in Phase 3 is the fallback but loses background locking, and the NoteCraft writer below is the fallback for the Windows side.
- Font metrics: if Bookman Old Style is absent the dates will not match the printed labels exactly. Acceptable, logged.

## Appendix

### Hierarchy XML for a week section

Sent once per section through UpdateHierarchy after all nine pages exist. IDs are the ones CreateNewPage returned.

```xml
<one:Section xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote" ID="{sectionId}" name="Week 02 (Jan 4 to Jan 10)">
  <one:Page ID="{p1}" name="Weekly Overview" pageLevel="1"/>
  <one:Page ID="{p2}" name="Mon Jan 4" pageLevel="2"/>
  <one:Page ID="{p3}" name="Tue Jan 5" pageLevel="2"/>
  <one:Page ID="{p4}" name="Wed Jan 6" pageLevel="2"/>
  <one:Page ID="{p5}" name="Thu Jan 7" pageLevel="2"/>
  <one:Page ID="{p6}" name="Fri Jan 8" pageLevel="2"/>
  <one:Page ID="{p7}" name="Sat Jan 9" pageLevel="2"/>
  <one:Page ID="{p8}" name="Sun Jan 10" pageLevel="2"/>
  <one:Page ID="{p9}" name="Weekly Reflection" pageLevel="1"/>
</one:Section>
```

### Contents page outline

A native page, so it is the one page a user can edit. Each line is a OneNote hyperlink from GetHyperlinkToObject(objectId, ""), which returns an onenote: URL that works on Windows, Mac and iPad.

```xml
<one:Outline>
  <one:Position x="36" y="86" z="0"/>
  <one:OEChildren>
    <one:OE><one:T><![CDATA[<a href="onenote:...section-id={...}">Objectives</a>]]></one:T></one:OE>
    <one:OE><one:T><![CDATA[<a href="onenote:...">Calendar</a>]]></one:T></one:OE>
    <one:OE><one:T><![CDATA[<a href="onenote:...">Week 02 (Jan 4 to Jan 10)</a>]]></one:T>
      <one:OEChildren>
        <one:OE><one:T><![CDATA[<a href="onenote:...">Mon Jan 4</a>]]></one:T></one:OE>
      </one:OEChildren>
    </one:OE>
  </one:OEChildren>
</one:Outline>
```

### Measured coordinates, daily page (PDF points)

| Item | x | y | Notes |
| --- | --- | --- | --- |
| Day name | 23.0 | 9.3 to 46.8 | 32 pt |
| "Weeks Remaining In Quarter" label | 386.1 to 527.7 | 30.0 to 41.7 | 10 pt |
| Numbers 13 ... 1 | 537.3 ... 716.5 | 30.0 to 41.7 | 10 pt, see manifest |
| Left date slash | 900.2 to 918.8 | 16.9 to 41.0 | 1 pt stroke |
| Right date slash | 952.1 to 970.7 | 16.9 to 41.0 | 1 pt stroke |
| Daily Goals label | 40.7 | 63.0 to 80.6 | 15 pt |
| Plan label | 404.0 | 63.0 to 80.6 | 15 pt |
| Time Blocks label | 935.3 | 62.8 to 80.4 | 15 pt |
| Hour labels | 885.3 to 894.0 | 90.7 to 642.1 | 7 pt, 24 rows, pitch 22.6 |
| Follow Up label | 40.7 | 328.0 to 345.6 | 15 pt |
| Daily Metrics label | 507.7 | 662.7 to 680.3 | 15 pt |

### Sources

- [Create OneNote pages, Microsoft Graph](https://learn.microsoft.com/graph/onenote-create-page): supported HTML, multipart images, section page limits. Used to confirm Graph cannot set page levels or background images.
- [OneNote get-pages search deprecation](https://devblogs.microsoft.com/microsoft365dev/onenote-get-pagessearch-api-deprecation): the only recent change to the Graph OneNote surface for consumer notebooks.
- [OneNote 2013 COM API XML schema](https://learn.microsoft.com/en-us/archive/blogs/descapa/onenote-2013-com-api-xml-schema-onenote-2013-xsd): location of the XSD the page XML must validate against.
- [Page width and height in the OneNote XML schema](https://learn.microsoft.com/en-us/archive/blogs/johnguin/looking-at-the-xml-schema-for-page-width-and-height-in-onenote): PageSize Dimensions element.
- [OneNote for Windows 10 retirement](https://m365admin.handsontek.net/?p=10968): retired 14 October 2025, which is why the desktop app is the only target.
- [Set Picture as Background in OneNote](https://office-watch.com/2022/set-picture-as-background-in-onenote/): the UI behavior the backgroundImage attribute reproduces.
- Geometry, fonts and colors: measured from SFP\_No\_Dates.pdf with pdfplumber and pdftotext on 19 October 2026.
