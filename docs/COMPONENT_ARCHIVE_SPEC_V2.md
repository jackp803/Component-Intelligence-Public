# Component Intelligence Archive Spec v2｜中央電料歸檔權威規格

Status: **Authoritative / 權威**  
Applies to: Google Drive central archive, `Component_Intelligence_Database.xlsx`, native Google Sheet mirror, GPT archiving workflow, Topology / Layout / Wiring downstream consumers.

> 本文件是目前元件歸檔的主要規格。若其他舊文件、聊天紀錄或程式註解與本文件衝突，以本文件與實際程式契約為準；無法確認的內容必須維持 `Unknown` / `Review`，不得猜測。

## 1. 目標與資料流

中央資料庫保存「可重複使用的元件工程知識」，不是單一專案的臨時畫面資料。

```text
Manufacturer + Model / Part Number
        ↓
Human / GPT archive
        ↓
Google Drive central archive
  ├─ Components
  ├─ Ports
  ├─ Pins
  └─ Documents/<Manufacturer>/<Model>/...
        ↓
Component_Intelligence_Database.xlsx
        ↓
Desktop read-only lookup
        ↓
Component IR → local SQLite cache
        ↓
Topology / Layout / Electrical Design / Wiring
```

Desktop 不得把網路搜尋、PDF parser 或 guessed data（猜測資料）當成中央真值。中央資料寫入由歸檔流程負責。

## 2. 中央檔案結構

```text
Component Intelligence/
├─ Component_Intelligence_Database.xlsx
└─ Documents/
   └─ <Manufacturer>/
      └─ <Model>/
         ├─ datasheet.pdf
         ├─ manual.pdf
         ├─ product.jpg|png
         ├─ mechanical_drawing.pdf|png
         ├─ wiring_drawing.pdf
         ├─ cad_2d.dxf
         └─ cad_3d.step
```

規則：

- 一個 Manufacturer + Model / Part Number 對應一個主要資料夾。
- 只有真正存在的檔案才建立，不建空白 placeholder file（佔位檔）。
- Workbook 內只存 relative path（相對路徑），例如 `Documents/OMRON/F03-20/mechanical_drawing.png`。
- 禁止保存固定 `C:\...`、`D:\...` 或 Google Drive drive-letter 路徑。
- 原廠官方 URL 可另外保留，但不能取代本地已歸檔的 `DatasheetPath` / `DrawingPath`。
- Native Google Sheet 是人類/GPT 可編輯的中央表面；Desktop 目前讀 `.xlsx`。兩者必須同步。若兩者衝突，以最新已確認的中央 Google Sheet 內容為準，重新輸出 `.xlsx`。

## 3. Source Priority｜來源優先級

工程資料來源依優先順序：

1. Exact-model manufacturer datasheet / manual / wiring / mechanical drawing（精確型號原廠文件）。
2. Exact-model manufacturer product page（精確型號原廠產品頁）。
3. Manufacturer family document，且文件明確涵蓋該 exact model / variant。
4. User-provided official vendor drawing / approved engineering drawing / supplier evidence（客製料可用）。
5. Authorized distributor / aggregator，只能作 identity、公開文件連結或候選資料輔助；不可單獨推翻原廠文件。
6. OCR / TableParser / AI extraction 只算 candidate evidence（候選證據），必須通過工程語意檢查後才可寫入 Pins / Ports。

若來源衝突：

- 優先 exact model、較新 revision、原廠正式文件。
- 不得把同系列不同 variant 的 Pin / Voltage / Connector 資料合併。
- 無法判斷時保留 `Review`，在 `Notes` 記錄衝突，不自行選一個看起來合理的答案。

## 4. Identity 與 ID 規則

### 4.1 Component Identity

最低正式身份：

- `Manufacturer`
- `Model` / Part Number

若缺任一項，不得創造假的官方身份。

### 4.2 Stable IDs

新資料建議：

```text
ComponentID = <MANUFACTURER>_<MODEL>
PortID      = <ComponentID>_<stable port name>
PinID       = <PortID>_<PinNumber or stable contact identifier>
```

例如：

```text
OMRON_K7L-AT50DP
OMRON_K7L-AT50DP_SENSING
OMRON_K7L-AT50DP_SENSING_2
```

ID 是 stable engineering key（穩定工程鍵），不要因 UI 顯示名稱改字就改 ID。`PortName` / `PinName` 是人類可讀名稱。

