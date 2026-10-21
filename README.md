# sfp2onenote

A small Windows tool that turns the Structured Focus Planner quarterly PDF plus a start month into a complete, fully
dated OneNote notebook: a section per week plus Quarter, Objectives, Calendar, Reviews and Notes, about 144 pages,
every page a locked background image you can type over on Windows and ink over on an iPad.

The requirements are in [docs/prd.md](docs/prd.md); the order of work and the code structure are in
[docs/development-plan.md](docs/development-plan.md). Judgment calls are logged in [docs/decisions.md](docs/decisions.md),
spike results in [docs/spike-findings.md](docs/spike-findings.md), and every task card in
[docs/CHANGELOG.md](docs/CHANGELOG.md).

## What you need

- Windows 10 22H2 or Windows 11, x64.
- OneNote desktop 16.x (Office 2016 or later, perpetual or Microsoft 365). OneNote for Windows 10 is not supported.
- Your own licensed copy of `SFP_No_Dates.pdf` (132 pages). The release zip ships without it and the tool never copies it anywhere; the owner keeps a copy under docs/ in this repository for the test suite.
- Bookman Old Style, which ships with Office. If it is missing the bundled TeX Gyre Bonum is used and a warning is shown.

## Install

There is no installer and nothing to install first: both executables are self-contained (the .NET 8 runtime is inside
them).

1. Download `sfp2onenote-<version>-win-x64.zip` from the repository's Releases page.
2. Unzip it into a folder of your own, for example `C:\Tools\sfp2onenote`. It holds:
   - `sfp2onenote-app.exe`, the window with the Generate and Refresh tabs: double-click it.
   - `sfp2onenote.exe`, the command line below, which the app's "Refresh every day at 05:00" task runs.
   - `templates\`, the planner manifest, the TeX Gyre Bonum fallback font with its license, and the US holiday list.
   - `LICENSE.md`.
3. Keep them together. Both executables read `templates` from their own folder and the app runs the `sfp2onenote.exe`
   beside it, so move or replace the folder as a whole. After moving it, untick and tick the daily refresh once so the
   scheduled task points at the new place.

On first start each executable unpacks its native libraries (PDFium, SkiaSharp and, for the app, WPF) under
`%TEMP%\.net`; set `DOTNET_BUNDLE_EXTRACT_BASE_DIR` to put them elsewhere. To update, unzip a newer release over the
folder. The planner PDF is never in the zip.

## Command line

```
sfp2onenote plan --start 2027-01
sfp2onenote render-only --pdf "C:\path\SFP_No_Dates.pdf" --start 2027-01 --out .\out\q1
sfp2onenote generate --pdf "C:\path\SFP_No_Dates.pdf" --start 2027-01 [--name "SFP Q1 2027"] [--out "D:\Notebooks"]
sfp2onenote refresh --notebook "SFP Q1 2027" [--calendar] [--horizon 14] [--dry-run]
sfp-measure "C:\path\SFP_No_Dates.pdf" 17
```

Exit codes: 0 success, 2 validation error, 3 OneNote unavailable, 4 render error, 5 write error, 6 refresh source unreachable.

Logs: one file per run under %LOCALAPPDATA%\sfp2onenote\logs (override with SFP2ONENOTE_LOGDIR). If that folder cannot be used, the verb says so in one line and logs to sfp2onenote\logs in the temp folder instead; it exits 2 only when neither folder works.

## Build from source

```
dotnet build -warnaserror
dotnet test --filter "Category!=COM"          # anywhere
dotnet test --filter "Category=COM"           # a Windows PC with OneNote
```

Tests that need the planner PDF read it from `SFP_PDF_PATH`, else from `docs/SFP No Dates.pdf`, and skip with a message when neither exists. OCR checks on
rendered pages use the `tesseract` CLI or the Python `tesserocr` module when one is present and skip otherwise;
`TESSDATA_PREFIX` must point at a folder holding `eng.traineddata`.

### Release package

`build/publish.sh` (bash) and `build/publish.ps1` (PowerShell 5.1 or 7) build the release zip on any machine with the
.NET 8 SDK; on Linux and macOS the WPF app is cross-published. Each runs, for `src/Sfp.Cli` and then `src/Sfp.App`,

```
dotnet publish <project> -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=<version> -o <out>
```

copies `LICENSE.md` beside the two executables, checks that both are single-file bundles and that `templates` is a
byte-identical copy of the repository's, and zips the folder as `sfp2onenote-<version>-win-x64.zip` next to it:

```
build/publish.sh [--out DIR] [--version 1.2.0]
pwsh -File build/publish.ps1 [-Out DIR] [-Version 1.2.0]
powershell -ExecutionPolicy Bypass -File build\publish.ps1 [-Out DIR] [-Version 1.2.0]
```

The folder defaults to `artifacts/sfp2onenote-win-x64`, emptied on every run (any other folder must be empty); the
version defaults to `Version` in Directory.Build.props, and a leading `v` is dropped so a tag name works. Trimming is
off on purpose: WPF is not trim-safe, so `PublishTrimmed` stays false and each executable carries the whole runtime
(about 91 MB for `sfp2onenote.exe` and 174 MB for `sfp2onenote-app.exe`, 113 MB zipped). Symbols and XML
documentation are left out of the folder.

Pushing a tag such as `v1.0.0` runs `.github/workflows/release.yml` on windows-latest: build, the tests,
`build/publish.ps1 -Version <tag>`, then the zip is attached to the GitHub release of that tag (a prerelease when the
tag has a hyphen, such as `v1.1.0-rc.1`).

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
