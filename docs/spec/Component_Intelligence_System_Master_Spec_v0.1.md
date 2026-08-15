# Component Intelligence System（元件智慧資料系統）
## Master Specification + Code Architecture v0.1
### 完整規格書＋程式架構版（不拆最小任務）

> 目的：作為 Codex、AO、本地模型與人工工程師共同遵循的單一技術基準。
>
> 本文件不將開發拆成小任務；它描述的是「完整系統最後應該長什麼樣」。

---

# 1. 系統目標

Component Intelligence System（元件智慧資料系統）負責將最小化 BOM（Bill of Materials，物料清單）轉換為可供工程系統使用、可追溯且經驗證的 Component IR（Component Intermediate Representation，元件標準中介資料）。

使用者只需要提供：

- Manufacturer（製造商）
- Model / Part Number（型號／料號）
- Used Quantity（使用數量）
- Total Quantity（總數）
- Notes（備註，可空白）

系統自動完成：

```text
BOM Import（BOM 匯入）
        ↓
Component Resolver（元件解析器）
        ↓
Local Component Repository（本地元件資料庫）
        ↓
必要時 External Lookup（外部查詢）
        ↓
Component Enricher（元件資料補全器）
        ↓
Datasheet / Product Page / Manual / CAD
（規格書／產品頁／手冊／CAD）
        ↓
Component Normalizer（元件標準化器）
        ↓
Verification Engine（資料驗證引擎）
        ↓
Component Repository（元件資料庫）
        ↓
Component IR（元件標準中介資料）
        ↓
Wiring / Topology / Validation / Drawing
（接線／拓樸／驗證／繪圖）
```

核心原則：

1. 能用 deterministic logic（確定性程式邏輯）解決的工作，不使用 AI。
2. AI 只作為 fallback（備援）與 ambiguous case handling（歧義案例處理）。
3. AI 推論不得直接成為 VERIFIED（已驗證）工程事實。
4. 所有重要工程欄位都必須能追溯 Evidence（證據）與 Source（來源）。
5. BOM 是需求線索，不是完整工程資料庫。
6. Raw Data（原始資料）與 Normalized Data（標準化資料）永久分離。
7. 原廠 Datasheet（規格書）與官方產品頁優先於經銷商與其他網頁來源。
8. 大型檔案採 URL + Local Cache（網址＋本地快取），避免無限制永久儲存。

---

# 2. v0.1 範圍

v0.1 必須完整跑通一條真正可用的資料鏈：

```text
Excel BOM
IFM | O5D100 | 4 | 5 | 主機光電感測器
        ↓
匯入
        ↓
本地資料庫搜尋
        ↓
若無資料則查 IFM 官方來源
        ↓
確認產品身分
        ↓
取得官方產品頁與 Datasheet
        ↓
至少取得 Voltage / Output / Connector / Pin
（電壓／輸出／接頭／腳位）
        ↓
保存 Raw Data + Evidence
        ↓
Normalization
        ↓
Verification
        ↓
Component IR
        ↓
SQLite 儲存
        ↓
再次遇到同一元件時優先重用本地資料
```

v0.1 明確不做：

- 完整 UI（使用者介面）
- AutoCAD Drawing（AutoCAD 繪圖）
- Reference Designator（元件代號）自動命名
- Device Tag（設備標籤）AI 命名
- 採購價格／庫存／ERP／PLM
- 一次支援大量品牌
- 向量資料庫
- Embedding（嵌入）
- 微服務架構
- 雲端資料庫
- AI 作為工程 Source of Truth（真相來源）

---

# 3. BOM Excel 規格

## 3.1 Sheet 名稱

第一版固定：

```text
BOM
```

## 3.2 使用者可編輯欄位

| Excel 欄 | 中文 | English（英文） | 預期必填 | 說明 |
|---|---|---|---:|---|
| A | 製造商 | Manufacturer（製造商） | 是 | IFM、SICK、OMRON 等 |
| B | 型號／料號 | Model / Part Number（型號／料號） | 是 | O5D100 等 |
| C | 使用數量 | Used Quantity（使用數量） | 是 | 真正裝機數量 |
| D | 總數 | Total Quantity（總數） | 是 | 含備品總數 |
| E | 備註 | Notes（備註） | 否 | 工程用途、位置等自由文字 |