## 5. Components Sheet｜元件表

目前核心欄位：

`ComponentID, Manufacturer, Model, Category, Description, Voltage, IOType, OutputType, Protocol, GeometryType, WidthMm, HeightMm, DepthMm, DiameterMm, MountingType, DatasheetPath, ImagePath, DrawingPath, TopologyStatus, LayoutStatus, DatasheetURL`

目前可使用的延伸欄位包含 `DrawingURL`, `Fastener`；未來可增加欄位，但不得把不同語意硬塞進既有欄位。

### 5.1 Unknown / blank

- Unknown textual value（未知文字值）→ `Unknown`。
- Unknown numeric value（未知數字值）→ blank。
- `NotApplicable` 只用在「確定不適用」，不能拿來代表不知道。

### 5.2 Category

Category 要描述真正的 component type（元件類型），不要只因 Description 出現某個字就分類。

例如：

- 真正 sensor / transmitter → Sensor 類型。
- `Liquid Leakage Sensor Amplifier` 是 amplifier/controller，不應因名稱含 Sensor 就被當成普通 Sensor。
- IO-Link Master、PLC、Power Supply、Terminal Block 各自保存真實分類。

Sensor 自動排列到 Topology 最右側是 **UI presentation rule（畫面規則）**，不是歸檔欄位。歸檔只負責正確 Category / Type，不存「應該在畫面右邊」。

## 6. Ports Sheet｜接口表

核心欄位：

`PortID, ComponentID, PortName, PortRole, Direction, SignalType, Voltage, Protocol, Connector, ConnectorCoding, Gender, PinCount, ActualPinCount, PinCompleteness, PhysicalSide, SourcePage, Notes, TopologyEndpointMode`

### 6.1 Port ≠ Pin

Port 是 physical/logical interface（實體／邏輯接口）；Pin 是該 Port 底下的 contact / terminal / conductor（接點／端子／線芯）。不可混用。

### 6.2 PortRole 與 Direction 必須分開

`PortRole`：Topology / functional role（拓樸／功能角色）。  
`Direction`：真實 electrical behavior（電氣方向）。

Direction 建議值：

`Input, Output, Bidirectional, Mixed, Passive, Unknown`

Pin Direction 可另外使用 `Return`。

禁止為了讓 UI 左右位置正確而竄改 Direction。

例：

```text
OMRON F03-20 INPUT
PortRole  = Input Port
Direction = Passive

OMRON F03-20 OUTPUT
PortRole  = Output Port
Direction = Passive
```

這樣可保留真實 Passive 行為，同時讓 Topology 使用 PortRole 顯示 Input-left / Output-right。

### 6.3 K7L-AT50DP SENSING 範例

OMRON K7L-AT50DP 的 terminal 2 / 3 / 4 屬於同一 sensing interface：

```text
PortName = SENSING
PortRole = Sensor Input
Direction = Mixed
TopologyEndpointMode = Pins
```

Pin 2、3 是 Input/receive semantics；Pin 4 會送出 sensing signal，因此整個 Port 的 `Direction=Mixed` 是真實電氣資料。但整組在 Topology 的 functional role 是 Sensor Input，因此 2/3/4 應顯示在元件左側。

### 6.4 AL1342 X01~X08 範例

IFM AL1342 X01~X08：

```text
PortRole = IO-Link Port Class A
Direction = Mixed
Connector = M12
ConnectorCoding = A
TopologyEndpointMode = Connector
```

此 Port 同時包含 sensor supply output、DI input、return、IO-Link C/Q，因此不得為了畫面位置硬改成 `Direction=Output`。目前 Topology 對這種 mixed / non-directional role 使用 presentation fallback，預設放右側。這是程式規則，不是歸檔造假的理由。

### 6.5 PhysicalSide

`PhysicalSide` 只描述真實機構位置，例如 `Front / Left / Right / Top`。沒有原廠證據就 `Unknown`。

`PhysicalSide` 不等於 Topology screen side（畫面左右）。

## 7. Pins Sheet｜腳位表

核心欄位：

`PinID, PortID, PinNumber, PinName, PinRole, Direction, SignalType, Voltage, Function, PinStatus, SourcePage, Notes`

PinStatus：

`Used, Unused, NC, Reserved, Optional, Unknown, NotApplicable`

語意：

