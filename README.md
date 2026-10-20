# sfp2onenote

A small Windows tool that turns the Structured Focus Planner quarterly PDF plus a start month into a complete, fully
dated OneNote notebook: a section per week plus Quarter, Objectives, Calendar, Reviews and Notes, about 144 pages,
every page a locked background image you can type over on Windows and ink over on an iPad.

The requirements are in [prd.md](prd.md); the order of work and the code structure are in
[development-plan.md](development-plan.md). Judgment calls are logged in [docs/decisions.md](docs/decisions.md),
spike results in [docs/spike-findings.md](docs/spike-findings.md), and every task card in
[docs/CHANGELOG.md](docs/CHANGELOG.md).

## What you need

- Windows 10 22H2 or Windows 11, x64.
- OneNote desktop 16.x (Office 2016 or later, perpetual or Microsoft 365). OneNote for Windows 10 is not supported.
- Your own licensed copy of `SFP_No_Dates.pdf` (132 pages). The tool ships without it and never copies it anywhere.
- Bookman Old Style, which ships with Office. If it is missing the bundled TeX Gyre Bonum is used and a warning is shown.

No installer: unzip the release, keep the `templates` folder beside the executable, and double-click `sfp2onenote-app.exe`,
or use the command line.

## Command line

```
sfp2onenote plan --start 2027-01
sfp2onenote render-only --pdf "C:\path\SFP_No_Dates.pdf" --start 2027-01 --out .\out\q1
sfp2onenote generate --pdf "C:\path\SFP_No_Dates.pdf" --start 2027-01 [--name "SFP Q1 2027"] [--out "D:\Notebooks"]
sfp2onenote refresh --notebook "SFP Q1 2027" [--calendar] [--horizon 14] [--dry-run]
sfp-measure "C:\path\SFP_No_Dates.pdf" 17
```

Exit codes: 0 success, 2 validation error, 3 OneNote unavailable, 4 render error, 5 write error, 6 refresh source unreachable.

## Building

```
dotnet build -warnaserror
dotnet test --filter "Category!=COM"          # anywhere
dotnet test --filter "Category=COM"           # a Windows PC with OneNote
```

Tests that need the planner PDF read it from `SFP_PDF_PATH` and skip with a message when it is not set. OCR checks on
rendered pages use the `tesseract` CLI or the Python `tesserocr` module when one is present and skip otherwise;
`TESSDATA_PREFIX` must point at a folder holding `eng.traineddata`.

## Layout

```
templates/      sfp-quarterly.json (every measured coordinate), fonts/, holidays-us.json
src/Sfp.Core    manifest model, date engine, notebook plan, tokens
src/Sfp.Render  rasterizer, overlays, generated pages, fonts
src/Sfp.OneNote COM wrapper, page XML, hierarchy, hyperlinks, sidecar
src/Sfp.Refresh Setup reader, propagation, calendar import, scheduler
src/Sfp.Cli     sfp2onenote verbs
src/Sfp.App     WPF window
tests/          one test project per library plus shared TestSupport
tools/spike     M0 OneNote spike scripts (Python, pywin32)
tools/measure   PDF geometry dump
```

## License

The tool's source is under the [TEW Group Noncommercial License](LICENSE.md). The planner PDF is copyrighted by its
publisher and is not part of this repository. TeX Gyre Bonum is bundled under the GUST Font License
(templates/fonts/GUST-FONT-LICENSE.txt).