範例：

| Manufacturer（製造商） | Model / Part Number（型號／料號） | Used Quantity（使用數量） | Total Quantity（總數） | Notes（備註） |
|---|---|---:|---:|---|
| IFM | O5D100 | 4 | 5 | 主機光電感測器 |
| SICK | WTB4 | 2 | 3 | 入料位置 |
| Siemens | 6EP1333-3BA10 | 1 | 2 | 24V 電源 |

## 3.3 Excel 排版

Header Row（標題列）：
- 第 1 列
- Bold（粗體）
- 淺色背景
- Freeze Top Row（凍結第一列）
- Auto Filter（自動篩選）

建議欄寬：

```text
A Manufacturer        22
B Model / Part Number 28
C Used Quantity       16
D Total Quantity      16
E Notes               40
```

數量欄：
- Integer（整數）
- 不允許負值
- `Used Quantity <= Total Quantity`

## 3.4 備品數量

Spare Quantity（備品數量）不由使用者輸入：

```text
Spare Quantity = Total Quantity - Used Quantity
```

若資料無效則保持 `null` 並加 Validation Flag（驗證標記）。

## 3.5 寬鬆匯入

BOM 的業務規範要求 Manufacturer 與 Model 必填，但 Importer（匯入器）不得因此拒絕整列。

例如：

```text
Manufacturer = IFM
Model = 空白
```

仍匯入，但：

```text
Import Status = IMPORTED_WITH_WARNINGS
Resolution Status = WAITING_FOR_INPUT
Validation Flag = MISSING_MODEL
```

完全空白列才忽略。

---

# 4. 建議技術堆疊

核心：
- C# / .NET
- System.Text.Json（JSON 序列化）

Excel：
- ClosedXML

Database（資料庫）：
- SQLite
- Microsoft.Data.Sqlite

HTTP：
- HttpClient
- IHttpClientFactory

HTML：
- AngleSharp

Browser Automation（瀏覽器自動化）：
- Playwright
- 僅 JavaScript 動態網站使用

PDF：
- PdfPig

Testing（測試）：
- xUnit

Hash（檔案指紋）：
- SHA-256

儲存：
- SQLite：結構化資料
- Local Cache（本地快取）：PDF、圖片、CAD
- URL：永久來源索引

---

# 5. 建議 Solution / Folder Architecture（方案／資料夾架構）

