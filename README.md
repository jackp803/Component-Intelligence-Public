# Component Intelligence System

Component Intelligence 是電子／電氣／工控元件的可重複工程知識系統。Repository 同時保存既有 runnable MVP，以及一套不綁定特定 AI 的 **Component Intelligence Archive Capability v1**。

## Component Intelligence Archive Capability v1

### 核心架構

```text
AI / Chat / Codex / Local Agent
        ↓
Skill
        ↓
GitHub Authority
        ↓
Validator
        ↓
Google Drive Archive
```

這個架構的目的，是讓今天的 ChatGPT、未來的 Chat、Codex、Claude 或本地 Agent 都能使用同一套歸檔制度，而不依賴某個聊天室的歷史記憶。

責任分工：

| Layer | 責任 |
|---|---|
| AI / Chat / Codex / Local Agent | 查資料、讀證據、做工程語意判斷、提出候選變更 |
| Skill | 定義歸檔工作怎麼執行 |
| GitHub Authority | 定義現在什麼規格才是正式工程規則 |
| Validator | 用程式阻擋可確定的結構／一致性錯誤 |
| Google Drive Archive | 保存真正的 Components / Ports / Pins / Documents 中央資料 |

**GitHub is not a second component database.** GitHub 保存規則、Skill、schema、Validator、eval、manifest 與 continuity 文件；真正中央元件資料仍留在 Google Drive。

### START HERE — 給人類與新的 AI

新的 AI 不需要讀舊聊天紀錄。依序讀：

1. [AI portability](docs/archive/AI_PORTABILITY.md)
2. [Archive authority](docs/archive/AUTHORITY.md)
3. [Archive Skill](skills/component-intelligence-archive/SKILL.md)
4. [Authoritative Archive Spec v2](docs/COMPONENT_ARCHIVE_SPEC_V2.md)
5. [Tool capability contract](skills/component-intelligence-archive/references/TOOL_CONTRACT.md)
6. [Validator](archive/validator/README.md)
7. [Machine-readable contracts](archive/contracts/)
8. [Reference evals](archive/evals/)
9. [Central archive manifest](archive/manifests/CENTRAL_ARCHIVE.md)
10. [Data continuity](docs/archive/DATA_CONTINUITY.md)

Archive policy 的正式選擇與 precedence 永遠從 `docs/archive/AUTHORITY.md` 開始；production archive-governance discovery branch 是 `main`。

### Policy / Procedure / Enforcement

這套系統刻意避免規則套娃：

```text
Policy       → docs/COMPONENT_ARCHIVE_SPEC_V2.md
Procedure    → skills/component-intelligence-archive/SKILL.md
Enforcement  → archive/validator/
Archive Data → Google Drive
```

原則是：

```text
Policy once.
Procedure once.
Enforcement once.
```

Skill 不重新發明 `Unknown != NC`、Port/Pin、Direction、Ready Gate 等工程語意；它只要求 AI 先取得 Authority，再依規格工作。

### 目前 v1 已實作

| Capability | Status |
|---|---|
| GitHub Authority / precedence | **Implemented** |
| Authoritative `docs/COMPONENT_ARCHIVE_SPEC_V2.md` path | **Implemented** |
| Portable `component-intelligence-archive` Skill | **Implemented** |
| Provider-neutral Tool Contract | **Implemented** |
| Archive changeset / validation / readback schemas | **Implemented** |
| Deterministic Archive Validator | **Implemented** |
| Stable rule IDs + authority mapping | **Implemented** |
| `archive-validate` CLI | **Implemented** |
| OMRON F03-20 / K7L-AT50DP / IFM AL1342 / AL5021 eval corpus | **Implemented** |
| Google Drive central archive manifest | **Implemented** |
| AI portability / data continuity / change policy | **Implemented** |
| Automated `lookup_component` service endpoint | **not implemented** |
| Automated `prepare_changeset` service endpoint | **not implemented** |
| Automated `commit_changeset` central write | **not implemented** |
| Automated `readback_changeset` service endpoint | **not implemented** |
| Automated `sync_archive` service endpoint | **not implemented** |

因此目前 **Validator PASS 不等於中央資料已經被寫入**。如果 AI 沒有外部授權寫入工具，正確終態是：

```text
VALIDATED_NOT_WRITTEN
```

只有實際 write、必要 sync、readback 全部成功，工作才可以回報 `COMPLETE`。

### Validator 可以保證什麼

目前 Validator 會檢查例如：

- Manufacturer + exact Model identity，且每個 changeset 只能有一個 target Component row；
- PortID / PortName / PinID / PinNumber 不可空白；
- changeset JSON 遇到未知／拼錯欄位會拒絕，不會靜默忽略；
- case-insensitive duplicate ComponentID / PortID / PinID；
- Component → Port → Pin ownership；
- stable engineering ID mutation；
- 已知 PinCount、宣告的 ActualPinCount 與 physical Pin rows 是否一致；
- 同一 Port 內重複 PinNumber/contact identifier；
- NC / Reserved 是否缺 explicit evidence；
- Archive file path 是否為 `Documents/...` 相對路徑；
- CREATE 是否與已存在 component 衝突；
- Ready 是否與 pin completeness 明顯矛盾；
- source conflict / unresolved unknown 是否需 Review。

詳細 rule map：

- [archive/validator/rules/RULES.md](archive/validator/rules/RULES.md)

但 Validator **不能證明研究完整性**。它無法單靠 schema 判斷 AI 是否漏掉另一個 physical Port、是否找錯 variant、是否漏讀 manual 某頁，或是否正確理解複雜 wiring drawing。這些仍需要 evidence + reasoning，必要時人工 review。

