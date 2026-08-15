# Model Routing Boundary（模型調度邊界）

本文件**不得指定任何模型名稱**。Task 只定義 objective（目標）、scope（範圍）、dependency（依賴）、artifact（產物）、acceptance（驗收）與 validation（驗證）。

Actor / Model Selection（執行者／模型選擇）由外部 Skill / AO Runtime 依模型庫、能力、成本、速度與失敗升級規則自行決定。

禁止 Task 欄位：`model`、`primary_actor`、`fallback_actor`。

---

# Component Intelligence System（元件智慧資料系統）
## Model-Agnostic Bounded Implementation Specification v0.1
### 完整規格＋最小任務拆解版

> 目的：讓 Selected Worker Model（由 Skill 選擇的執行模型） 或其他本地 Coding Model（程式模型）可以用極小工作包逐步實作完整系統。
>
> 本文件與 Master Specification（主規格）使用同一架構；差別是本文件新增 Task（任務）、Dependency（依賴）、Writable Scope（可修改範圍）與 Acceptance Gate（驗收閘門）。

---

# 1. 全域實作原則

每次本地模型只能執行一個 Task ID。

每個 Task 必須符合：

```text
TASK
GOAL
CONTEXT
INPUT CONTRACT
OUTPUT CONTRACT
ALLOWED FILES
FORBIDDEN
ACCEPTANCE TESTS
STOP CONDITION
```

模型不得：
- 自行進下一 Task
- 順便重構其他模組
- 放寬測試
- 刪除失敗測試
- 跨 Scope 修改
- 因測試失敗改架構契約
- 把 AI 推論當 VERIFIED

單一 Task 建議上限：
- Production Files（正式檔）≤ 4
- Test Files（測試檔）≤ 3

每個 Task 完成回報：

```text
TASK ID
STATUS: PASS / FAIL / BLOCKED
FILES CHANGED
FUNCTIONS ADDED
TESTS ADDED
BUILD RESULT
TEST RESULT
KNOWN LIMITATIONS
NEXT TASK RECOMMENDATION
```

若 Build + Relevant Tests PASS：
- 必須停止。
- 不得執行下一 Task。

若需要跨 Scope：
- `STATUS = BLOCKED`
- 回報原因與最小需要變更。

---

# 2. 目標系統摘要

輸入 BOM：

| Manufacturer（製造商） | Model / Part Number（型號／料號） | Used Quantity（使用數量） | Total Quantity（總數） | Notes（備註） |
|---|---|---:|---:|---|
| IFM | O5D100 | 4 | 5 | 主機光電感測器 |

流程：

```text
BOM Import
↓
Resolver
↓
Local Repository
↓
若 Local MISS → IFM Source
↓
Enricher
↓
Datasheet / HTML / Assets
↓
Normalizer
↓
Verifier
↓
Component IR
↓
SQLite
```

第一版必須至少取得：
- Voltage（電壓）
- Output Type（輸出型式）
- Connector（接頭）
- Pin（腳位）

---

# 3. Phase Dependency（階段依賴）

```text
Phase 0 Contracts
↓
Phase 1 BOM
↓
Phase 2 SQLite
↓
Phase 3 Local Resolver
↓
Phase 4 Source Abstraction
↓
Phase 5 Network Infrastructure
↓
Phase 6 IFM Resolver Adapter
↓
Phase 7 Enricher Core
↓
Phase 8 Document/PDF
↓
Phase 9 Specification Extraction
↓
Phase 10 Normalizer
↓
Phase 11 Verification
↓
Phase 12 Component IR Persistence
↓
Phase 13 Cache
↓
Phase 14 End-to-End
```

任何 Phase Gate（階段閘門）未 PASS，不得自動進下一 Phase。

---

# 4. Phase 0 — Foundation / Contracts（基礎／資料契約）

## T001 — 建立 ComponentIntelligence 專案骨架

GOAL：
建立主專案與測試專案目錄，不實作功能。

MUST COMPLETE：
- ComponentIntelligence project
- ComponentIntelligence.Tests project
- 基本 folder layout
- 專案引用
- 初始 build