```text
ComponentIntelligence/
│
├── Bom/
│   ├── BomImporter.cs
│   ├── BomTemplateGenerator.cs
│   ├── BomHeaderMapper.cs
│   ├── BomRowReader.cs
│   └── BomValidator.cs
│
├── Contracts/
│   ├── BomRow.cs
│   ├── ComponentIdentity.cs
│   ├── ComponentIdentityQuery.cs
│   ├── ComponentCandidate.cs
│   ├── ResolutionResult.cs
│   ├── RawComponentProfile.cs
│   ├── RawSpecification.cs
│   ├── ComponentPort.cs
│   ├── ComponentPin.cs
│   ├── Evidence.cs
│   └── ComponentIR.cs
│
├── Resolution/
│   ├── ComponentResolver.cs
│   ├── ManufacturerNormalizer.cs
│   ├── ModelNormalizer.cs
│   ├── IdentityMatcher.cs
│   ├── CandidateBuilder.cs
│   └── ResolutionDecisionEngine.cs
│
├── Sources/
│   ├── IComponentSource.cs
│   ├── SourcePlanner.cs
│   ├── SourceResult.cs
│   ├── IfmSource.cs
│   ├── SickSource.cs
│   ├── OmronSource.cs
│   └── GenericWebSource.cs
│
├── Network/
│   ├── ComponentHttpClient.cs
│   ├── HttpResponseWrapper.cs
│   ├── RetryPolicy.cs
│   ├── RateLimitPolicy.cs
│   ├── HtmlParser.cs
│   └── BrowserFetcher.cs
│
├── Enrichment/
│   ├── ComponentEnricher.cs
│   ├── DocumentDiscoverer.cs
│   ├── AssetDiscoverer.cs
│   ├── StructuredDataExtractor.cs
│   └── MissingDataAnalyzer.cs
│
├── Extraction/
│   ├── PdfTextExtractor.cs
│   ├── SpecificationDictionary.cs
│   ├── SpecificationParser.cs
│   ├── VoltageRawParser.cs
│   ├── CurrentRawParser.cs
│   ├── OutputTypeParser.cs
│   ├── ProtocolParser.cs
│   ├── ConnectorParser.cs
│   ├── PinTableParser.cs
│   └── PortParser.cs
│
├── Normalization/
│   ├── ComponentNormalizer.cs
│   ├── UnitNormalizer.cs
│   ├── VoltageNormalizer.cs
│   ├── CurrentNormalizer.cs
│   ├── SignalNormalizer.cs
│   ├── ProtocolNormalizer.cs
│   ├── ConnectorNormalizer.cs
│   ├── PinNormalizer.cs
│   ├── PortNormalizer.cs
│   └── CategoryNormalizer.cs
│
├── Verification/
│   ├── VerificationEngine.cs
│   ├── FieldComparator.cs
│   ├── ConflictDetector.cs
│   ├── SourceAuthority.cs
│   ├── CompletenessCalculator.cs
│   ├── ConfidenceCalculator.cs
│   └── ReadinessEvaluator.cs
│
├── Repository/
│   ├── ComponentRepository.cs
│   ├── ManufacturerAliasRepository.cs
│   ├── SourceRepository.cs
│   ├── DatabaseBootstrap.cs
│   └── SqliteSchema.cs
│
├── Cache/
│   ├── CacheManager.cs
│   ├── CacheMetadata.cs
│   ├── HashService.cs
│   ├── CacheSizeCalculator.cs
│   └── LruEvictionPolicy.cs
│
└── Logging/
    ├── ResolutionRunLogger.cs
    ├── EnrichmentRunLogger.cs
    └── EvidenceRecorder.cs
```

---

# 6. 核心 Contract（資料契約）

## 6.1 BomRow

```csharp
public sealed record BomRow
{
    public required string RowId { get; init; }

    public string? RawManufacturer { get; init; }
    public string? RawModelOrPartNumber { get; init; }

    public string? Manufacturer { get; init; }
    public string? ModelOrPartNumber { get; init; }

    public int? UsedQuantity { get; init; }
    public int? TotalQuantity { get; init; }
    public int? SpareQuantity { get; init; }

    public string? Notes { get; init; }

    public required BomImportStatus ImportStatus { get; init; }
    public IReadOnlyList<string> ValidationFlags { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string?> RawRow { get; init; }
        = new Dictionary<string, string?>();
}
```

## 6.2 ComponentIdentity

```csharp
public sealed record ComponentIdentity
{
    public required string Manufacturer { get; init; }
    public required string OfficialModel { get; init; }
    public string? Mpn { get; init; }
    public Uri? OfficialProductUrl { get; init; }
}
```

## 6.3 ComponentIdentityQuery

```csharp
public sealed record ComponentIdentityQuery
{
    public string? RawManufacturer { get; init; }
    public string? RawModel { get; init; }

    public string? NormalizedManufacturer { get; init; }
    public string? NormalizedModel { get; init; }

    public string? SearchKey { get; init; }
}
```

## 6.4 ComponentCandidate

```csharp
public sealed record ComponentCandidate
{
    public required string Manufacturer { get; init; }
    public required string OfficialModel { get; init; }
    public string? Mpn { get; init; }

    public required ComponentSourceType SourceType { get; init; }
    public Uri? ProductUrl { get; init; }

    public string? RawSourceTitle { get; init; }
    public IReadOnlyList<Evidence> Evidence { get; init; } = Array.Empty<Evidence>();
}
```

## 6.5 ResolutionResult

```csharp
public sealed record ResolutionResult
{
    public required ResolutionStatus Status { get; init; }
    public required MatchLevel MatchLevel { get; init; }

    public required ComponentIdentityQuery Input { get; init; }
    public ComponentIdentity? ResolvedIdentity { get; init; }

    public IReadOnlyList<ComponentCandidate> Candidates { get; init; }
        = Array.Empty<ComponentCandidate>();

    public IReadOnlyList<Evidence> Evidence { get; init; }
        = Array.Empty<Evidence>();
}
```

