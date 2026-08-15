namespace ComponentIntelligence.Knowledge;

/// <summary>
/// Stable, copyable handoff contract for asking another GPT chat to normalize a vendor/custom part
/// and archive it into the Component Intelligence central Notion knowledge base.
/// This prompt is intentionally self-contained so the receiving chat does not need project memory.
/// </summary>
public static class VendorPartIntakePrompt
{
    public const string ContractVersion = "component-intelligence-vendor-intake-v1";

    public static string Build(string? manufacturer = null, string? model = null, string? componentDefinitionId = null)
    {
        var knownManufacturer = string.IsNullOrWhiteSpace(manufacturer) ? "<UNKNOWN - ask me or derive only from explicit evidence>" : manufacturer.Trim();
        var knownModel = string.IsNullOrWhiteSpace(model) ? "<UNKNOWN - ask me or derive only from explicit evidence>" : model.Trim();
        var knownComponentId = string.IsNullOrWhiteSpace(componentDefinitionId) ? "<not assigned>" : componentDefinitionId.Trim();

        return $$"""
你正在處理 Component Intelligence 的 Vendor Part Intake（廠商／特製料歸檔）。
Contract Version: {{ContractVersion}}

目前已知身份：
- Manufacturer / Vendor（製造商／供應商）：{{knownManufacturer}}
- Model / Part Number（型號／料號）：{{knownModel}}
- Existing Component Definition ID（既有元件定義 ID）：{{knownComponentId}}

你的任務：
1. 讀取我在這個聊天室提供的所有檔案、圖片、PDF、圖面、規格、BOM 列、網址與文字說明。
2. 只根據可追溯 Evidence（證據）建立 Component Intelligence 標準資料；不得用常識補成工程事實。
3. 如果已連接 Notion，先搜尋頁面「🗄️ Component Intelligence｜中央電料知識庫」，依既有 7 張資料表 Components / Documents / Ports / Pins / Specifications / Projects / BOM Items 進行 upsert（新增或更新）。
4. 如果無法直接寫 Notion，仍必須輸出下方 JSON，讓使用者可交給另一個有 Notion 權限的 GPT，並作為跨聊天室的可攜式交接紀錄。
5. 完成後回報：Created / Updated / Needs Review / Unknown / Conflict，以及缺少哪些資料才能 Topology Ready（拓樸就緒）。

【身分規則】
- Canonical Key = UPPER(TRIM(Manufacturer)) + "::" + UPPER(TRIM(Model / Part Number))。
- 不得用 Description、照片外觀或模糊型號當主鍵。
- 廠商特製品若只有 Drawing No. / Assembly No. / Vendor Custom PN，且它是穩定唯一識別，可把該識別作為 Model / Part Number，並在 notes 說明來源。
- 如果沒有穩定 Manufacturer + Model/Part Number，標記 NEEDS_IDENTITY；可以整理證據，但不要建立假的正式元件身份。

【Category / Material Role（分類／電料角色）】
- component.category 必須優先使用以下標準角色之一：Sensor、PLC、IO Module、Hub、PCB、Power Supply、Relay、Terminal Block、Connector、Coupler、Cable、Cable Assembly、Wire、Other、Unknown。
- Subcategory 可再描述更細的類型，但不得用 Subcategory 取代上述主要 Category。
- Cable（散裝電纜／線材）、Cable Assembly（成品線組／預製線）與 Wire（單芯線／導線）是 Connection Material（連線材料），不是一般 Device Node（設備節點）。
- 公司自製／內部 Layout 的電路板使用 PCB；只需要建立有證據的外部 Port / Pin / Connector，沒有內部原理圖時不得猜板內 Net 或 IC 關係。
- 不可只因 Model 名稱含有 cable、wire 等字樣就分類；必須有文件、圖面、產品描述或使用者明確說明支持。
- 無法確認角色時 category = Unknown，讓軟體保留可見待審核狀態，不得為了讓流程通過而猜分類。

【驗證狀態】
- Verified：原廠／廠商圖面、正式 Datasheet、正式 Manual、正式規格或同等明確證據直接支持。
- SingleSource：只有一份可追溯來源支持，尚未交叉驗證。
- Inferred：只能推論；不可升級成 Verified。
- Unknown：來源沒有提供。
- Conflict：來源互相衝突；全部保留並列出衝突，不自行選邊。

【重要工程規則】
- Unknown 必須保持 Unknown，不可猜。
- Connector（接頭）不等於 Protocol（協定）；例如 RJ45 不自動代表 Ethernet。
- Port（接口）不等於 Pin（腳位）。一個 Port 可包含多個 Pin，一個元件可有多個 Port。
- 多 Port 元件若文件沒有證明 Pin 屬於哪個 Port，pin.port_id = null，加入 open_items: NEEDS_PORT_MAPPING。
- 圖片只能作 Visual Reference（視覺參考）；不得僅從照片外觀推定 Pin Function、Voltage、Protocol 或 Connector Coding。
- Pin Function / Connector / Voltage / Protocol 等工程欄位必須附 Evidence。
- 特製線材／轉接頭要保存端 A、端 B、Connector、Pin Mapping、線芯／顏色（若有證據），不可假設 straight-through（直通）。
- 若資料只足夠歸檔但不足以接線，仍建立元件並將 readiness.topology = NeedsData。
- 若文件標示 Confidential / NDA / Internal，或使用者說明為公司／廠商機密，不得自行把原始 PDF、圖面或圖片上傳到 Notion。Notion 只保存允許的結構化工程資料、file_name、SHA256、page_number、Evidence 摘要與核准的 URL；原始敏感檔案留在使用者允許的位置。

【我最好提供的資料】
最低可歸檔：
- 廠商／製造商名稱
- 穩定的 Vendor PN / Model / Drawing No. / Assembly No. 至少一個
- 至少一份可追溯證據：PDF、圖面、規格文件、廠商郵件截圖、產品標籤照片或正式網址

要達到 Topology Ready，盡量再提供：
- Connector / Terminal 類型、數量、性別、Coding
- Port 名稱／用途／Direction
- Pin number + Pin function + Signal type + Voltage domain
- Protocol（若有）
- 電源需求：Voltage / Current
- 若為 Cable / Adapter：端 A、端 B、每芯／每 Pin Mapping、Shield / Twisted Pair 等
- 元件正面照片或原廠產品圖（只作顯示用途）
- Datasheet / Wiring Diagram / Pinout / Mechanical Drawing

【固定輸出 JSON】
最後一定輸出一個合法 JSON code block，不要省略 Unknown 欄位，不要在 JSON 裡加註解：
{
  "schema_version": "component-intelligence-vendor-intake-v1",
  "component": {
    "manufacturer": null,
    "model": null,
    "mpn": null,
    "canonical_key": null,
    "description": null,
    "category": "Sensor|PLC|IO Module|Hub|PCB|Power Supply|Relay|Terminal Block|Connector|Coupler|Cable|Cable Assembly|Wire|Other|Unknown",
    "subcategory": null,
    "vendor_custom_part": true,
    "identity_status": "Verified|SingleSource|NeedsIdentity|Conflict",
    "notes": null
  },
  "assets": {
    "image_url": null,
    "product_page_url": null,
    "datasheet_url": null,
    "cad_url": null
  },
  "documents": [
    {
      "document_type": "Datasheet|Manual|Drawing|WiringDiagram|Pinout|Photo|VendorEmail|Other",
      "title": null,
      "source_url": null,
      "file_name": null,
      "sha256": null,
      "verification_status": "Verified|SingleSource|Inferred|Unknown|Conflict"
    }
  ],
  "ports": [
    {
      "port_id": null,
      "name": null,
      "port_type": null,
      "direction": null,
      "signal_type": null,
      "protocol": null,
      "voltage_domain": null,
      "connector_family": null,
      "connector_coding": null,
      "connector_gender": null,
      "pin_count": null,
      "evidence_refs": []
    }
  ],
  "pins": [
    {
      "port_id": null,
      "pin_number": null,
      "function": null,
      "signal_type": null,
      "direction": null,
      "voltage_domain": null,
      "description": null,
      "evidence": [
        {
          "source_type": null,
          "source_url": null,
          "document_sha256": null,
          "page_number": null,
          "raw_value": null,
          "verification_status": "Verified|SingleSource|Inferred|Unknown|Conflict"
        }
      ]
    }
  ],
  "specifications": [
    {
      "key": null,
      "name": null,
      "section": null,
      "value": null,
      "verification_status": "Verified|SingleSource|Inferred|Unknown|Conflict",
      "evidence": []
    }
  ],
  "cable_or_adapter_mapping": {
    "applicable": false,
    "side_a_port": null,
    "side_b_port": null,
    "pin_mapping": [],
    "core_mapping": [],
    "shielding": null,
    "twisted_pairs": []
  },
  "readiness": {
    "archive": "Ready|NeedsData|Conflict",
    "topology": "Ready|Partial|NeedsData|Conflict",
    "wiring": "Ready|Partial|NeedsData|Conflict",
    "drawing": "Ready|Partial|NeedsData|Conflict"
  },
  "open_items": []
}

【Notion 寫入原則】
- Components：只放元件主身份與摘要；同 Canonical Key 不重複建立。Category 必須遵守上述標準角色；Cable / Cable Assembly / Wire 不得偽裝成 Sensor 或一般 Device。
- Documents：每份正式證據各一筆並 Relation 回 Component；機密原檔不得未經允許上傳。
- Ports：每個實體／邏輯 Port 各一筆並 Relation 回 Component。
- Pins：每個 Pin 各一筆；保留 Source URL / PDF SHA256 / Page 等 Evidence。
- Specifications：每個工程規格各一筆；衝突值不得互相覆蓋成單一 Verified 值。
- Projects / BOM Items：只有我明確說這顆料屬於某個 BOM 專案時才建立／更新，不要自動虛構專案。

現在請先檢查我提供的資料是否足以確認 Manufacturer + Model/Part Number；如果足夠就直接處理，不要因為缺次要欄位而停住。缺的工程資料保留 Unknown，最後列在 open_items。
""";
    }
}
