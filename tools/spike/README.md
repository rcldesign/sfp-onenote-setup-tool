# M0 spike scripts

Python scripts (pywin32) that answer the OneNote questions in the development plan's M0 table. Nothing here ships;
the findings go into docs/spike-findings.md and the manifest's `onenote` block.

Requirements: a Windows PC with OneNote desktop (Office 2016 or later) signed in, Python 3.11 or later, and
`pip install pywin32 pymupdf pillow`. Run from this folder.

Scripts are added in wave 1 (cards M0.1 to M0.9). Each prints a findings block to paste into docs/spike-findings.md.