## 6.6 RawSpecification

```csharp
public sealed record RawSpecification
{
    public required string RawName { get; init; }
    public string? RawValue { get; init; }

    public string? ProposedKey { get; init; }

    public required VerificationStatus Status { get; init; }
    public IReadOnlyList<Evidence> Evidence { get; init; } = Array.Empty<Evidence>();
}
```

## 6.7 Evidence

```csharp
public sealed record Evidence
{
    public required ComponentSourceType SourceType { get; init; }
    public Uri? SourceUrl { get; init; }

    public Uri? DocumentUrl { get; init; }
    public string? DocumentHashSha256 { get; init; }
    public int? PageNumber { get; init; }

    public required ExtractionMethod ExtractionMethod { get; init; }

    public string? RawValue { get; init; }
    public required DateTimeOffset RetrievedAt { get; init; }

    public required VerificationStatus VerificationStatus { get; init; }
}
```

## 6.8 ComponentPort

```csharp
public sealed record ComponentPort
{
    public required string PortId { get; init; }
    public string? PortType { get; init; }
    public string? ConnectorFamily { get; init; }
    public string? SignalType { get; init; }
    public string? Direction { get; init; }
    public string? VoltageDomain { get; init; }
    public string? Protocol { get; init; }

    public IReadOnlyList<string> AllowedConnections { get; init; }
        = Array.Empty<string>();
}
```

## 6.9 ComponentPin

```csharp
public sealed record ComponentPin
{
    public required string PinNumber { get; init; }
    public string? Function { get; init; }
    public string? SignalType { get; init; }
    public string? Direction { get; init; }
    public string? VoltageDomain { get; init; }
    public string? Description { get; init; }

    public IReadOnlyList<Evidence> Evidence { get; init; }
        = Array.Empty<Evidence>();
}
```

---

# 7. 狀態 Enum（列舉）

```csharp
public enum ResolutionStatus
{
    WaitingForInput,
    Resolving,
    Resolved,
    Ambiguous,
    NotFound,
    Conflict,
    Failed
}

public enum MatchLevel
{
    None,
    Exact,
    Strong,
    Ambiguous
}

public enum VerificationStatus
{
    Verified,
    SingleSource,
    Conflict,
    NotAvailable,
    NotFound,
    Inferred,
    UserConfirmed
}
```

---

# 8. BOM Importer（BOM 匯入器）

主要責任：
- 讀取 `.xlsx`
- 找到 `BOM` Sheet
- 解析正式 Header
- 保留 Raw Cell（原始儲存格）
- 建立 BomRow
- 做寬鬆驗證
- 計算 Spare Quantity（備品數量）

主要 Function（函式）：

```csharp
Task<BomImportResult> ImportAsync(string filePath);
HeaderMap DetectHeaders(...);
BomRow ReadRow(...);
string? NormalizeCell(...);
BomRowValidationResult ValidateRow(BomRow row);
int? CalculateSpareQuantity(int? used, int? total);
```

Acceptance（效果）：
- `IFM | O5D100 | 4 | 5` → `SpareQuantity = 1`
- 型號空白仍匯入，但 `WAITING_FOR_INPUT`
- `Used=5, Total=4` 不改值，加入 `INVALID_QUANTITY`
- 完全空白列忽略
- Raw Row 永久保留

---

# 9. Component Resolver（元件解析器）

目的：

> 確認「這到底是哪一顆真實產品」。

主要流程：

```text
Input Validation（輸入驗證）
↓
Manufacturer Normalization（製造商正規化）
↓
Model Normalization（型號正規化）
↓
Local Lookup（本地查詢）
↓
Local HIT → Identity Verification
Local MISS → External Sources
↓
Candidate Generation（候選建立）
↓
Identity Matching（身分比對）
↓
Resolution Decision（解析決策）
```

主要 Function：