FORBIDDEN：
- 不得加入網路套件
- 不得建立真實 Resolver
- 不得建 SQLite schema

ACCEPTANCE：
- Solution build PASS
- Test project 可執行

---

## T002 — 建立核心 Status Enum（狀態列舉）

MUST COMPLETE：
- BomImportStatus
- ResolutionStatus
- MatchLevel
- EnrichmentStatus
- VerificationStatus
- ReadinessStatus

ACCEPTANCE：
- Enum serialization 測試
- 不得依賴外部模組

---

## T003 — ComponentIdentity Contract（元件身分契約）

MUST COMPLETE：
- ComponentIdentity
- ComponentIdentityQuery

FUNCTION：
資料結構可表示：
- raw manufacturer
- raw model
- normalized manufacturer
- normalized model
- search key
- official manufacturer
- official model
- mpn
- official url

ACCEPTANCE：
- 建構測試
- JSON serialization 測試

---

## T004 — Resolution Contract（解析契約）

MUST COMPLETE：
- ComponentCandidate
- ResolutionResult
- IdentityMatchResult

ACCEPTANCE：
可表示：
- Exact
- Strong
- Ambiguous
- NotFound
- Conflict

---

## T005 — Evidence Contract（證據契約）

MUST COMPLETE：
- Evidence
- ExtractionMethod
- ComponentSourceType

FIELDS：
- source type/url
- document url/hash
- page
- extraction method
- raw value
- retrieved time
- verification status

---

## T006 — RawSpecification Contract（原始規格契約）

MUST COMPLETE：
- RawSpecification
- ProposedKey
- Evidence list

ACCEPTANCE：
Raw value 必須可與 normalized value 分離。

---

## T007 — Port / Pin Contract（連接埠／腳位契約）

MUST COMPLETE：
- ComponentPort
- ComponentPin

Pin 至少：
- number
- function
- signal type
- direction
- voltage domain
- description
- evidence

Port 至少：
- id
- type
- connector
- signal type
- direction
- voltage domain
- protocol
- allowed connections

---

## T008 — ComponentIR Skeleton（元件 IR 骨架）

MUST COMPLETE：
- ComponentIR
- Identity
- Classification
- Power
- I/O
- Connector
- Ports
- Pins
- Assets
- Readiness

FORBIDDEN：
- 不實作 Normalizer
- 不實作 DB

PHASE 0 GATE：
- Build PASS
- Contract tests PASS

---

# 5. Phase 1 — BOM

## T009 — BomRow

MUST COMPLETE：
- 五個使用者欄位
- raw manufacturer/model
- spare quantity
- import status
- validation flags
- raw row dictionary

---

## T010 — Spare Quantity Calculator（備品計算）

FUNCTION：

```csharp
int? CalculateSpareQuantity(int? used, int? total);
```

RULE：
- valid → total - used
- invalid/null → null
- 不修改原始數量

TEST：
- 4/5 → 1
- 5/5 → 0
- 5/4 → null + invalid
- null → null

---

## T011 — BomRowValidator（BOM 資料列驗證）

FLAGS：
- MISSING_MANUFACTURER
- MISSING_MODEL
- INVALID_USED_QUANTITY
- INVALID_TOTAL_QUANTITY
- TOTAL_LESS_THAN_USED

RULE：
Validation error 不等於拒絕匯入。

---

## T012 — BomHeaderMapper（標題映射）

第一版支援正式 Header：
- Manufacturer
- Model / Part Number
- Used Quantity
- Total Quantity
- Notes

可選同義詞：
- 製造商
- 型號 / 料號
- 使用數量
- 總數
- 備註

---

## T013 — Excel Row Reader（Excel 資料列讀取）

TECH：
ClosedXML

MUST：
- 讀取單列
- Trim
- Preserve raw values
- 不擅自修改型號符號

---

## T014 — BomImporter

FUNCTION：

```csharp
Task<BomImportResult> ImportAsync(string filePath);
```

MUST：
- 找 `BOM` sheet
- detect headers
- ignore empty rows
- import non-empty rows
- apply validator
- preserve raw row

