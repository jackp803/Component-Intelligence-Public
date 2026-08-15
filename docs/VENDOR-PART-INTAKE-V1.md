# Vendor Part Intake v1｜廠商／特製料標準歸檔流程

Contract Version: `component-intelligence-vendor-intake-v1`

## 目的

把廠商特製料、客製線材、轉接頭、治具內部零件或沒有公開產品頁的特殊電料，使用同一個工程資料契約整理後寫入 Component Intelligence 中央 Notion 電料知識庫，之後可被 BOM、Topology（拓樸）與 Wiring（接線）重複使用。

## 使用者最少要準備什麼

能先「歸檔」的最低門檻：

1. Manufacturer / Vendor（製造商／供應商）名稱。
2. 一個穩定且唯一的 Model / Vendor PN / Drawing No. / Assembly No.（型號／廠商料號／圖號／組件號）。
3. 至少一份可追溯 Evidence（證據）：PDF、正式圖面、規格文件、廠商郵件截圖、銘牌／標籤照片或正式網址。

如果沒有穩定的 Manufacturer + Model/Part Number，只能整理成 `NEEDS_IDENTITY`，不得創造假的正式元件身份。

## 如果希望直接 Topology Ready｜拓樸就緒

盡量再提供：

- Connector / Terminal（接頭／端子）類型、數量、Gender（公母）、Coding（編碼）。
- Port（接口）名稱、用途、Direction（方向）。
- Pin number + Pin function + Signal type + Voltage domain（腳位號碼／功能／訊號型態／電壓域）。
- Protocol（協定），如果該元件有通訊。
- Voltage / Current（工作電壓／電流需求）。
- Cable / Adapter（線材／轉接頭）要有 Side A、Side B、Pin Mapping、Core Mapping、Shield / Twisted Pair（屏蔽／雙絞）等資料。
- Product Image（產品圖片），只作拓樸顯示用途。
- Datasheet / Wiring Diagram / Pinout / Mechanical Drawing（規格書／接線圖／腳位圖／機構圖）。

## 在 Component Intelligence 裡怎麼做

1. BOM 匯入後打開 Topology。
2. 雙擊特製料元件，進入 `Complete Component Data（補元件資料）`。
3. 按 `複製 GPT 特製料歸檔提示詞`。
4. 到任意新的 GPT 聊天室貼上整段 Prompt。
5. 同一個聊天室再附上廠商 PDF、照片、圖面、BOM 列、郵件截圖或規格說明。
6. 如果該 GPT 可使用你的 Notion，它應搜尋 `🗄️ Component Intelligence｜中央電料知識庫` 並依既有資料表 upsert。
7. 如果該 GPT 無法寫 Notion，它仍必須回傳 `component-intelligence-vendor-intake-v1` JSON；不可因為沒有 Notion 權限而停止整理。

## 中央 Notion 資料責任

- `Components`：唯一元件主身份與摘要；Canonical Key 不重複。
- `Documents`：PDF、Manual、Drawing、Vendor Evidence；機密原檔不得未經允許上傳。
- `Ports`：每個 Port 一筆。
- `Pins`：每個 Pin 一筆，並保留證據。
- `Specifications`：工程規格與證據。
- `Projects` / `BOM Items`：只有在明確屬於某個 BOM 專案時才建立關聯。

Canonical Key：

```text
UPPER(TRIM(Manufacturer)) + "::" + UPPER(TRIM(Model / Part Number))
```

## 禁止猜測

- Unknown 保持 Unknown。
- RJ45 不自動等於 Ethernet。
- Connector 不等於 Protocol。
- Port 不等於 Pin。
- 多 Port 元件沒有 Pin ownership（腳位歸屬）證據時，保持未歸屬並標記 `NEEDS_PORT_MAPPING`。
- 產品照片不可單獨用來證明 Pin Function、Voltage、Protocol、Connector Coding。
- 特製線材不可因為兩端接頭相同就假設 `1→1 / 2→2 / ...`；必須有 Pin Mapping 證據。

## Confidential / NDA｜機密文件規則

如果廠商文件、公司圖面或郵件標示 `Confidential`、`NDA`、`Internal`，或使用者明確說明屬於機密資料：

- 不得自行把原始 PDF、圖面、圖片上傳到 Notion。
- Notion 可保存經允許的結構化工程資料、`file_name`、`SHA256`、`page_number`、Evidence 摘要與核准的 URL。
- 原始敏感檔案留在使用者允許的本機／公司儲存位置。
- 公開原廠 Datasheet 才適合直接保存公開 URL 或附件。

## GPT 驗收輸出

另一個 GPT 完成後必須回報：

- Created（新增）
- Updated（更新）
- Needs Review（需人工確認）
- Unknown（缺資料）
- Conflict（來源衝突）
- 缺少哪些資料才能 Topology Ready

並在最後輸出合法的 `component-intelligence-vendor-intake-v1` JSON。這份 JSON 是跨聊天室、跨工具的可攜式交接格式。

## 圖片規則

圖片是 Visual Reference（視覺表示），不是工程真值。中央庫保存 Image URL / 來源；桌面程式可建立本機縮圖快取供 Topology 顯示。Pin / Port / Connector / Voltage / Protocol 等工程結論仍須來自可追溯文件 Evidence。