- `Used`：標準接法使用。
- `Unused`：實體 contact 存在，但標準接法保持 open / 未使用。
- `NC`：原廠明確定義 Not Connected。
- `Reserved`：原廠明確保留。
- `Optional`：選配／條件使用。
- `Unknown`：實體 Pin 存在，但功能未知。
- `NotApplicable`：確定不適用。

**Unknown != NC，Unused != NC。**

## 8. Complete Physical Pin Rule｜完整實體 Pin 規則

若 `PinCount = N`，必須建立全部 N 個實體 contact 的 Pin row。

不可只保存「有用的 Pin」。

```text
M12 5-contact
├─ Pin 1 Used
├─ Pin 2 Unknown
├─ Pin 3 Used
├─ Pin 4 Used
└─ Pin 5 NC
```

但也不可反過來看到 `M12` 就假設 5 Pin。PinCount 必須來自 exact connector specification。

`ActualPinCount` 可由 Pins sheet 計算；`PinCompleteness` 在 expected = actual 時才是 `Complete`。

Missing Pin row 與「存在但 PinStatus=Unknown」是兩件不同事情。

## 9. TopologyEndpointMode｜拓樸端點顯示模式

允許值：

- `Connector`：整組 mating connector（對插接頭）平常只顯示一個 Port endpoint；底層 Pins 仍完整保存，可在 UI 展開。
- `Pins`：每一個 terminal / loose conductor / independently wired contact 必須直接顯示成可接線 endpoint。

典型規則：

| Interface | TopologyEndpointMode |
|---|---|
| M12 / M8 / RJ45 whole-mated connector | `Connector` |
| Screw terminal / terminal block | `Pins` |
| Flying lead / loose wire / bare wire | `Pins` |
| Fixed multi-core pigtail, 每芯獨立接線 | `Pins` |

例：AL5021 fixed 18-wire field cable 必須顯示 X1.0~X1.15、L-、L+ 全部 18 個 endpoint；不能只存成一個 FIELD_IO Port。

完整互動與 routing 規格見 `TOPOLOGY_ENDPOINT_ROUTING_V2.md`。

## 10. Power Return / 0V 規則

`0V`, `L-`, `V-`, `Supply Return` 等 DC 回路 return pin，Pin `Direction` 統一使用 `Return`，不要有的填 Input、有的 Output、有的 Passive。

這不代表 PortDirection 一律 Return；Port 仍依整組接口真實行為決定 Input / Mixed / Passive 等。

## 11. Layout Archive Rules｜佈局歸檔規則

### 11.1 Basic geometry

Rectangular（矩形）：

- `GeometryType = Rectangular`
- `WidthMm`, `HeightMm`, `DepthMm` 來自原廠 mechanical drawing。

Cylindrical（圓柱）：

- 保存 `DiameterMm`。
- 目前 Topology/Layout bounding box 可用 `WidthMm = DiameterMm`, `DepthMm = DiameterMm`, `HeightMm = OverallLength` 表示。
- 不得把不知道的尺寸補成估計值。

### 11.2 MountingType vs Fastener vs Process Connection

`MountingType` 是安裝類型，例如：

`DIN Rail, Backplate, PanelCutout, Surface, Unknown`

`Fastener` 是固定件，例如 `M3 screw`。

以下屬 Process Connection（製程／管路接口），不得塞進 MountingType：

`G 1/4`, `G 1/2`, `Rp 1`, clamp-on 等。

目前 schema 沒有 `ProcessConnection` 時，先保留在 Description / Notes；不要為了填滿欄位製造錯誤分類。

## 12. Evidence / SourcePage｜證據規則

下列資料若有官方頁碼，應填 `SourcePage`：

- Pin numbering / Pin function
- Terminal numbering
- Connector coding / Gender
- Voltage / current / protocol
- Dimensions / mounting
- Wiring / polarity

圖片只能作 Visual Reference（視覺參考）。產品照不能單獨證明 Pin Function、Voltage、Protocol 或 Connector Coding。

## 13. Ready Gates｜歸檔完成條件

### 13.1 TopologyStatus = Ready

至少必須：

- Component identity 穩定。
- 必要 Port 已建立且 owner 正確。
- PortRole / Direction 不互相混淆。
- `TopologyEndpointMode` 已可決定 Connector vs Pins。
- 已知 `PinCount` 的 Port 需 `ActualPinCount == PinCount`。
- Pins mode 下，所有實際可接線 terminal / conductor 都有獨立 Pin row。
- 不得存在已知錯誤的 pin ownership。