```csharp
Task<ResolutionResult> ResolveAsync(ComponentIdentityQuery query);

string? NormalizeManufacturer(string? manufacturer);
NormalizedModel NormalizeModel(string? model);

Task<ComponentRecord?> SearchLocalAsync(ComponentIdentityQuery query);

Task<IReadOnlyList<ComponentCandidate>> SearchExternalAsync(
    ComponentIdentityQuery query);

IReadOnlyList<ComponentCandidate> BuildCandidates(...);

IdentityMatchResult MatchIdentity(
    ComponentIdentityQuery query,
    ComponentCandidate candidate);

ResolutionResult DecideResolution(
    ComponentIdentityQuery query,
    IReadOnlyList<ComponentCandidate> candidates);

Task SaveResolutionRunAsync(...);
```

規則：

- 原始 Manufacturer / Model 不可修改。
- Model Normalization 第一版只允許 Trim、Case、Unicode 等安全處理。
- `- / .` 等符號不得直接移除作為官方型號。
- 可以建立額外 SearchKey（搜尋鍵）供檢索。
- `RESOLVED` 才能自動進 Enricher。
- Multiple strong candidates（多個高度候選）→ `AMBIGUOUS`
- 權威來源互相矛盾 → `CONFLICT`
- 不得自己創造型號。

---

# 10. Source Adapter（來源介接器）

所有外部來源統一實作：

```csharp
public interface IComponentSource
{
    Task<IReadOnlyList<ComponentCandidate>> SearchAsync(
        ComponentIdentityQuery query,
        CancellationToken cancellationToken = default);

    Task<ProductPage?> GetProductPageAsync(
        ComponentIdentity identity,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ComponentDocument>> DiscoverDocumentsAsync(
        ComponentIdentity identity,
        CancellationToken cancellationToken = default);

    Task<RawComponentData> ExtractAsync(
        ComponentIdentity identity,
        CancellationToken cancellationToken = default);
}
```

未來：
- IfmSource（IFM 來源）
- SickSource（SICK 來源）
- OmronSource（OMRON 來源）
- KeyenceSource（KEYENCE 來源）
- SiemensSource（Siemens 來源）
- DigiKeySource
- MouserSource
- GenericWebSource（通用網站來源）

Source Priority（來源優先）：

```text
Manufacturer Datasheet（原廠規格書）
>
Manufacturer Product Page（原廠產品頁）
>
Manufacturer Manual（原廠手冊）
>
Manufacturer Download Center（原廠下載中心）
>
Authorized Distributor（授權經銷商）
>
Trusted Third Party（可信第三方）
>
Generic Web（一般網路）
>
AI Inference（AI 推論）
```

---

# 11. Network Layer（網路層）

策略：

```text
Level 1: API / JSON
Level 2: HTTP + HTML Parser
Level 3: Playwright Browser Automation
```

主要 Function：

```csharp
Task<HttpFetchResult> FetchAsync(Uri uri, CancellationToken ct);
Task<HtmlDocumentResult> ParseHtmlAsync(string html, CancellationToken ct);
Task<BrowserFetchResult> BrowserFetchAsync(Uri uri, CancellationToken ct);
```

要求：
- 使用 `IHttpClientFactory`
- Timeout
- Retry with Backoff（退避式重試）
- HTTP 429 / 5xx 處理
- per-source Rate Limit（每來源速率限制）
- 不得一次對 500 個 BOM 元件平行開 500 個 Request

---

# 12. Component Enricher（元件資料補全器）

前提：

```text
ResolutionStatus = RESOLVED
```

目標：

> 把已確認身分的元件補成可供後續工程使用的 Raw Component Profile（原始元件完整資料）。

需要補的資料：

## Identity（身分）
- Official Manufacturer
- Official Model
- MPN
- Product Name
- Product Family
- Category
- Subcategory
- Lifecycle

## Power（電源）
- Operating Voltage
- Rated Voltage
- Voltage Type
- Current Consumption
- Maximum Current
- Power Consumption
- Polarity Protection
- Short Circuit Protection

## I/O（輸入輸出）
- Input Type
- Output Type
- DI / DO / AI / AO
- PNP / NPN
- Sourcing / Sinking
- NO / NC
- Signal Level

