# Component Intelligence MVP — Codex Handoff

## 1. 目前交付狀態

本 branch 的目標不是宣稱 125 Tasks 全部完成，而是先建立一條 **可以 build / test / run 的 v0.1 vertical slice（垂直切片）**，讓後續 Codex 可以在可執行基線上擴充。

已實作：

```text
Excel BOM
→ Local SQLite IR lookup
→ Resolver
→ IFM O5D100 seed source
→ Enricher
→ Normalizer
→ Verification
→ Component IR
→ SQLite snapshot persistence
→ second-run local reuse
```

操作入口：

- `ComponentIntelligence.Desktop`：Windows WPF GUI，給人工操作。
- `ComponentIntelligence.Cli`：CLI，給 AO / Codex / automated tests 使用。

GUI 目前支援匯入 BOM、產生 Template、執行 pipeline、查看單筆結果與 SQLite 路徑。

## 2. 必須保留的工程規則

- deterministic logic first。
- AI 不得成為 Source of Truth。
- 不得自己創造 Model / MPN / Voltage / Connector / Pin function。
- Raw 與 normalized 概念必須保持分離。
- Evidence 狀態不可無理由升級為 `Verified`。
- Resolver 必須 local-first。
- 不完整資料要顯式呈現，不得用 0 或猜測值填充。

## 3. 已知疑慮 / 技術債

### P0 — IFM 真實來源尚未完成

`IfmO5D100SeedSource` 是離線 MVP fixture，不是 production web adapter。

目前 seed 的 Voltage / Output / Connector / Pin count 來自專案 v0.1 specification example，標記為 `SingleSource/User`，不是 `Verified`。

Codex 下一步應建立真正的 `IfmSource`：

1. official product page identity verification
2. official documents discovery
3. direct datasheet URL
4. HTTP/HTML extraction
5. PDF download + SHA-256
6. PdfPig text extraction
7. field-level Evidence
8. only then upgrade verified fields

### P0 — Pin function 刻意沒有猜

目前只依已知 connector pin count 建立 Pin 1..4 skeleton；`Function` 是 `null`。

在取得 IFM 官方 wiring/pinout Evidence 前，不得填入常見 M12 pin assignment 當成事實。

### P0 — SQLite 尚未完整保存 Raw/Evidence

MVP 的 `SqliteComponentIrRepository` 只保存 normalized Component IR snapshot。

Master Spec 要求的正式 tables：raw specs、normalized specs、sources、documents、ports、pins、assets、resolution/enrichment/verification runs，仍需要整合到正式 Repository。

Repo 內既有 `ComponentRepository` / `SqliteSchema` 是早期 Task 實作，和 MVP snapshot repository 尚未合併。Codex 應統一成單一 repository implementation，避免長期雙軌。

### P1 — Desktop GUI 目前是 MVP code-behind

WPF GUI 刻意先用 code-behind（事件式程式碼）完成可操作版本，尚未導入完整 MVVM、DI、navigation、settings 或 background job architecture。

後續若 GUI 功能變多，再重構成 MVVM；目前不應為架構漂亮而延誤 core pipeline。

### P1 — Component ID

MVP 使用 manufacturer + model 的 SHA-256 前 4 bytes 建立 deterministic ID，例如 `CMP-XXXXXXXX`。

Master Spec 範例是 `CMP-000001`。正式 ID issuance policy 尚未定義；不要在沒有決策前假裝 numeric sequence 是權威規格。

### P1 — Verification / Readiness 還是最小版

目前 completeness 僅針對 v0.1 的四項：Voltage / Output / Connector / Pins。

Confidence 只做粗粒度 `NONE/LOW/MEDIUM`。正式版應依 source authority、field-level conflicts、purpose-specific readiness 重新計算。

### P1 — Datasheet discovery

MVP `datasheet-index` 指向 IFM product documents tab，不是已下載且 hash 過的直接 PDF。

不可把它當成已完成 Datasheet evidence pipeline。

### P1 — Network / Cache / PDF

尚未實作：

- IHttpClientFactory network layer
- retry/backoff / 429 / 5xx
- per-source rate limit
- AngleSharp
- Playwright fallback
- PDF download
- SHA-256 cache
- PdfPig
- Cache Manager / LRU

### P1 — Multi-brand

目前 Source Adapter 只支援 IFM O5D100 acceptance fixture。SICK / OMRON / Siemens 等尚未實作。

### P1 — AO Plan reconciliation

這個 runnable MVP 增加了 Pipeline / CLI / Desktop GUI / MVP repository / IFM seed source 等檔案。現有 AO 125-task authoring plan 是較早的細粒度施工路徑。

Codex 接手前應先做 plan-vs-repository reconciliation：已被 MVP 滿足的 Task 不應重複破壞性施工；未完成 Task 則繼續依 Master Spec 推進。

## 4. 建議 Codex 接手順序

1. 在 CI / 本機執行 `dotnet build` + `dotnet test` + CLI `demo`，並手動開一次 Desktop GUI。
2. 修任何 compile/runtime defect，但不要擴 Scope。
3. 實作 live IFM Source + official Evidence。
4. 統一 `ComponentRepository` 與 `SqliteComponentIrRepository`。
5. 補 Raw/Evidence persistence。
6. 補 PDF/cache/network layer。
7. 提升 Normalizer / Verification。
8. 以 O5D100 做完整 online E2E。
9. 確認第二次完全 local-first，不再必要網路查詢。
10. 再擴品牌。

## 5. MVP 驗收命令

```powershell
dotnet restore
dotnet build ComponentIntelligence.sln
dotnet test ComponentIntelligence.sln
dotnet run --project src/ComponentIntelligence.Cli/ComponentIntelligence.Cli.csproj -- demo --db artifacts/mvp.db
dotnet run --project src/ComponentIntelligence.Desktop/ComponentIntelligence.Desktop.csproj
```

預期 demo：

- first run `localRepositoryHit=false`
- Component IR 有 10..30 V DC / PNP / M12 / 4 pins
- pin functions remain null
- verification status `SingleSource`
- second run `localRepositoryHit=true`

## 6. 不要誤判為已完成

以下仍不得宣稱 production complete：

- official IFM datasheet extraction
- field-level manufacturer verification
- exact pin functions
- full normalized spec dictionary
- full SQLite schema persistence
- full cache/PDF/network pipeline
- production-grade GUI architecture
- 125 AO Tasks completion

這些都是後續 Codex 的明確工作項。