### 使用 Archive Validator CLI

對符合 `archive/contracts/archive-changeset.schema.json` 的 changeset：

```powershell
dotnet run --project src/ComponentIntelligence.Cli/ComponentIntelligence.Cli.csproj -- archive-validate path\to\changeset.json
```

Exit code：

| Code | Meaning |
|---:|---|
| 0 | PASS |
| 2 | REVIEW_REQUIRED |
| 3 | REJECT |
| 64 | Input / usage / tool failure |

CLI 只驗證 changeset，不會自行修改 Google Drive。

### 中央 Archive

正式 archive storage contract：

```text
Component Intelligence/
├─ Component_Intelligence_Database
├─ Component_Intelligence_Database.xlsx
└─ Documents/
   └─ <Manufacturer>/
      └─ <Model>/
```

正式工程資料核心：

- `Components`
- `Ports`
- `Pins`

詳細位置／同步責任請讀：

- [archive/manifests/CENTRAL_ARCHIVE.md](archive/manifests/CENTRAL_ARCHIVE.md)
- [docs/CENTRAL_WORKBOOK_KNOWLEDGE_V1.md](docs/CENTRAL_WORKBOOK_KNOWLEDGE_V1.md)

GitHub 不保存中央資料的第二份結構化 replica。這是 Architecture v1 選定的 continuity 方案。

---

## Existing Runnable MVP

Repository 原本的 runnable MVP 仍保留。它依 `docs/spec/Component_Intelligence_System_Master_Spec_v0.1.md` 提供：

```text
BOM Import
→ Local SQLite lookup
→ Component Resolver
→ IFM O5D100 deterministic seed source
→ Enricher
→ Normalizer
→ Verification
→ Component IR
→ SQLite snapshot
→ second-run local reuse
```

目前可直接 Run 的既有功能：

- Windows WPF GUI：匯入 BOM、產生範本、開始處理、查看元件詳細資料。
- 產生標準 `BOM` Excel template。
- 匯入 `.xlsx`，保留 raw values、寬鬆驗證、計算 Spare Quantity。
- Resolver / Enricher / Normalizer / Verification / Component IR pipeline。
- IFM O5D100 deterministic offline acceptance demo。
- SQLite 保存與第二次查詢重用。
- CLI demo、template、BOM run。
- 新增的 archive changeset deterministic validation CLI。

### Legacy runtime note

Archive Capability v1 建立的是新的 durable governance / Skill / validation boundary。既有 application code 中仍可能存在較早期的 Notion、SQLite 或 source-adapter runtime path；這個 PR **不宣稱**已把所有舊 runtime storage adapter 全面改寫成新的 Google Drive Archive Service。

新的中央歸檔決策以 `docs/archive/AUTHORITY.md` 選定的規格為準。自動 central write/readback/sync service 尚未實作，不能把 legacy runtime 行為誤認為新的 ArchiveWrite service。

---

## Environment

- Windows 10 / 11
- .NET 8 SDK

## Build / Test

```powershell
dotnet restore ComponentIntelligence.sln
dotnet build ComponentIntelligence.sln --configuration Release --no-restore
dotnet test ComponentIntelligence.sln --configuration Release --no-build
```

## Desktop GUI

```powershell
dotnet run --project src/ComponentIntelligence.Desktop/ComponentIntelligence.Desktop.csproj
```

GUI 既有功能包括：

1. **匯入 BOM**
2. **產生 BOM 範本**
3. **開始處理**
4. **元件詳細資料**

SQLite 預設位於目前 Windows 使用者的 Local Application Data，不會寫入 Git repository。

## CLI

### Deterministic MVP Demo

```powershell
dotnet run --project src/ComponentIntelligence.Cli/ComponentIntelligence.Cli.csproj -- demo
```

Acceptance component：

```text
IFM | O5D100 | 4 | 5 | 主機光電感測器
```

### Generate BOM Template

```powershell
dotnet run --project src/ComponentIntelligence.Cli/ComponentIntelligence.Cli.csproj -- template BOM.xlsx
```

### Import BOM and Run Pipeline

```powershell
dotnet run --project src/ComponentIntelligence.Cli/ComponentIntelligence.Cli.csproj -- run BOM.xlsx
```

指定 SQLite：

```powershell
dotnet run --project src/ComponentIntelligence.Cli/ComponentIntelligence.Cli.csproj -- run BOM.xlsx --db artifacts/component-intelligence.db
```

### Validate Archive Changeset

```powershell
dotnet run --project src/ComponentIntelligence.Cli/ComponentIntelligence.Cli.csproj -- archive-validate path\to\changeset.json
```

---

## O5D100 Offline Demo Limitation

`IfmO5D100SeedSource` 是 deterministic offline seed adapter，只用於 v0.1 acceptance component `IFM O5D100`。工程值保留 `SingleSource`，不標成 `Verified`，Pin function 不自行猜測。

它不是 production-complete manufacturer adapter。

---

## Model Routing Boundary

本專案不固定模型。

Archive Capability 的設計要求 AI 可替換：只要新的 AI 能讀 GitHub Authority、使用 Skill、取得必要 evidence/tool capabilities、產生 changeset 並通過 Validator，就能使用同一套歸檔制度。

真正不能被替換掉的是：

```text
GitHub Authority
+ Archive Skill
+ Contracts
+ Validator
+ Google Drive Central Archive
```

而不是任何特定 Chat、模型或歷史對話。
