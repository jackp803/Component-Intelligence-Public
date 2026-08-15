# Search / Knowledge Implementation Status — 2026-08-14

## Scope

This document records the implemented Component Intelligence search and knowledge-acquisition behavior on branch `gpt/electrical-design-core`. It is an implementation handoff, not an assertion that all manufacturer sites or document layouts are solved.

## Implemented acquisition layers

1. **Local Component IR reuse**
   - Rich local Component IR can be reused.
   - Legacy sparse cache is refreshed by the online pipeline instead of permanently masking new extractors.
   - `Deep Search` forces a fresh online acquisition pass.

2. **Manufacturer identity and product-page discovery**
   - Existing manufacturer adapters: IFM plus generic official catalog adapters for OMRON, WAGO, Schneider Electric, MEAN WELL, Moxa, and supported Fuji Electric cases.
   - Lightweight HTTP is attempted before Playwright browser fallback.

3. **Browser fallback**
   - Microsoft Playwright uses installed Microsoft Edge when ordinary HTTP cannot obtain useful dynamic content.
   - Rendered DOM is returned to the same extraction pipeline.
   - Same-site bounded JSON API responses observed during browser rendering are embedded into the returned content so structured JSON extraction can process them.

4. **HTML extraction**
   - Visible HTML table rows and definition lists are retained.
   - Unknown manufacturer fields are preserved as raw engineering specifications instead of being discarded.
   - Known labels are normalized through `SpecificationDictionary`.

5. **Structured web data extraction**
   - schema.org JSON-LD.
   - `application/json` script payloads.
   - common framework payloads such as `__NEXT_DATA__` / `__NUXT_DATA__`.
   - HTML microdata via `itemprop`.
   - Explicit property name/value objects and conservative engineering-relevant scalar fields are preserved with evidence.

6. **Manufacturer document discovery**
   - Datasheet/manual/technical/download links are recognized even when the URL does not literally end in `.pdf`.
   - Downloaded content is checked by actual content type before PDF processing.

7. **Digital PDF extraction**
   - PdfPig text extraction for known high-value patterns.
   - PdfPig coordinate-based reconstruction of visual label/value rows.
   - Reconstructed rows are retained as PDF table evidence and normalized where possible.

8. **Textual pinout extraction**
   - Explicit rows such as `Pin 1 = L+`, `3 = L-`, `4 = C/Q IO-Link` can be promoted into `ComponentPin` facts.
   - Arbitrary numbered dimension rows are not treated as pins.
   - Missing pin functions are not invented.

9. **Secondary-source enrichment**
   - DigiKey and RS are currently integrated as `AuthorizedDistributor` enrichment sources.
   - Secondary sources cannot decide manufacturer identity.
   - They are consulted when official knowledge remains sparse, wiring-critical facts are missing, or engineering documents are absent.

10. **Manual knowledge upload**
    - User PDF/text knowledge can be attached to an identified component.
    - Uploaded PDFs use the same text + coordinate-table + textual-pinout extraction path.
    - Supplemental knowledge is merged with existing online Component IR instead of replacing it.
    - Original file, SHA-256 and evidence provenance are retained.

11. **Evidence / verification**
    - Source trust order is preserved: manufacturer datasheet/manual/product/download > user file > authorized distributor > trusted third party > generic web > AI inference.
    - Critical engineering values from independent sources are compared.
    - Conflicting values create explicit `DATA_CONFLICT` diagnostics and reduce confidence instead of being silently overwritten.

12. **User-visible inspection**
    - `Knowledge Inspector` shows saved technical specifications, documents and evidence.
    - Quality summary includes total specifications, normalized vs raw-only fields, independent source count, pin-function coverage, structured JSON evidence count and PDF-table evidence count.
    - `Deep Search` reports specification/document/source counts after refresh.

## Deliberately not claimed as complete

- OCR for scanned/image-only PDFs is not implemented yet.
- Image/diagram-based pinout recognition is not implemented yet.
- Arbitrary manufacturer private APIs are not hard-coded; browser-observed same-site JSON is captured when available.
- Component-type-specific completeness profiles still need further refinement.
- Source freshness / automatic age-based refresh policy is not final.
- More official manufacturer adapters and distributor/catalog sources remain future expansion work.
- Generic web sources do not automatically override manufacturer evidence.
- AutoCAD Drawing Automation is outside this search/knowledge scope.

## Test intent

The search/knowledge test suite now covers structured JSON/JSON-LD extraction, coordinate-based PDF rows, textual pin assignments, independent-source conflict detection, secondary-source enrichment behavior, and manual knowledge merge behavior. Windows CI must remain green before a build is presented as the current test artifact.
