# Component Intelligence Remote Build Acceptance Checklist｜最終人工驗收清單

> 這份文件只列出必須在實際 Windows UI 上由人操作／觀看才能確認的項目。Build、unit tests、E2E、PDF smoke、publish 等可自動驗證項目由 GitHub Actions 負責。

## A. 啟動與主頁

- [ ] 解壓最新 `ComponentIntelligence-Desktop-win-x64` Artifact 後可直接啟動。
- [ ] 主電氣工作區只有 **Topology｜電路拓樸** 與 **Layout｜實體佈局** 兩個主要頁籤。
- [ ] 不需要 Notion Token 也能啟動與使用本機功能。
- [ ] CPU 在純啟動、未按 Process / Deep Search 時不應因文件掃描而長時間滿載。

## B. BOM → Topology

1. 匯入一份 BOM。
2. 打開 Topology。

驗收：
- [ ] `Used Quantity` 會建立相對應元件實例。
- [ ] Spare 不會建立 Topology node。
- [ ] Used Quantity 未知時只建立一個 `Qty ?` placeholder。
- [ ] 找不到 Component IR 的元件仍以 placeholder 顯示，不會消失。
- [ ] 再次同步同一份 BOM 不會重複建立實例。

## C. Notion 中央電料庫連線

1. 在 Topology 工具列按 **Notion 中央庫**。
2. 貼上 Notion Internal Integration Token。
3. 按 **測試連線 / Test**。

驗收：
- [ ] 測試只讀取，不新增測試元件。
- [ ] 成功時顯示 Components 中央資料表可讀。
- [ ] Token 清除後程式立即回到 Local-first（本地優先）模式。
- [ ] Token 不出現在 GitHub、Notion page content 或一般 UI 明文區。

## D. 廠商／特製料人工建檔

1. 找一個 BOM 裡已有 Manufacturer + Model/Part Number、但沒有完整 Component IR 的特製料。
2. 雙擊元件 → **人工修正 / Edit**。
3. 建立／修改：
   - Category / Subcategory
   - Connector Family / Coding / Pin Count
   - Product Image URL
   - Ports
   - Pins
   - Specifications
4. 儲存。

驗收：
- [ ] 不需要先成功上網搜尋即可建立最小 Component IR。
- [ ] 人工改動的 Pin / Specification 保存 `UserConfirmed` Evidence（使用者確認證據）。
- [ ] Reference、Topology X/Y、目前專案 Connection 不會因編輯元件知識而重設。

## E. Local → Notion 雙向同步

### 正常同步

1. 在元件補資料視窗按 **同步到 Notion / Sync**。
2. 到 Notion Components / Ports / Pins / Specifications 檢查。

- [ ] 顯示 `Synced` 成功狀態。
- [ ] Components 以 Canonical Key（Manufacturer + Model）更新同一筆，不重複建檔。
- [ ] Port / Pin / Specification 關聯仍正確。

### 從中央庫重新載入

1. 在本機改一個非關鍵值或清除本機測試 DB 後重新開啟。
2. 按 **從 Notion 重新載入 / Reload**。

- [ ] 中央 Component IR 可重新寫回 Local SQLite cache。
- [ ] Topology 再次取得相同元件 Port / Pin / Image URL。

### 離線 Pending Sync

1. 清除 Notion Token 或暫時斷網。
2. 人工修改元件並儲存／同步。

- [ ] 本機資料仍成功保存。
- [ ] 中央同步顯示 `Pending Sync（待同步）`，不是丟失修改。
- [ ] 恢復連線後再次 Sync 可完成同步。

### Verified Conflict 保護

1. 找一個 Notion 中已有 `Verified` Pin 或 Specification 的測試元件。
2. 在本機把同一欄改成不同值。
3. 按 Sync。

- [ ] 本機修改保留。
- [ ] 顯示 `Conflict（衝突）`。
- [ ] Notion 的既有 Verified 值不會被靜默覆蓋。

## F. 圖片 → Topology

1. 在 Component IR / Notion 設定一個可公開讀取的 Product Image URL。
2. 重新開啟／Refresh Topology。

- [ ] 元件 Node 顯示產品圖片。
- [ ] 圖片失敗或離線時只是不顯示圖片，不阻塞 Topology。
- [ ] 首次下載後會使用 LocalAppData image cache。
- [ ] 圖片只作 Visual Reference，不會改變 Pin / Voltage / Protocol 等工程值。

## G. Port / Pin 顯示

1. 在 Select 模式雙擊一個有 Pins 的 Port 圓點。

- [ ] 顯示 Pin Number + Function 清單。
- [ ] 再次雙擊可收合。
- [ ] 展開／收合只影響畫面，不建立任何 Connection。

## H. Port → Port 接線與 Pin Mapping

1. 按 **拉線 / Wire**。
2. 點 A Port，再點 B Port。
3. 雙擊新線路。
4. 選 **編輯 Pin Mapping（腳位映射）**。
5. 測試非直通映射，例如：
   - A Pin 1 → B Pin 3
   - A Pin 3 → B Pin 1
   - A Pin 4 → B Pin 4

驗收：
- [ ] 新 Port Connection 預設沒有任何 Pin Mapping。
- [ ] 系統不會自動假設 `1→1 / 2→2 / 3→3 / 4→4`。
- [ ] 明確 Pin Mapping 會保存到 Cable / Core Assignment。
- [ ] 同一 A Pin 或 B Pin 重複映射會被拒絕。
- [ ] 專案 Save → Close → Load 後 Pin Mapping 仍存在。

## I. Topology Layers / Show Wires

- [ ] Show Wires 可開／關。
- [ ] All / Power / Analog / Digital / Communication / Ground-Shield 都是 View Filter（顯示篩選），不會複製或刪除 graph data。

## J. Layout

- [ ] Topology Placement 與 Physical Placement 分開。
- [ ] Layout 仍能放 Control Cabinet / mounting surface / rail / duct / components。
- [ ] Layout 不會自動推算 Cable Length。

## K. PDF Export

1. 在 Topology 按 **匯出 PDF**。

- [ ] PDF 可輸出目前拓樸。
- [ ] 未知工程資料保持 Unknown / Review，不會因輸出而被自動補成確定值。

## L. Vendor Part Intake GPT Workflow

1. 雙擊特製料。
2. 按 **複製 GPT 特製料歸檔提示詞**。
3. 到新的 GPT 聊天室貼上 Prompt，再傳 PDF / Drawing / Photo。

- [ ] 新 GPT 能在沒有舊聊天室記憶時理解 `component-intelligence-vendor-intake-v1`。
- [ ] 可用 Notion 時直接 upsert 7 張中央資料表。
- [ ] 無 Notion 寫入能力時仍輸出完整 JSON handoff。
- [ ] Confidential / NDA 文件不會被 Prompt 要求自行上傳原檔到 Notion。

---

## 驗收完成定義

人工項目全部通過 + 最新 GitHub Actions：

- Restore ✅
- Build ✅
- Tests ✅
- deterministic MVP demo ✅
- Windows x64 Publish ✅
- Artifact ✅
- no-OpenCV dependency guard ✅
- Real-world PDF Engineering Markdown smoke ✅

才把此 build 視為可進一步 promotion / merge 的候選。PR #2 在此之前維持 Draft。
