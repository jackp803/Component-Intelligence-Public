# Component Intelligence AO Task Plan v1

這個資料夾把 Component Intelligence System（元件智慧資料系統）轉成正式的
**Plan Layer（計畫層） + Task Layer（任務層）**。

## 主要檔案

- `plan.yaml`：整個 Plan 的共用政策與 `task_sources`。
- `tasks/phase-*.yaml`：15 個 Phase，共 125 個最小 Task。
- `docs/spec/Component_Intelligence_System_Master_Spec_v0.1.md`：完整 Source of Truth（真相來源）。
- `docs/spec/Component_Intelligence_System_Worker_Task_Spec_v0.2.md`：Model-Agnostic（模型無關）T001～T125 施工規格。
- `schemas/task-source.schema.json`：Task YAML Schema（結構規範）。
- `validation/profiles.yaml`：Build / Test deterministic validation（確定性驗證）。
- `tools/validate_plan.py`：不呼叫 LLM 即可檢查 Plan。

## 本版政策

```yaml
execution_mode: sequential
max_workers: 1
stop_on_final_failure: true
stop_on_human_gate: true
require_artifact_dependencies: true
disallow_model_pinning: true
```

Task **沒有 `model`、`primary_actor`、`fallback_actor`**。
「誰來做」由 Skill A / Runtime 決定；Task 只描述「要做什麼」。

## Worker 看到的內容

Runner 不應把 125 個 Task 都放進 Worker Prompt（工作模型提示）。

只放：

```text
Current Task（目前任務）
+ Required Artifacts（必要產物）
+ Relevant Source of Truth（相關權威規格）
+ Necessary Source Files（必要原始碼）
+ Acceptance（驗收）
+ Validation（驗證）
+ Writable Scope（可修改範圍）
```

因此 Plan 有 125 個 Task 不等於每次 LLM 要讀 125 個 Task。

## 驗證 Plan

在本資料夾執行：

```bash
python tools/validate_plan.py
```

預期：

```text
PLAN VALID: 125 tasks, 125 artifacts
TASK SOURCES: 15
MODEL PINNING: none
```

Validator 會檢查：

- Task ID 是否唯一
- Dependency 是否存在
- Artifact Dependency 是否有正確 Provider（提供者）
- DAG 是否有 Cycle（循環）
- 必填欄位
- Acceptance 是否存在
- Validation Profile 是否存在
- Task 是否偷塞固定 Model 欄位

## 為什麼目前依賴較保守？

本版目標是讓單一 Local Worker（本地工作模型）安全施工，所以採：

```text
max_workers = 1
```

並使用保守的前置 Artifact Dependency（產物依賴）。

未來若 AO Runtime 已經穩定，可只調整 Dependency Graph（依賴圖）與 Plan Policy，
把互不依賴的 Task 釋放成可平行 READY；不需要重寫每個 Task 的 objective / acceptance。

## Task 結束規則

每個 Task：

```text
Build PASS
+
Deterministic Tests PASS
↓
Publish Task Artifact
↓
DONE
↓
STOP
```

模型不得自行開始下一題。