---

## T015 — BOM Template Generator（BOM 模板產生器）

MUST：
建立 `.xlsx`：
- BOM Sheet
- 5 欄
- bold header
- light background
- freeze first row
- auto filter
- suggested widths

---

## T016 — BOM Integration Tests

CASES：
- 完整 row
- missing manufacturer
- missing model
- invalid quantity
- extra blank rows
- notes blank
- raw value preservation

PHASE 1 GATE：
`IFM | O5D100 | 4 | 5` → BomRow + Spare=1

---

# 6. Phase 2 — SQLite Repository

## T017 — SQLite Connection Factory

MUST：
- open database
- create file if missing
- connection string centralized

---

## T018 — DatabaseBootstrap

FUNCTION：

```csharp
Task InitializeAsync(CancellationToken ct);
```

MUST：
- idempotent
- repeated run safe

---

## T019 — Components Schema

TABLE：
components

FIELDS：
- id
- manufacturer
- official_model
- mpn
- product_name
- category
- subcategory
- identity_status
- enrichment_status
- verification_status
- created_at
- updated_at
- last_verified_at

UNIQUE：
manufacturer + official_model

---

## T020 — Source / Document Schema

TABLE：
- component_sources
- component_documents

MUST：
保存 source authority / urls / hashes / timestamps。

---

## T021 — Raw Specs Schema

TABLE：
component_raw_specs

---

## T022 — Normalized Specs Schema

TABLE：
component_normalized_specs

支援：
- text value
- numeric value
- unit
- status
- source id

---

## T023 — Port / Pin Schema

TABLE：
- component_ports
- component_pins

---

## T024 — Run Log Schema

TABLE：
- resolution_runs
- enrichment_runs
- verification_results

---

## T025 — SaveComponentAsync

MUST：
新增 component。

---

## T026 — GetByIdAsync

MUST：
不存在回 null，不丟假資料。

---

## T027 — FindByIdentityAsync

INPUT：
manufacturer + model

MUST：
exact identity lookup。

---

## T028 — UpdateComponentAsync

MUST：
更新允許欄位。
不得更換 primary identity 而不留紀錄。

---

## T029 — Repository CRUD Tests

PHASE 2 GATE：
Save / Read / Find / Update 全 PASS。

---

# 7. Phase 3 — Local Resolver Core

## T030 — ManufacturerNormalizer

FUNCTION：

```csharp
string? Normalize(string? manufacturer);
```

MUST：
- Trim
- Case normalization
- Alias lookup hook

TEST：
`ifm`, `IFM`, `ifm electronic` 可映射 IFM（透過 alias）。

---

## T031 — ModelNormalizer

MUST：
- Trim
- Unicode normalize
- case normalize for search
- preserve raw model
- generate SearchKey

FORBIDDEN：
不得直接把官方 Model 中 `-`, `/`, `.` 全部刪除。

---

## T032 — ManufacturerAliasRepository

MUST：
- save alias
- resolve alias
- canonical manufacturer

---

## T033 — LocalComponentLookup

FUNCTION：

```csharp
Task<ComponentRecord?> FindAsync(ComponentIdentityQuery query);
```

---

## T034 — IdentityMatcher

比較：
- manufacturer exact
- official model exact
- normalized model match
- mpn match
- source authority

不得看 description 就判定唯一產品。

---

## T035 — ResolutionDecisionEngine

OUTPUT：
- EXACT
- STRONG
- AMBIGUOUS
- NOT_FOUND
- CONFLICT

---

## T036 — ComponentResolver Local Pipeline

FUNCTION：

```csharp
Task<ResolutionResult> ResolveAsync(ComponentIdentityQuery query);
```

第一版只：
- validate
- normalize
- local lookup
- decision

---

## T037 — Local Resolver Tests

CASES：
- local hit → RESOLVED EXACT
- missing → NOT_FOUND
- missing input → WAITING_FOR_INPUT

PHASE 3 GATE：
不使用網路也可正確解析 Local HIT / MISS。

---

# 8. Phase 4 — Source Architecture

