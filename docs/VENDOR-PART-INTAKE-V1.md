# Vendor Part Intake v1｜廠商／特製料歸檔流程（Legacy filename）

> 本檔名保留相容性，但原本的 Notion-only 流程已退役。所有新歸檔必須遵守 [`COMPONENT_ARCHIVE_SPEC_V2.md`](COMPONENT_ARCHIVE_SPEC_V2.md)。中央資料寫入目標為 Google Drive 的 `Component_Intelligence_Database` / `Component_Intelligence_Database.xlsx` 與 `Documents/`。

Contract Version: `component-intelligence-vendor-intake-v1`（portable handoff JSON 仍可保留）

## 目的

把廠商特製料、客製線材、轉接頭、治具內部零件或沒有公開產品頁的特殊電料，使用同一個工程資料契約整理後寫入 Component Intelligence 中央 Google Drive archive，之後可被 BOM、Topology（拓樸）、Layout（佈局）與 Wiring（接線）重複使用。

## 使用者最少要準備什麼

最低可建立正式 identity（身份）的門檻：

1. Manufacturer / Vendor（製造商／供應商）。
2. 穩定且唯一的 Model / Vendor PN / Drawing No. / Assembly No.。
3. 至少一份可追溯 Evidence（證據）：PDF、正式圖面、規格文件、廠商郵件截圖、銘牌／標籤照片或正式網址。

若沒有穩定的 Manufacturer + Model/Part Number，只能標記 identity 未完成，不得創造假的官方型號。

## 中央資料責任

中央核心只有三張工程資料表：

- `Components`：元件 identity、分類、主要規格、Layout 尺寸、文件相對路徑與 readiness。
- `Ports`：每個 physical/logical Port 一筆，包含 PortRole、Direction、Connector 與 `TopologyEndpointMode`。
- `Pins`：每個 physical contact / terminal / conductor 一筆，保留 Pin number、Function、Direction、PinStatus 與 evidence。

原始/公開文件放在：

```text
Documents/<Manufacturer>/<Model>/...
```

Workbook/Google Sheet 不建立另一套 Notion `Documents / Specifications / Projects / BOM Items` schema 作為正式來源。

## Topology Ready 前應盡量提供

- Connector / Terminal 類型、數量、Gender、Coding。
- PortName、PortRole、真實 electrical Direction。
- 實體 PinCount 與全部 Pin rows。
- Pin number + Pin function + Signal type + Voltage domain。
- Protocol（若有）。
- Cable / Adapter 的 Side A、Side B、Pin Mapping、Core Mapping、Shield / Twisted Pair 證據。
- Datasheet / Wiring Diagram / Pinout / Mechanical Drawing。

若是 whole-mated M12 / RJ45 等 connector，`TopologyEndpointMode=Connector`；若是 terminal、flying lead、loose wire、固定多芯散線，`TopologyEndpointMode=Pins`。

## 標準 GPT 歸檔流程

1. 先讀 [`COMPONENT_ARCHIVE_SPEC_V2.md`](COMPONENT_ARCHIVE_SPEC_V2.md)。
2. 確認 exact Manufacturer + Model / Part Number。
3. 檢查中央 Components 是否已有同一 identity，避免重複建立。
4. 依來源優先級整理 Components / Ports / Pins。
5. 所有已知 physical Pin 必須完整建立；`Unknown`, `Unused`, `NC` 不可混用。
6. `PortRole` 與 `Direction` 分開保存，不為了 Topology 左右位置造假 Direction。
7. 將允許保存的 evidence 放到 `Documents/<Manufacturer>/<Model>/`，Workbook 只記 relative path。
8. 執行 Component→Port→Pin ownership 與 `PinCount == ActualPinCount` 驗證。
9. 資料不足時保持 `Review` / `NeedsData`；不得為了完成率猜測。
10. 更新 native Google Sheet 後，重新輸出／覆蓋 `Component_Intelligence_Database.xlsx`，確保 Desktop 讀到最新資料。

## 禁止猜測

- Unknown 保持 Unknown。
- RJ45 不自動等於 Ethernet。
- M12 不自動等於 5-pin。
- Connector 不等於 Protocol。
- Port 不等於 Pin。
- 多 Port 元件沒有 Pin ownership 證據時，不得把 Pin 隨意塞到最像的 Port。
- 產品照片不可單獨證明 Pin Function、Voltage、Protocol、Connector Coding。
- 特製線材不可因兩端接頭相同就假設 `1→1 / 2→2 / ...`。
- 不得把同系列不同 variant 的規格套到 exact model。
- 不得為了 UI screen side 把 Mixed / Passive 改成假的 Input / Output。

## Confidential / NDA｜機密文件規則

如果廠商文件、公司圖面或郵件標示 `Confidential`、`NDA`、`Internal`，或使用者明確說明屬於機密資料：

- 不得自行上傳到未授權的外部公開位置。
- 只存使用者/公司允許的 Google Drive 位置或本機/company storage。
- 中央表可保存經允許的結構化工程資料、file name、page number、hash/evidence 摘要。
- 公開原廠 Datasheet 才適合保存公開 URL。

## GPT 驗收輸出

完成後至少回報：

- Created（新增）
- Updated（更新）
- Needs Review（需人工確認）
- Unknown（缺資料）
- Conflict（來源衝突）
- 缺少哪些資料才能 Topology / Layout / Wiring Ready

若工作流仍需要 `component-intelligence-vendor-intake-v1` JSON，可繼續輸出作跨聊天室 handoff；但中央真值以 Components / Ports / Pins 與 Documents archive 為準。

## 圖片規則

圖片是 Visual Reference（視覺表示），不是工程真值。Pin / Port / Connector / Voltage / Protocol 等工程結論必須來自可追溯 evidence。
