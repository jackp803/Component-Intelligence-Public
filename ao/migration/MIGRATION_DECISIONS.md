# AO v0.8.1 Native Plan Migration Decisions
## Component Intelligence System（元件智慧資料系統）

本資料夾將 v0.2 的 authoring plan（撰寫格式）確定性編譯為 AO v0.8.1 native plan（原生格式）。

## 權威檔案

- 人／GPT 維護：`ao/authoring/tasks/phase-*.yaml`
- Output Authority：`ao/migration/output-authority.v081.yaml`
- Compiler：`ao/tools/build_v081_plan.py`
- AO Runtime 使用：`ao/tasks.v081.yaml`
- 相容性入口副本：`ao/plan.yaml`

不得人工分別維護 `tasks.v081.yaml` 與 `plan.yaml`；兩者都由 compiler 生成。

## Migration Policy

1. 125 Tasks 全部 materialize 到 root `tasks`。
2. `task_sources` / `includes` 不輸出。
3. `version` integer → string。
4. `inputs.references` → `inputs: [string]`。
5. `inputs.artifacts` 不輸出；Artifact Dependency 由 `dependencies[].required_artifacts` 保留。
6. `implementation` 的全部非 excerpt 結構化內容與原始 `spec_excerpt` 都遷入 `objective`。
7. `acceptance.id/type/statement` → `[ID]` + `category/description`。
8. `stop_condition` → `[STOP]` deterministic acceptance。
9. Phase 最後一個 Task 增加 `[PHASE-GATE]` acceptance。
10. `execution_scope` 只保留 `allowed_roots/protected`。
11. 原 file-count budget 轉成 `[SCOPE-BUDGET]` acceptance，保留語意但不假裝 AO 有原生欄位。
12. `side_effects.level` → `side_effect`。
13. `priority high/normal/low` → `100/50/10`。
14. Validation 固定使用 AO v0.8.1 allowlist：
    - `dotnet_build`, target `ComponentIntelligence.sln`
    - `dotnet_test`, target `ComponentIntelligence.sln`
15. 每 Task 都必須有明確 `outputs.files`。
16. 每 Task 的 `Txxx-result` artifact 都必須有 `path`，由 `output-authority.v081.yaml` 定義。
17. 不輸出 `model` / `primary_actor` / `fallback_actor` / `fallback`。
18. 不主動加入 `routing_hints`；Actor/Model Selection 仍由 AO Skill B / Skill A / Runtime 決定。

## Output Authority 原則

每個 implementation Task 最多授權：
- 其主要 production file(s)
- 一個 task-specific deterministic test file

Test-only / E2E Task 只授權其對應 test file。

若日後真實實作需要跨出這些檔案，不應讓 Worker 自行擴 Scope；應修改 authoring/output authority 後重新編譯 Plan。

## 正式驗證順序

```text
python ao/tools/build_v081_plan.py
python ao/tools/validate_migration.py
python ao/tools/validate_with_installed_ao.py
agent-orchestrator plan validate ao/tasks.v081.yaml
agent-orchestrator plan run ao/tasks.v081.yaml --repo . --dry-run
```

若 `agent-orchestrator` 不在 PATH，最後兩步應由已安裝 AO Skill / bridge 使用其正式等價入口執行，不要自行猜一個替代 CLI。