## T038 — IComponentSource

建立統一 Interface。

---

## T039 — SourceResult / ProductPage / ComponentDocument

建立來源資料 DTO（資料物件）。

---

## T040 — SourcePlanner

INPUT：
ComponentIdentity / Manufacturer

OUTPUT：
排序後來源列表。

---

## T041 — FakeComponentSource

回傳固定假資料：
IFM O5D100

用途：
測試外部流程，不碰真網站。

---

## T042 — Resolver External Hook

Local MISS 時可呼叫 SourcePlanner + Sources。

---

## T043 — External Source Tests

PHASE 4 GATE：

```text
Local MISS
→ Fake Source
→ Candidate
→ RESOLVED
```

---

# 9. Phase 5 — Network Infrastructure

## T044 — HttpClient Infrastructure

使用：
IHttpClientFactory

---

## T045 — HTTP Response Wrapper

統一：
- success
- status code
- body
- error
- timing

---

## T046 — Retry Policy

處理：
- timeout
- 429
- 500/502/503/504

使用 Backoff（退避）。

---

## T047 — Rate Limit Contract

per source：
- max concurrency
- minimum delay
- max retry

---

## T048 — HTML Parser Wrapper

AngleSharp wrapper。

---

## T049 — Fake HTTP Tests

不得依賴真網路。

PHASE 5 GATE：
Fake HTTP 可重試、可解析 HTML。

---

# 10. Phase 6 — IFM Resolver Adapter

## T050 — IfmSource Skeleton

只建立 class + interface implementation skeleton。

---

## T051 — IFM Product Search

INPUT：
IFM + model

OUTPUT：
Candidate list

不得解析全部 specs。

---

## T052 — IFM Identity Parser

從官方頁讀：
- official model
- product name if available
- mpn if available

---

## T053 — IFM Official Product URL

保存官方 URL。

---

## T054 — IFM Candidate Builder

把 IFM raw result → ComponentCandidate。

---

## T055 — IFM Resolver Integration

接入 ComponentResolver。

---

## T056 — IFM Fixture Tests

用保存的 HTML Fixture（固定測試頁）離線測試。

---

## T057 — Optional Live IFM Test

可選。
失敗不得使單元測試紅燈。

PHASE 6 GATE：

```text
IFM + O5D100
→ RESOLVED
→ Official Product URL
```

---

# 11. Phase 7 — Component Enricher Core

## T058 — RawComponentProfile Contract

包含：
- identity
- raw specs
- raw pins
- raw ports
- documents
- assets
- evidence
- missing data

---

## T059 — ComponentEnricher Skeleton

建立流程控制，不做品牌細節。

---

## T060 — Enrichment Source Planning

決定：
- product page
- datasheet
- manual
- download center
- secondary source

---

## T061 — Product Page Retrieval

讀取已解析官方頁。

---

## T062 — DocumentDiscoverer

找：
- Datasheet
- Manual
- Wiring Diagram
- Dimension Drawing

---

## T063 — AssetDiscoverer

找：
- Product Image
- CAD URL

---

## T064 — StructuredDataExtractor

優先：
- JSON
- metadata
- HTML tables/labels

---

## T065 — MissingDataAnalyzer

依 Category 判斷必要資料缺口。

第一版 Sensor Profile 至少：
- Voltage
- Output
- Connector
- Pin

---

## T066 — EnrichmentRunLogger

保存整次補全結果與失敗原因。

PHASE 7 GATE：
可產生 RawComponentProfile，即使部分欄位為空。

---

# 12. Phase 8 — Document / PDF

## T067 — DocumentDownloader

下載到 cache，支援 cancellation。

---

## T068 — SHA256 HashService

FUNCTION：
檔案 → lowercase/consistent SHA-256 string。

---

## T069 — CacheMetadata

保存：
- URL
- local path
- hash
- size
- timestamps

---

## T070 — PdfTextExtractor

PdfPig。
回傳逐頁文字或可追蹤 page number。

---

## T071 — Datasheet Fixture Test

使用固定 PDF Fixture。

---

## T072 — Document Evidence

