# Offscreen Cable Wiring Regression

Run without opening a native window or using production files:

```powershell
dotnet run --project validation/CableWiringEditorSmoke -c Release -p:WarningLevel=0 -- C:/temp/ci-synthetic-wiring pdf
dotnet run --project validation/CableWiringEditorSmoke -c Release -p:WarningLevel=0 -- fit C:/temp/ci-synthetic-fit pdf
```

The output directory contains disposable synthetic PNG/PDF evidence. Tests cover exact-Pin connections, branches, deletion, undo/redo, border attachment, mirroring through the real toolbar command, function association and manual table overrides, staged-copy isolation, selected-wire bridge rendering, zoom/pan and nonblank drawing. PDF coverage includes main/appendix order, physical-instance deduplication, forward/return annotations, relocated continuation pages, inline tables, readable large-inventory tiling and atomic export failure.

Supplement with domain tests (`CableWiringConvenienceTests`) for archive v8/project 0.9 reopen, explicit blank overrides, cyclic graphs, conflicts, source removal and external instance-mapping overrides. Open and inspect the rendered PNGs before delivery; do not infer visual correctness from successful serialization alone.

The `fit` mode reproduces a moderately oversized 8/10/6-Pin diagram inside a synthetic company frame. It checks single-page fit, larger paper-size text, actual WPF label extents and overlap, nonconsecutive Pin numbers, preserved jump arcs, unchanged normal editor text, source immutability, readable large-inventory fallback, narrow-frame safe type, whole-label tile-seam coverage, explicit oversized-label rejection, and linked-PDF page counts. Fitting changes only the exported viewport, not stored connector positions or electrical data.
