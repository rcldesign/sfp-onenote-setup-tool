# Golden hashes

Golden images pin the renderer's output so that a change to the page art is always deliberate. The PNGs themselves are
never committed (`.gitignore` excludes `tests/**/golden/**/*.png`); only a SHA-256 per rendered page lives here.

## Convention

- One file per fixture and platform: `<fixture>.<rid>.sha256`.
  - `<fixture>` is `synthetic` for the PdfPig look-alike built by `tests/Sfp.TestSupport/SyntheticTemplate.cs`
    (always available), or `sfp` for the real planner at `SFP_PDF_PATH` (skipped when the variable is unset).
  - `<rid>` is the .NET runtime identifier the hashes were recorded on: `win-x64`, `linux-x64`, `osx-arm64` and so on.
    PDFium and Skia builds differ by a few antialiased pixels between platforms, so a hash recorded on Windows is not
    expected to match Linux; each platform keeps its own file.
- Each file holds one line per page in the form `<sha256>  <page key>` (two spaces, as `sha256sum` prints), sorted by
  page key, so a diff shows exactly which pages moved.
- The golden test renders every page of the plan, hashes the PNG bytes, and compares with the file for the current
  fixture and RID. A mismatch fails with the page key and both hashes. A missing file for the current RID skips with a
  message instead of failing, so a new platform is added by recording rather than by editing.

## Regenerating

Set `SFP_RECORD_GOLDENS=1` and run the render tests. The suite then writes the `.sha256` file for the current fixture
and RID instead of comparing. Commit that change on its own, and say in the commit message which card changed the art
and why (the development plan's definition of done asks for exactly that). Never regenerate to turn a red test green
without understanding what moved: compare the before and after PNGs first.

The comparison and recording mechanism is implemented in M2.10 (golden suite and render-only); this file fixes only the
naming and the workflow so earlier cards write their renders to the same convention.