將 PDF hash/page/method 寫進 Evidence。

PHASE 8 GATE：
可從 fixture PDF 取得可追蹤文字。

---

# 13. Phase 9 — Specification Extraction

## T073 — SpecificationDictionary

獨立 mapping。

---

## T074 — VoltageRawParser

只抽 Raw：
`10...30 V DC`

不得在本 Task 做 normalized min/max。

---

## T075 — CurrentRawParser

抽 Raw Current。

---

## T076 — OutputTypeParser

抽：
- PNP
- NPN
- Relay
等 raw output。

---

## T077 — ProtocolParser

抽：
- RS-485
- IO-Link
- Ethernet
- Modbus...
Raw。

---

## T078 — ConnectorParser

抽：
- M8 / M12 / RJ45...
Raw。

---

## T079 — PinTableParser

先支援簡單文字 Pin Table。

---

## T080 — PortParser

先支援明確 Port 資料。

---

## T081 — Extraction Integration

把 extraction 結果放進 RawComponentProfile。

PHASE 9 GATE：
IFM fixture 至少取得 Voltage / Output / Connector / Pin。

---

# 14. Phase 10 — Normalizer

## T082 — UnitNormalizer

支援：
- V / mV
- A / mA

---

## T083 — VoltageNormalizer

支援：
- 24 VDC
- 10...30 V DC
- 10 to 30VDC
- DC 10-30V

OUTPUT：
- min
- max
- unit
- type

---

## T084 — CurrentNormalizer

支援：
- 0.5 A
- 500 mA
- <25 mA

---

## T085 — SignalNormalizer

支援：
- PNP
- NPN
- sourcing
- sinking

---

## T086 — ProtocolNormalizer

例：
RS485 → RS-485

---

## T087 — ConnectorNormalizer

例：
4-pin M12 A-coded →
- family M12
- coding A
- pins 4

---

## T088 — PinNormalizer

Raw Pin → standard ComponentPin。

---

## T089 — PortNormalizer

Raw Port → standard ComponentPort。

---

## T090 — CategoryNormalizer

先支援：
- Sensor
- Power Supply
- PLC
- Connector
- Cable
- Communication Device

---

## T091 — ComponentNormalizer

串起上述 normalizers。

PHASE 10 GATE：
Raw Profile → deterministic normalized result。

---

# 15. Phase 11 — Verification

## T092 — SourceAuthority

建立固定來源優先順序。

---

## T093 — FieldEvidence Model Integration

每一規格能附 Evidence。

---

## T094 — FieldComparator

比較 normalized same-key values。

---

## T095 — ConflictDetector

不同可信來源不同值 → CONFLICT。

---

## T096 — Verification Status Decision

產生：
VERIFIED / SINGLE_SOURCE / CONFLICT / NOT_FOUND / ...

---

## T097 — CompletenessCalculator

依 category profile 計算找到多少。

---

## T098 — ConfidenceCalculator

依 source quality / conflict / evidence 計算可信等級。

不得直接等同 Completeness。

---

## T099 — WiringReadiness

第一版 Sensor 至少需：
- power
- connector or cable
- relevant pins
- output/protocol

---

## T100 — TopologyReadiness

檢查：
- ports
- protocol
- direction / connection constraints

---

## T101 — ValidationReadiness

檢查：
工程驗證必要欄位是否足夠。

---

## T102 — DrawingReadiness

檢查：
繪圖必要資料；Image/CAD 缺失可依繪圖模式降級。

---

## T103 — VerificationEngine Integration

PHASE 11 GATE：
同值 → VERIFIED；
衝突 → CONFLICT；
完整度與可信度分開輸出。

---

# 16. Phase 12 — Component IR / Persistence

## T104 — ComponentIR Builder

Normalized Profile + Verification → ComponentIR。

---

## T105 — Save Raw Specs

---

## T106 — Save Normalized Specs

---

## T107 — Save Evidence

---

## T108 — Save Pins

---

## T109 — Save Ports

---

## T110 — Save Assets

只要求 metadata/url；大檔案可只 cache。

---

