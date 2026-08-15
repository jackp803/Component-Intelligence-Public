# Component Intelligence System — Runnable MVP

這個 repository 依 `docs/spec/Component_Intelligence_System_Master_Spec_v0.1.md` 提供一條可執行的最小資料鏈：

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

## 目前可直接 Run 的功能

- Windows WPF GUI（圖形使用者介面）：匯入 BOM、產生範本、開始處理、查看元件詳細資料。
- 產生標準 `BOM` Excel template。
- 匯入 `.xlsx`，保留 raw values、寬鬆驗證、計算 Spare Quantity。
- Resolver 先查本地 SQLite，再查 Source Adapter。
- IFM O5D100 可跑完整離線 MVP 流程。
- 產生 Voltage / Output Type / Connector / Pin-count-based Pin skeleton。
- 不猜 Pin function；未知值保持 `null`。
- 產生 Component IR。
- SQLite 保存與第二次查詢重用。
- Verification / Readiness 最小版。
- CLI 可執行 demo、template、BOM run。

## 環境

- Windows 10 / 11
- .NET 8 SDK

## Build / Test

```powershell
dotnet restore
dotnet build ComponentIntelligence.sln
dotnet test ComponentIntelligence.sln
```

## 開啟桌面 GUI

```powershell
dotnet run --project src/ComponentIntelligence.Desktop/ComponentIntelligence.Desktop.csproj
```

GUI 目前提供：

1. **匯入 BOM**：選取 `.xlsx`。
2. **產生 BOM 範本**：建立規格書要求的 BOM Excel。
3. **開始處理**：逐筆執行 Resolver → Enricher → Normalizer → Verification。
4. **元件詳細資料**：顯示 Voltage、Output、Connector、Pins、Readiness、Evidence 狀態等 MVP 結果。

SQLite 預設存放於目前 Windows 使用者的 Local Application Data，不會寫到 Git repository。

## 直接跑 CLI Demo

```powershell
dotnet run --project src/ComponentIntelligence.Cli/ComponentIntelligence.Cli.csproj -- demo
```

Demo 會處理規格書的：

```text
IFM | O5D100 | 4 | 5 | 主機光電感測器
```

並立即再查一次，用來證明第二次會命中 Local Repository（本地資料庫）。

## 產生 BOM Template

```powershell
dotnet run --project src/ComponentIntelligence.Cli/ComponentIntelligence.Cli.csproj -- template BOM.xlsx
```

## 匯入 BOM 並執行 Pipeline

```powershell
dotnet run --project src/ComponentIntelligence.Cli/ComponentIntelligence.Cli.csproj -- run BOM.xlsx
```

指定 SQLite：

```powershell
dotnet run --project src/ComponentIntelligence.Cli/ComponentIntelligence.Cli.csproj -- run BOM.xlsx --db artifacts/component-intelligence.db
```

## 很重要：目前不是 Production-complete IFM Adapter

為了讓專案在沒有網路、沒有 Playwright、沒有 PDF parser 的環境也能真正 Run，`IfmO5D100SeedSource` 是 **deterministic offline seed adapter（確定性離線種子來源）**。

它只支援 v0.1 acceptance component `IFM O5D100`，工程值保留 `SingleSource`，不標成 `Verified`。Pin function 不會自行猜測。

下一階段應由 Codex 依 `docs/handoff/CODEX_MVP_HANDOFF.md` 將 seed adapter 替換為 Manufacturer Product Page / Datasheet 的真實 extraction pipeline。

## Model Routing Boundary

本專案不固定模型。Task 定義工作；AO Skill B / Skill A / Runtime 決定 capability 與 Actor / Model。