## Communication（通訊）
- RS-485
- IO-Link
- Ethernet
- Modbus RTU
- Modbus TCP
- PROFINET
- EtherNet/IP
- CAN / CANopen
- Baud Rate
- Address
- Duplex
- Termination

## Connector（接頭）
- Family
- Coding
- Pin Count
- Gender
- Thread
- Orientation

## Port（連接埠）
- Port ID
- Port Type
- Connector
- Signal Type
- Direction
- Voltage Domain
- Protocol
- Allowed Connections

## Pin（腳位）
- Pin Number
- Function
- Signal Type
- Direction
- Voltage Domain
- Description

## Mechanical（機械）
- Width / Height / Depth
- Weight
- Mounting
- Thread
- Housing

## Environmental（環境）
- Operating Temperature
- Storage Temperature
- IP Rating
- Humidity
- Vibration
- Shock

## Assets（資產）
- Product Page URL
- Datasheet URL
- Manual URL
- Product Image URL
- Wiring Diagram URL
- Dimension Drawing URL
- CAD URL

主要 Function：

```csharp
Task<RawComponentProfile> EnrichAsync(
    ComponentIdentity identity,
    CancellationToken cancellationToken = default);

IReadOnlyList<IComponentSource> PlanSources(ComponentIdentity identity);

Task<ProductPage?> RetrieveProductPageAsync(...);

Task<IReadOnlyList<ComponentDocument>> DiscoverDocumentsAsync(...);

Task<IReadOnlyList<RawSpecification>> ExtractStructuredDataAsync(...);

Task<CachedDocument> DownloadDocumentAsync(...);

Task<string> ComputeDocumentHashAsync(...);

Task<string> ExtractPdfTextAsync(...);

IReadOnlyList<RawSpecification> ParseSpecifications(...);

IReadOnlyList<ComponentPin> ParsePins(...);

IReadOnlyList<ComponentPort> ParsePorts(...);

Task<IReadOnlyList<ComponentAsset>> DiscoverAssetsAsync(...);

MissingDataResult AnalyzeMissingData(...);

Task SaveEnrichmentRunAsync(...);
```

---

# 13. PDF / Document Pipeline（PDF／文件流程）

```text
Datasheet URL
↓
Temporary Download（暫時下載）
↓
SHA-256
↓
PDF Text Extraction
↓
Rule Parser（規則解析）
↓
Table Parser（表格解析）
↓
若仍失敗才 AI / Vision（AI／視覺）
↓
Evidence
```

AI Extraction（AI 擷取）得到的資料：

```text
VerificationStatus = INFERRED
```

不得直接為 VERIFIED。

---

# 14. Specification Dictionary（規格字典）

不得把不同廠商欄名硬寫死在各 Parser。

範例：

```text
Operating voltage
Supply voltage
Power supply voltage
Rated supply voltage

→ power.operating_voltage
```

```text
Electrical design
Output type
Switching output

→ io.output_type
```

字典應可獨立維護、測試與擴充。

---

# 15. Component Normalizer（元件標準化器）

主要 Function：

```csharp
Task<ComponentIR> NormalizeAsync(RawComponentProfile raw);

NormalizedVoltage NormalizeVoltage(string raw);
NormalizedCurrent NormalizeCurrent(string raw);
NormalizedSignal NormalizeSignal(string raw);
NormalizedConnector NormalizeConnector(string raw);
string NormalizeProtocol(string raw);
ComponentPin NormalizePin(ComponentPin raw);
ComponentPort NormalizePort(ComponentPort raw);
NormalizedCategory NormalizeCategory(...);
```

範例：

```text
Raw:
"10...30 V DC"

Normalized:
min = 10
max = 30
unit = V
type = DC
```

```text
Raw:
"RS485"

Normalized:
"RS-485"
```

```text
Raw:
"4-pin M12 A-coded"

Normalized:
family = M12
coding = A
pins = 4
```

---

# 16. Verification Engine（驗證引擎）

目的：

> 不是只問「有沒有資料」，而是問「為什麼相信它」。

主要 Function：