功能仍有 Unknown 可以存在，但如果 Unknown 會讓實際接線對象無法判斷，必須保持 `Review`，不能標 Ready。

### 13.2 LayoutStatus = Ready

至少必須有足以畫真實 bounding geometry（外框）的可靠尺寸；需要 placement validation 的元件還必須有適用的 mounting 資料。

缺尺寸 → `NeedsData` / `Review`，不可拿產品照片目測尺寸。

### 13.3 Wiring / Electrical Drawing

即使 Topology 可顯示，若 pin function、polarity、voltage domain 或 connector mapping 不足以確定實際線路，最終 Wiring / AutoCAD Electrical 仍必須由 downstream validation 阻擋或標 Review。

## 14. 禁止猜測清單

以下資料沒有可靠 evidence 時一律不得猜：

- Pin number / Pin function
- Direction
- Connector coding
- Gender
- Dimensions
- Voltage / current
- Protocol
- Cable pin mapping / core mapping
- Shield / twisted-pair mapping
- PhysicalSide
- NC / Reserved 狀態

特別禁止：

- RJ45 自動等於 Ethernet。
- M12 自動等於 5-pin。
- 兩端同型 connector 就自動假設 1→1、2→2。
- 為了 UI 左右位置把 Mixed / Passive 改成假的 Input / Output。
- 把相近型號或同系列 variant 的資料直接套用到 exact model。

## 15. 新元件歸檔 SOP

1. 確認 Manufacturer + exact Model / Part Number。
2. 找 exact-model 原廠 Datasheet / Manual / Drawing。
3. 建立 Component row 與穩定 ComponentID。
4. 判斷真實 Category、Description、Voltage、Protocol 等。
5. 找出所有 physical/logical Ports。
6. 為每個 Port 建 PortID / PortName / PortRole / Direction。
7. 判斷 Connector family / coding / gender / actual contact count。
8. 建立全部 physical Pins，不省略 NC / Unused / Unknown。
9. 寫 PinRole / Direction / SignalType / Voltage / Function / PinStatus。
10. 決定 `TopologyEndpointMode=Connector` 或 `Pins`。
11. 歸檔 mechanical dimensions / mounting / drawing。
12. 保存官方文件到 `Documents/<Manufacturer>/<Model>/...` 並填 relative path。
13. 驗證 Component → Port → Pin 關聯與 PinCount completeness。
14. 通過 Gate 才標 Ready；缺資料保持 Review / NeedsData。
15. 更新 native Google Sheet 後，重新輸出 / 覆蓋 `Component_Intelligence_Database.xlsx`，避免 Desktop 讀到舊副本。

## 16. Known Reference Cases｜已確認參考案例

### OMRON F03-20

- Passive component。
- Input/Output topology role 可和 `Direction=Passive` 共存。
- 所有實體 terminal 必須存在；Unused terminal 不能省略。

### OMRON K7L-AT50DP

- POWER → `Power Input`。
- SENSING 2/3/4 → `Sensor Input`, `Direction=Mixed`, `TopologyEndpointMode=Pins`，Topology 左側。
- OUTPUT 5/6/7 → functional output group，Topology 右側。

### IFM AL1342

- X01~X08 是 M12 A-coded IO-Link Class A whole-mated connectors → `Connector`。
- `Direction=Mixed` 保持真實，不為畫面位置改成 Output。
- X21~X23 Ethernet whole-mated connector → `Connector`。
- X31 module power connector → `Connector`。

### IFM AL5021

- IO-Link M12 → `Connector`。
- Fixed 18-wire field cable → `Pins`。
- X1.0~X1.15、L-、L+ 必須各自成為可接線 endpoint。

## 17. 文件責任邊界

- 本文件：中央歸檔資料與 GPT 歸檔決策規則。
- `CENTRAL_WORKBOOK_KNOWLEDGE_V1.md`：現有 workbook storage/runtime contract 的歷史基礎文件；若規則衝突，以本 v2 為準。
- `TOPOLOGY_ENDPOINT_ROUTING_V2.md`：Topology endpoint 顯示、Pin-level connection、orthogonal routing（正交走線）與 UI 行為。
- `VENDOR-PART-INTAKE-V1.md`：legacy intake 文件；新的 vendor/custom-part 流程應遵守本 v2。
