# M3.10 acceptance: first generated notebook on Windows and iPad

The build environment has no OneNote, so M3's COM cards were verified against the in-memory fake OneNote
(tests/Sfp.OneNote.Tests/Writer/FakeOneNoteApp.cs) and the page XML against the reconstructed 2013 schema. This
checklist is the owner's run on the PC and the iPad. Any failure becomes a card before M4 work is accepted.

## Before the run

1. `dotnet test --filter "Category=COM"` on the PC: the OneNoteApp integration test lists notebooks, ComNotebookWriterComTests creates and deletes a scratch notebook with three sections and a nine-page week.
2. Record in docs/spike-findings.md what the COM run showed if the M0 scripts have not run yet (title offset, hex page color, anchor padding), and apply with tools/spike/m0_9_apply.py.
3. `sfp2onenote generate --pdf "<path>\SFP_No_Dates.pdf" --start 2027-01` from a folder outside OneDrive.

## Windows (PRD Generate items)

- [ ] Run completes under 3 minutes with no warnings; the summary shows 144 pages, the notebook path, the elapsed time and the OneDrive move instruction.
- [ ] OneNote opens on the cover; tab row reads Quarter, Objectives, Calendar, Week 01 ... Week 14, Reviews, Notes.
- [ ] Week 01 page list: Weekly Overview, Fri Jan 1, Sat Jan 2, Sun Jan 3 (indented), Weekly Reflection; Week 02 has all seven days.
- [ ] Mon Jan 4: the sheet cannot be selected or deleted; typing creates a text container at the click point.
- [ ] Dates: 01 / 04 / 27 on Mon Jan 4 (a link to Week 02's overview), 01 / 10 / 27 baked on Week 02's reflection; strip with 14 numbers, circle on 13.
- [ ] Calendar: January, February, March 2027; Dec 28 to 31 greyed in January, Apr 1 to 4 greyed in March; day numbers and Week NN labels are links.
- [ ] Contents page links open the right pages and sections.
- [ ] Links read in the template typeface and ink color (note whether OneNote underlines them; see spike M0.4).
- [ ] sfp-state.json sits beside the notebook folder and lists every page key with its ids.

## iPad (PRD iPad list)

- [ ] After File, Share, Move Notebook to OneDrive, the notebook appears on the iPad with all sections and pages.
- [ ] Wed Jan 6 in Draw mode: pencil ink over lines and the date; the picture does not move, select or show handles.
- [ ] Lasso and move the ink; the picture stays. Finger tap on the date link opens the overview; a pencil stroke over it draws.
- [ ] Ink appears on Windows within 2 minutes; Windows text appears on the iPad in place.
- [ ] Fit-to-width landscape: Daily Goals numbers and the day name readable without zoom; hour labels at 150%.

## Results

| Item | Result | Date | Note |
| --- | --- | --- | --- |
| | | | |