```csharp
FieldVerificationResult VerifyField(...);
SourceComparisonResult CompareSources(...);
int RankSourceAuthority(ComponentSourceType type);
ConflictResult DetectConflict(...);
double CalculateCompleteness(...);
ConfidenceLevel CalculateConfidence(...);
ReadinessResult EvaluateReadiness(...);
```

Verification Status：

```text
VERIFIED（已驗證）
SINGLE_SOURCE（單一來源）
CONFLICT（來源衝突）
NOT_AVAILABLE（官方未提供）
NOT_FOUND（目前找不到）
INFERRED（推論）
USER_CONFIRMED（人工確認）
```

重要：
- Completeness（完整度）與 Confidence（可信度）分開。
- 原廠單一來源可以 Confidence 高但 Completeness 低。
- 第三方資料很多不代表 Confidence 高。

Readiness（就緒狀態）：
- ReadyForWiring（可接線）
- ReadyForTopology（可做拓樸）
- ReadyForValidation（可工程驗證）
- ReadyForDrawing（可繪圖）

---

# 17. SQLite Database Schema（資料庫結構）

主要 Table：

```text
components
manufacturer_aliases
component_aliases
component_sources
component_documents
component_raw_specs
component_normalized_specs
component_ports
component_pins
component_assets
resolution_runs
enrichment_runs
verification_results
```

SQL 骨架：

```sql
CREATE TABLE IF NOT EXISTS components (
    id TEXT PRIMARY KEY,
    manufacturer TEXT NOT NULL,
    official_model TEXT NOT NULL,
    mpn TEXT NULL,
    product_name TEXT NULL,
    category TEXT NULL,
    subcategory TEXT NULL,
    identity_status TEXT NOT NULL,
    enrichment_status TEXT NOT NULL,
    verification_status TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    last_verified_at TEXT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_components_identity
ON components(manufacturer, official_model);
```

Dynamic Specs（動態規格）：

```sql
CREATE TABLE IF NOT EXISTS component_normalized_specs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    component_id TEXT NOT NULL,
    spec_key TEXT NOT NULL,
    value_text TEXT NULL,
    value_number REAL NULL,
    unit TEXT NULL,
    status TEXT NOT NULL,
    source_id INTEGER NULL,
    FOREIGN KEY(component_id) REFERENCES components(id)
);
```

Raw Specs：

```sql
CREATE TABLE IF NOT EXISTS component_raw_specs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    component_id TEXT NOT NULL,
    raw_name TEXT NOT NULL,
    raw_value TEXT NULL,
    proposed_key TEXT NULL,
    source_id INTEGER NULL,
    status TEXT NOT NULL,
    FOREIGN KEY(component_id) REFERENCES components(id)
);
```

---

# 18. Component Repository（元件資料庫服務）

主要 Function：

```csharp
Task SaveComponentAsync(ComponentRecord component, CancellationToken ct);
Task<ComponentRecord?> GetByIdAsync(string componentId, CancellationToken ct);
Task<ComponentRecord?> FindByIdentityAsync(
    string manufacturer,
    string model,
    CancellationToken ct);

Task UpdateComponentAsync(ComponentRecord component, CancellationToken ct);

Task SaveRawSpecificationsAsync(...);
Task SaveNormalizedSpecificationsAsync(...);
Task SaveEvidenceAsync(...);
Task SavePinsAsync(...);
Task SavePortsAsync(...);
Task SaveAssetsAsync(...);

Task<ComponentIR?> LoadComponentIrAsync(string componentId, CancellationToken ct);
```

---

# 19. Cache Manager（快取管理器）

大型檔案不強制永久保存。

目錄：

```text
/cache
    /documents
    /images
    /cad
```

Metadata（中繼資料）：
- Source URL
- Local Path
- SHA-256
- File Size
- Created At
- Last Accessed

設定：
- Maximum Cache Size（最大快取），例如 10 GB
- LRU / Least Recently Used（最久未使用優先淘汰）

不得因 Cache Eviction（快取清除）而刪除：
- URL
- Hash
- Evidence
- Raw Specs
- Normalized Specs
- Component IR

---

# 20. State Machine（狀態機）

正常：