## T111 — Load ComponentIR

從 DB 重建 IR。

PHASE 12 GATE：
關閉再開，Component IR 可重建。

---

# 17. Phase 13 — Cache

## T112 — CacheDirectoryManager

建立：
- documents
- images
- cad

---

## T113 — CacheAccessTracker

更新 LastAccessed。

---

## T114 — CacheSizeCalculator

取得總 bytes。

---

## T115 — LruSelector

選最久未使用 cache asset。

---

## T116 — CacheEviction

超上限刪實體 cache。
不得刪 DB metadata / Evidence / URL。

---

## T117 — Cache Tests

PHASE 13 GATE：
LRU 可刪檔但不損害 Component IR。

---

# 18. Phase 14 — End-to-End（端到端）

## T118 — Offline E2E Fixture

Excel → Fake/Fixture Source → Raw → Normalize → Verify → SQLite → IR。

---

## T119 — IFM O5D100 E2E

真正樣本完整跑通。

---

## T120 — Existing Component Reuse

第二次：
Local HIT → 不重複走完整下載。

---

## T121 — Missing Model Case

BOM 可匯入；
Resolver → WAITING_FOR_INPUT。

---

## T122 — Ambiguous Case

多個強候選：
不得自動亂選。

---

## T123 — Conflict Case

權威來源衝突：
輸出 CONFLICT + Evidence。

---

## T124 — Database Reload

程式重新啟動後：
資料仍在；
IR 可重建。

---

## T125 — Final Regression Suite

所有主要模組回歸測試。

FINAL GATE：

```text
Excel:
IFM | O5D100 | 4 | 5

→ Import
→ Spare = 1
→ Resolve
→ Official URL
→ Datasheet
→ Voltage
→ Output
→ Connector
→ Pin
→ Raw Data
→ Evidence
→ Normalize
→ Verify
→ Component IR
→ SQLite
→ Reload
→ Local Reuse
```

---

# 19. Worker Model（執行模型） Task Packet Template（任務包模板）

```text
TASK
T0XX — <名稱>

GOAL
只完成 <單一功能>。

CONTEXT
本 Task 所需最小上下文。

INPUT CONTRACT
列出可以依賴的已完成資料結構／介面。

OUTPUT CONTRACT
列出本 Task 必須新增／修改的 public API。

ALLOWED FILES
只允許：
- ...
- ...

FORBIDDEN
- 不得執行下一 Task
- 不得修改 Contract 除非本 Task 明確要求
- 不得修改其他模組
- 不得新增未要求的第三方套件
- 不得刪除／放寬測試
- 不得用 AI 猜工程資料

ACCEPTANCE TESTS
1. ...
2. ...
3. ...

BUILD
指定 build command。

TEST
指定 test command。

STOP CONDITION
上述 Build + Tests PASS 後立即停止並回報。
```

---

# 20. Reviewer（審查模型）規則

建議：
- Selected Worker Model（由 Skill 選擇的執行模型）：Implementation（實作）
- Selected Reviewer Model（由 Skill 選擇的審查模型）：Review（審查）

Reviewer 只檢查：
- Contract compliance（契約符合）
- Scope compliance（範圍符合）
- Tests
- obvious bugs
- hard-coded assumptions
- accidental cross-module changes

Reviewer 不得自行做大規模重構。

---

# 21. AI Runtime Boundary（執行期 AI 邊界）

AI 只處理：
- 複雜 PDF
- 圖片 Pinout
- 歧義候選
- 非結構化分類

AI 輸出：
`INFERRED`

除非後續由可信 Evidence 證明，否則不得 `VERIFIED`。

---

# 22. 最終開發哲學

```text
Contract 先固定
↓
小 Task 實作
↓
Compiler
↓
Unit Test
↓
Reviewer
↓
Gate
↓
下一 Task
```

而不是：

```text
把整份規格丟給 Worker Model（執行模型）
↓
叫它一次做完整系統
```

系統架構不由 Worker Model（執行模型） 即興決定；Worker Model（執行模型） 的工作是忠實完成已定義的小型 Implementation Task（實作任務）。
