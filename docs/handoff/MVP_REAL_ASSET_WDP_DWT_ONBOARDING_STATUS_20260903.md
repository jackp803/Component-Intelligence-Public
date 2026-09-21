# MVP Real-Asset WDP / DWT Onboarding Status

- Task: `CODEX-W1-20260903-005`
- Checkpoint: `MVP Real-Asset Onboarding - Company Project Baseline + Title Block`
- Evidence execution resumed: `2026-09-21`
- Disposition: `DONE`
- Semantic drawing verification: `NOT_RUN` (`APPLIED != VERIFIED`)

## Accepted executable identity

- Component commit: `2dd4981d38eae163e2c55299bfdce9221f1b130d`
- Component tree: `b099be15479ef3f662308a06495ed9b0f923213c`
- Source repairs: `NONE`
- Verification branch contains evidence only.

## Selected company assets

Private absolute paths are intentionally retained only in task-local evidence and the user-local runtime configuration.

| Asset alias | Size | SHA-256 | Selection evidence |
| --- | ---: | --- | --- |
| `COMPANY_PROJECT_BASELINE/CM-LRDU-20260722.wdp` | 2,646 | `1E805901A4938AB89E0D77E46142743B4334F19916D47D5D2B12637E73C174F0` | Same LRDU project previously supplied by the Product Owner; project-relative WDT/WDL and 17 real drawing references establish it as the active company project rather than an Autodesk demo, POC, backup, or empty template. |
| `COMPANY_DRAWING_TEMPLATE/CM_A3_TITLEBLOCK.dwt` | 432,343 | `D5DB332E88F16F18ED82FB3A2982EF2E4970F2438F09522DDEEE51E9F17A03DF` | Existing company automation documentation selects it; read-only AutoCAD inspection matches the real project drawing title-block identity and attribute mapping. |

Corroborating read-only evidence:

- representative formal project DWG SHA-256: `6B341EFED05DA4BBE60235464685D7388E104D6ECC7996A1221CE0310F6AFB0D`
- title-block mapping WDT SHA-256: `B851C9D43854F6EB9419FF690B7FE2506A62054C5051ADE0B3E23EA1EE6AAC70`
- project WDL SHA-256: `400260D1EDA538F10C1BD0ADC4313004A83570D35029D7B63EDCF86139F97076`
- older `.backup-*`, `.before-*`, stock Autodesk, POC/staging, and empty-template candidates were excluded by direct association evidence, not filename recency.

## User-local runtime onboarding

- Both CP3-C settings files were absent before onboarding; absence was recorded before the write.
- The real WPF candidate was launched against a byte-for-byte disposable SQLite copy through `COMPONENT_INTELLIGENCE_DB_PATH`.
- The main UI visibly reported the disposable database path.
- All six runtime fields were validated and saved through `DrawingExecutorRuntimeSettingsValidator` and the real settings dialog.
- The dialog was reopened; the settings store reloaded the accepted company WDP and DWT values.
- WPF status: `Drawing runtime settings validated and saved to the user-local profile.`
- The application closed normally before physical execution.

## Real isolated-staging execution

The one authorized physical run used the production `LocalDrawingExecutorClient` boundary and the accepted runtime chain:

`C# -> Python -> PowerShell -> accoreconsole / AutoLISP -> ISOLATED_STAGING`

- run ID: `CP3C-4B82EA9419684F5FBAB972934F506332`
- result schema: `electrical-execution-result.v1`
- status: `APPLIED`
- source Drawing IR hash: `EAFDE590410D85AB2C6BCBE0B6CF1FB2E3899FCD18D8A73E8B835BF4F7BA5DBE`
- executor plan hash: `2A32DAAF7A174402892E0AFDA4556BC8D857BA27E4024D180062B0637C2C4FEA`
- execution evidence hash: `370731E213EB64028C6A39BE77D7D7E44D2481EBFEBE8201901C011E826D58A1`
- command events: two `STARTED` and two corresponding `APPLIED`; no issues
- output remained below the fresh task-local staging root.

Output inventory:

| File | Size | SHA-256 |
| --- | ---: | --- |
| `CP3C-SYNTHETIC.wdp` | 2,711 | `43480A8CB91C9E36003A9EFF973297C681B4823FF4BED531FF058E0ED171BCCA` |
| `001_FieldDevices_PAGE-MANUAL-B.dwg` | 433,687 | `372F3C367C8D6BDF26242A78E8C9D150A684B53584704EB2C044FBBF66D5DBAE` |
| `002_Power_PAGE-MANUAL-A.dwg` | 432,883 | `93F7E6AB6E28DB5033D285D5EEFB04B5CB7DA06865C8D0D6C6256B3F563A4E4E` |

## Bounded title-block evidence

Independent read-only accoreconsole inventory of a copied generated page found:

- block identity: `CM_A3圖框模板`
- insertion: `0,0,0`
- layer: `Text`
- attribute count: `10`
- tags: `NAME`, `PART_NO.`, `TITLE`, `CHECKED`, `DRI`, `DWFG_NO.`, `PG`, `TTL`, `REV.`, `WD_TB`
- hidden `WD_TB` mapping matches the project WDT mapping.
- inspected copy SHA-256 before/after: `372F3C367C8D6BDF26242A78E8C9D150A684B53584704EB2C044FBBF66D5DBAE` / same

This proves the selected company template was used. It is not a CP3-D semantic verification claim.

## Fresh gates

- focused Component Release tests: `10 passed / 0 failed / 0 skipped`
- Desktop Release build: `PASS`, `0 warnings / 0 errors`
- focused Auto runtime tests: `13 passed / 0 failed / 0 skipped`
- `git diff --check`: required before publication

## Protected state

The company WDP, company DWT, representative formal DWG, WDT, WDL, and production Component SQLite all retained exact pre/post size and SHA-256. No `.dwl` / `.dwl2` lock remained and no AutoCAD process remained after evidence collection.

## Bounded observation

The staging WDP intentionally preserves the baseline project's existing 17 relative drawing references and appends the two generated pages. The current executor does not copy those pre-existing formal drawings into staging. Self-contained project cloning or title-block/project metadata population is not part of this onboarding checkpoint and requires separate authority.

Final: `DONE`