```text
IMPORTED
↓
RESOLVING
↓
RESOLVED
↓
ENRICHING
↓
ENRICHED
↓
NORMALIZING
↓
VERIFYING
↓
READY
```

例外：

```text
WAITING_FOR_INPUT
AMBIGUOUS
NOT_FOUND
PARTIAL
CONFLICT
FAILED
```

---

# 21. Component IR（元件標準中介資料）

```json
{
  "identity": {
    "component_id": "CMP-000001",
    "manufacturer": "IFM",
    "model": "O5D100",
    "mpn": "O5D100"
  },
  "classification": {
    "category": "sensor",
    "subcategory": "photoelectric_sensor"
  },
  "power": {
    "operating_voltage": {
      "min": 10,
      "max": 30,
      "unit": "V",
      "type": "DC"
    }
  },
  "io": {
    "output_type": "PNP"
  },
  "connector": {
    "family": "M12",
    "coding": "A",
    "pins": 4
  },
  "ports": [],
  "pins": [],
  "assets": {
    "product_page_url": null,
    "datasheet_url": null,
    "image_url": null,
    "cad_url": null
  },
  "readiness": {
    "wiring": false,
    "topology": false,
    "validation": false,
    "drawing": false
  }
}
```

---

# 22. AI Boundary（AI 邊界）

AI 可以：
- 解析非常複雜的 PDF
- 解析圖片 Pinout
- 協助元件分類
- 協助分析 Ambiguous Candidates
- 協助非結構化文字抽取

AI 不可以：
- 無來源創造 Model / MPN
- 無來源創造 Pin
- 無來源創造 Voltage
- 無來源創造 Connector
- 把推論直接標成 VERIFIED

所有 AI 產生工程資料：

```text
VerificationStatus = INFERRED
```

只有得到可信 Evidence 才能升級。

---

# 23. Logging / Audit（紀錄／稽核）

Resolution Run（解析紀錄）至少保存：

```text
Input Manufacturer
Input Model
Normalized Manufacturer
Normalized Model
Local Lookup Result
External Sources Queried
Candidates
Selected Candidate
Match Level
Resolution Status
Timestamp
```

Enrichment Run（補全紀錄）至少保存：

```text
Component ID
Sources Queried
Product Page
Documents Discovered
Raw Specs Extracted
Assets Discovered
Missing Fields
Errors
Timestamp
```

---

# 24. v0.1 Acceptance Criteria（驗收標準）

輸入：

```text
IFM | O5D100 | 4 | 5 | 主機光電感測器
```

必須達成：

1. Excel 可匯入。
2. Spare Quantity = 1。
3. 原始值被保存。
4. Resolver 先查 SQLite。
5. 本地無資料才使用 IFM Source。
6. 能確認 IFM O5D100 身分。
7. 保存官方產品 URL。
8. 能找到 Datasheet。
9. 至少取得 Voltage / Output Type / Connector / Pin。
10. Raw Data 保留。
11. 重要欄位有 Evidence。
12. Normalizer 產生標準值。
13. Verification Engine 給狀態。
14. Component IR 可建立。
15. SQLite 可保存。
16. 關閉程式再開可重建 Component IR。
17. 第二次遇到 O5D100 優先使用本地資料。
18. 若資料不足不得亂猜，必須輸出 PARTIAL / AMBIGUOUS / CONFLICT 等明確狀態。

---

# 25. 後續擴充原則

新增品牌時主要新增：

```text
Manufacturer Source Adapter（原廠來源介接器）
+
Manufacturer-specific Parsing Rules（品牌專屬解析規則）
```

不得重新設計：
- BOM Contract
- Component Identity Contract
- Evidence Contract
- Component IR 基礎架構
- Repository 核心介面

若新品牌真的需要新欄位，優先透過 Dynamic Specs（動態規格）擴充。

---

# 26. 最終架構口訣

```text
BOM
提供「我要什麼」

Resolver
確認「你是誰」

Enricher
取得「你的資料」

Normalizer
統一「資料格式」

Verifier
證明「為什麼相信」

Repository
永久「記住結果」

Engineering Engine
使用可信資料做「工程判斷」
```

AI 是 Exception Handler（例外處理助手），不是 Source of Truth（工程真相來源）。
