# M5.6 acceptance: calendar import and the daily task

PRD Refresh acceptance items 3 and 4, run on the owner's PC after M3.10 passes and the notebook has been moved to
OneDrive. The calendar sources, placement, freeze and diff, failure handling and the schtasks command lines are
covered by tests/Sfp.Refresh.Tests (Calendar/*, Scheduler/*) in the build environment; only the end-to-end run
against OneNote, a live .ics feed and the Task Scheduler remains.

## Setup

1. On the Setup page, type one calendar source line: `Work | <https URL of a published .ics>` or `Work | C:\path\work.ics`.
2. Put a 9:00 to 10:00 meeting on Tue Jan 5, 2027 in that calendar, plus one all-day event on Wed Jan 6.
3. `sfp2onenote refresh --notebook "SFP Q1 2027" --calendar --horizon 14 --dry-run --today 2027-01-04`, then without `--dry-run`.

## Items

- [ ] Dry run lists the 9:00 meeting as timeBlockEvent:9 on Tue Jan 5 and overviewDay:1 on Week 02's overview, and the all-day event as allDayBand on Wed Jan 6 and calendarEvents:2027-01-06 on January; nothing is written.
- [ ] Real run writes them; Tue Jan 5 shows "9:00 <title>" in the 9 row at 70% ink; the overview column shows "09:00 <title>"; the sidecar's lastRefresh and frozenThrough are set.
- [ ] Cancel the meeting in the source and refresh again: both lines are gone; the all-day event stays.
- [ ] Run with --today 2027-01-06: the Tue Jan 5 page is not touched again (frozen), even if the source changed.
- [ ] Make the source URL unreachable and refresh: exit code 6, the summary names the failed source, nothing is removed; three consecutive failures show the Windows toast.
- [ ] Enable "Refresh every day at 05:00" in the app (or `schtasks /Query /TN "sfp2onenote refresh SFP Q1 2027"`); the task exists, runs only when logged on, and its log under %LOCALAPPDATA%\sfp2onenote\logs shows a run the next morning.
- [ ] The iPad shows the events after OneDrive sync.

## Results

| Item | Result | Date | Note |
| --- | --- | --- | --- |
| | | | |
