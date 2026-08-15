# Notion Central Knowledge｜Notion 中央電料知識庫

## Purpose｜目的

Component Intelligence uses Notion as an optional cross-project engineering knowledge store while keeping the desktop application fully usable offline.

```text
BOM
 ↓
Local SQLite Cache（本機快取）
 ↓ miss
Notion Central Knowledge（中央電料知識庫）
 ↓ miss / incomplete
Manufacturer + trusted PDF enrichment（原廠＋可信 PDF 補全）
 ↓
Component IR
 ↓
Local SQLite + Notion mirror
 ↓
Topology / Layout
```

Notion is **not** the runtime database for Topology X/Y, Layout placement, wire drawing state, Undo/Redo, or other high-frequency UI state.

## Data model｜資料模型

The user's Notion workspace currently contains seven related data sources:

| Data source | Purpose | ID |
|---|---|---|
| Components｜元件主檔 | Cross-project component identity and summary | `968e2dad-0581-49b9-831f-f22fed36e145` |
| Documents｜文件與證據 | Datasheet/manual/product evidence | `57030154-c404-4916-9848-f9580f576ac2` |
| Ports｜元件接口 | Structured ports | `f73bc721-1459-4dab-967f-a79d35775d00` |
| Pins｜元件腳位 | Structured pins + evidence provenance | `95ca405a-0144-4f50-a511-c80a7d4bcc6b` |
| Specifications｜工程規格 | Field-level engineering facts + evidence | `9d4d02cc-372c-4115-84d6-e335d78b7fea` |
| Projects｜BOM 專案 | BOM project index | `67211e0a-c968-4cae-9bdb-25fb6ee4049d` |
| BOM Items｜專案料件 | Project/component quantities and snapshots | `079ccff2-92d3-4250-a5d3-2cfac72bc79c` |

### Identity rule｜身分規則

```text
Canonical Key = UPPER(TRIM(Manufacturer)) + "::" + UPPER(TRIM(Model / Part Number))
```

Description, usage notes, fuzzy product family names, or inferred aliases must never replace the canonical identity key.

## Verification rules｜驗證規則

- `Verified`: supported by sufficiently strong explicit engineering evidence.
- `UserConfirmed`: explicitly confirmed by the user.
- `SingleSource`: supported by one source only.
- `Inferred`: derived/inferred; never silently promoted to Verified.
- `Unknown`: not known from reliable evidence.
- `Conflict`: sources disagree and require review.

`RJ45` remains a Connector（接頭）, not an automatic Ethernet protocol assignment. Port（接口）and Pin（腳位）remain separate. Unknown values remain Unknown.

## Local application configuration｜本機程式設定

The Notion connection is optional. Without a token, the application behaves exactly as a local-first application and does not make Notion requests.

Create a Notion internal integration/connection with read + update content access, share the Central Knowledge databases with that connection, then set:

```powershell
$env:COMPONENT_INTELLIGENCE_NOTION_TOKEN = "<your Notion integration token>"
```

Do **not** commit the token to GitHub, source code, appsettings, screenshots, or Notion itself.

Optional data-source overrides:

```powershell
$env:COMPONENT_INTELLIGENCE_NOTION_COMPONENTS_DS = "..."
$env:COMPONENT_INTELLIGENCE_NOTION_DOCUMENTS_DS = "..."
$env:COMPONENT_INTELLIGENCE_NOTION_PORTS_DS = "..."
$env:COMPONENT_INTELLIGENCE_NOTION_PINS_DS = "..."
$env:COMPONENT_INTELLIGENCE_NOTION_SPECIFICATIONS_DS = "..."
```

The code uses Notion API version `2026-03-11`.

## Runtime behavior｜執行行為

1. Query the local SQLite Component IR repository.
2. If local cache misses and Deep Search is not requested, query Notion by Canonical Key.
3. A Notion hit is cached locally and evaluated by the existing Topology Knowledge Policy.
4. Only topology-ready knowledge may stop before online enrichment.
5. Incomplete Notion knowledge continues through the normal manufacturer/PDF enrichment pipeline.
6. Successful enriched Component IR is saved locally and mirrored to Notion Components / Ports / Pins / Specifications / Documents.
7. Notion failures are diagnostics, not a reason to destroy or downgrade valid local engineering knowledge.

## GPT-assisted workflow｜GPT 協作流程

A BOM may be given directly to GPT. GPT can normalize it to the same Canonical Key contract and write new/updated entries to the Notion knowledge base. The desktop application then consumes the same central knowledge through the optional Notion adapter.

This does not require OpenAI API calls inside Component Intelligence. GPT/Notion interaction and the desktop application's local engineering runtime remain separate concerns.
