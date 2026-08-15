Audit 完成。結論是：目前 Plan 與 AO v0.8.1 不相容；125 個 Task 可以遷移成原生格式，但不是單純刪欄位即可完成，仍需處理 validation target、output file authority，以及沒有一對一欄位的 implementation／stop-condition 語意。

本次只讀取：

- AO 正式 [task\_plan.schema.json]\(C:/Users/jackp/AppData/Roaming/Python/Python312/site-packages/agent\_orchestrator/config/task\_plan.schema.json)
- AO [plan\_loader.py]\(C:/Users/jackp/AppData/Roaming/Python/Python312/site-packages/agent\_orchestrator/task\_execution/plan\_loader.py)
- AO runtime、dependency resolver、validation implementation
- Downstream [ao/plan.yaml]\(C:/Users/jackp/Documents/ChatGPT/Bom自動探索-電路拓譜-Autocad/Component\_Intelligence\_Starter\_Project\_v0.2/Component\_Intelligence\_Starter\_Project\_v0.2/ao/plan.yaml)
- 15 個 `ao/tasks/*.yaml`
- `ao/validation/profiles.yaml`

沒有執行 dry-run、Task 或任何修改。

## AO v0.8.1 正式 Task Plan 結構

根層必要欄位：

```
schema_version: "1.0"
plan:
  id: ...
  project_id: ...
  name: ...
policy:
  execution_mode: sequential
  max_workers: 1
tasks:
  - ...
```

根層允許但目前必須為空的欄位：

```
task_sources: []
includes: []
```

Task 必要欄位：

```
id
version
title
objective
acceptance
validation
side_effect
outputs
```

Task 可選欄位：

```
non_goals
dependencies
inputs
source_of_truth
retry
priority
execution_scope
routing_hints
baseline_revision
```

## 全量 Schema incompatibility

目前 root Plan 有 5 組完整錯誤：

1. `<root>.validation_profiles_source`
   - 不允許的額外欄位。
2. `<root>.tasks`
   - 必要欄位缺失。
3. `plan.version`
   - `plan` 不允許此欄位。
4. `plan.source_of_truth`
   - `plan` 不允許此欄位。
5. `policy` 額外欄位
   - `require_artifact_dependencies`
   - `disallow_model_pinning`
   - `worker_context_mode`
   - `actor_selection`
   - `task_defines_work_not_actor`
6. `task_sources`
   - 實際有 15 個項目。
   - Schema 規定 `maxItems: 0`，所以必須為空。

對抽出的 125 個 Task 逐一套用正式 `$defs.task`，共有 1,040 個 schema violations：

| 問題影響數量                                                |           |
| ----------------------------------------------------- | --------- |
| `version` 是 integer，正式格式要求 string                     | 125       |
| `inputs` 是 object，正式格式要求 string array                 | 125       |
| acceptance item 形狀不合法                                 | 290       |
| `execution_scope` 有額外欄位                               | 125       |
| `priority` 是字串，正式格式要求 integer                         | 125       |
| `implementation`、`side_effects`、`stop_condition` 不被允許 | 125 Tasks |
| 缺少必要的 `side_effect`                                   | 125       |

## Current Plan → AO Schema mapping

| Current 欄位AO v0.8.1判斷                     |                                |                                              |
| ----------------------------------------- | ------------------------------ | -------------------------------------------- |
| `schema_version: '1.0'`                   | `schema_version: "1.0"`        | 直接相容                                         |
| `plan.id/project_id/name`                 | 同名欄位                           | 直接相容                                         |
| `plan.version`                            | 無對應欄位                          | 必須移除或另行保留 metadata                           |
| `plan.source_of_truth`                    | 只能存在於 Task                     | 根層必須移除；125 個 Task 已各自保留相同值                   |
| 核心 `policy` 四欄位                           | 同名欄位                           | 直接相容                                         |
| 其他五個 policy 欄位                            | 無對應欄位                          | 必須移除；多數語意已由 AO schema/runtime 強制執行           |
| `task_sources`                            | 保留欄位、必須為空                      | 不能用來載入 Task                                  |
| `validation_profiles_source`              | 不存在                            | 必須移除                                         |
| phase file 的 `phase`                      | 不存在                            | phase metadata 不能直接 materialize              |
| `id/title/objective/non_goals`            | 同名欄位                           | 直接相容                                         |
| `version: 1`                              | `version: "1"`                 | 必須轉為字串                                       |
| `dependencies`                            | 同形狀                            | 直接相容                                         |
| `inputs.references`                       | `inputs: [string]`             | 可展平成字串陣列                                     |
| `inputs.artifacts`                        | 無專用欄位                          | 應由 dependency artifact runtime 承接            |
| `source_of_truth`                         | 字串陣列                           | 直接相容                                         |
| `execution_scope.allowed_roots/protected` | 同名欄位                           | 直接相容                                         |
| `max_production_files_changed`            | 無對應欄位                          | 無一對一 mapping                                 |
| `max_test_files_changed`                  | 無對應欄位                          | 無一對一 mapping                                 |
| `implementation`                          | 無對應欄位                          | 必須把內容遷入 objective／acceptance／source of truth |
| `outputs.artifacts`                       | 正式支援                           | Schema 相容，但缺少 operational path               |
| acceptance `type`                         | `category`                     | 可直接語意 mapping                                |
| acceptance `statement`                    | `description`                  | 可直接語意 mapping                                |
| acceptance `id`                           | 無對應欄位                          | AC identity 會遺失，除非嵌入 description             |
| `validation.profiles`                     | 支援 string 或 `{profile,target}` | 現值存在 runtime incompatibility                 |
| `retry.max_total_task_attempts`           | 同名欄位                           | 直接相容                                         |
| `side_effects.level`                      | `side_effect` scalar           | 可直接 flatten                                  |
| `priority: high/normal`                   | integer                        | 需制定數值 mapping                                |
| `stop_condition`                          | 無對應欄位                          | 必須遷入 acceptance 或 objective                  |

## A. `tasks`

AO v0.8.1 要求所有 Task 實際存在於 root `tasks`：

```
"tasks": {
  "type": "array",
  "minItems": 1
}
```

答案：

- `tasks: []` 不合法。
- 125 個 Task 必須全部 materialize 到同一個 root `tasks`。
- AO 沒有 Task 數量上限；runtime 明確表示 Plan Task count 不影響 per-Task quality limit。
- 125 個 ID `T001`～`T125` 全部唯一。
- Dependency graph 是完整線性鏈，124 條 edge 恰為 `T001 → T002 → ... → T125`。

## B. `task_sources`／`includes`

它們不是外部 Task loader，也不是有效 runtime metadata。

正式 schema：

```
"task_sources": {"type": "array", "maxItems": 0},
"includes": {"type": "array", "maxItems": 0}
```

正式 loader 還有第二層防護：

```
若 task_sources 或 includes 非空：
TASK_SOURCES_UNSUPPORTED:
modular plan sources are reserved for a future release
```

因此：

- 不會開啟或合併外部 YAML。
- 不會讀取 phase Task。
- `tasks: [] + task_sources: [...]` 不合法。
- `task_sources: [...]` 即使配合非空 `tasks` 仍不合法。
- 必須把全部 Task 展開至 root `tasks`。
- 15 個 phase YAML 目前只是 downstream 自訂格式，不是 AO v0.8.1 Task Plan fragment。

## C. Validation Profile

正式 Task schema 支援兩種格式：

```
validation:
  profiles:
    - python_pytest
    - profile: dotnet_build
      target: ComponentIntelligence.sln
  all_must_pass: true
```

Runtime allowlist 包含：

```
dotnet_build
dotnet_test
python_unittest
python_pytest
powershell_syntax
json_parse
json_schema
typescript_typecheck
javascript_npm_test
```

目前設定的問題：

```
- dotnet_build
- dotnet_test_all
```

- `dotnet_build`：名稱合法，但 .NET profile 必須提供明確 `.sln` 或 `.csproj` target。
- `dotnet_test_all`：不在正式 allowlist；正式名稱是 `dotnet_test`。
- `validation/profiles.yaml`：Task Plan loader 和 runtime 不會讀取。
- `validation_profiles_source`：不合法，不能保留。
- 該外部檔中的任意 command 定義也不能透過 Task Plan 執行。

可相容的原生表達形式是：

```
validation:
  profiles:
    - profile: dotnet_build
      target: ComponentIntelligence.sln
    - profile: dotnet_test
      target: ComponentIntelligence.sln
  all_must_pass: true
```

實際 repository 中這個 solution target 存在。

## D. Artifact Dependency

目前概念與正式 schema 完全一致：

```
dependencies:
  - task_id: T001
    required_artifacts:
      - T001-result
```

Audit 結果：

- 124 個 dependency 全部找到 parent Task。
- 124 個 required artifact reference 全部由 parent `outputs.artifacts` 宣告。
- 沒有 missing Task、duplicate dependency 或 invalid artifact reference。

但有一個重要 runtime gap：

- 125 個 output artifact 都沒有 `path`。
- 125 個 Task 都沒有 `outputs.files`。
- Runtime 只接受 patch 修改 `outputs.files` 或 artifact `path` 所宣告的檔案。
- Required artifact 若沒有 `path`，runtime 只會嘗試尋找名稱正好等於 artifact ID 的實體檔案，例如 repository root 的 `T001-result`。

所以目前 artifact graph 在 schema／dependency 層正確，但不能直接形成可執行的 output contract。Migration 必須為每個 Task補上真實 output file authority；這不能由 schema 自動推導。

## E. Model Routing Boundary

目前 125 個 Task 中：

```
model: 0
primary_actor: 0
fallback_actor: 0
fallback: 0
routing_hints: 0
```

邊界符合要求。

正式 schema：

- 明確禁止 `model`、`fallback`。
- `primary_actor`、`fallback_actor` 會被 `additionalProperties: false` 拒絕。
- 允許的 `routing_hints` 只有抽象的：
  - `specialization`
  - `reasoning_floor`
  - `risk`

Task Plan 沒有 `capability_requirement` 或具體 actor/model 欄位。Task Runtime 將單一 Task ContextPack 交給 ProjectPlanner，Capability Requirement 與 Actor／Model 選擇仍由既有 Skill B、Skill A 與 Runtime 決定。

目前沒有必要加入 `routing_hints`；保持省略即可。

## 必須移除的欄位

原生 v0.8.1 文件不能包含：

```
validation_profiles_source
plan.version
plan.source_of_truth
policy.require_artifact_dependencies
policy.disallow_model_pinning
policy.worker_context_mode
policy.actor_selection
policy.task_defines_work_not_actor
phase
implementation
side_effects
stop_condition
execution_scope.max_production_files_changed
execution_scope.max_test_files_changed
acceptance[].id
acceptance[].type
acceptance[].statement
```

其中部分內容不能直接丟棄，必須先做語意 migration。

## 必須新增或改形狀

- 新增 root `tasks`，包含全部 125 個 Task。
- `version` 轉為字串。
- `side_effects.level` 轉為 `side_effect`。
- acceptance 轉為 `{category, description}`。
- `inputs` 轉為 string array。
- `priority` 轉為 integer。
- .NET validation profile 補上 target。
- `dotnet_test_all` 改成正式 `dotnet_test`。
- 補充 `outputs.files` 或每個 artifact 的實際 `path`。

## 無法一對一 mapping 的語意

以下需要 migration policy，而非機械改名：

- Phase metadata 與 15 個 phase gate。
- `implementation.required_items/functions/rules/cases/...`
- 每 Task 的 `stop_condition`。
- Production/test file-count budget。
- Acceptance criterion ID。
- `priority: high/normal` 的數值定義。
- `inputs.artifacts` URI 與 runtime parent artifact record 的差異。
- `task_result` artifact 到實體檔案 path 的映射。
- 外部 command-based validation profile。

目前 phase gate 的主要順序語意可由完整線性 dependency chain 加上 `stop_on_final_failure: true` 保留，但它不是正式 phase gate 功能。

## 建議的新檔案形式

建議建立獨立的新檔案：

```
ao/tasks.v081.yaml
```

形式為單一 AO 原生 Task Plan：

```
schema_version: "1.0"

plan:
  id: COMPONENT-INTELLIGENCE-V01
  project_id: component-intelligence
  name: Component Intelligence System Development Plan

policy:
  execution_mode: sequential
  max_workers: 1
  stop_on_final_failure: true
  stop_on_human_gate: true

tasks:
  - id: T001
    version: "1"
    # 全部原生 Task 欄位

  # T002 ... T125
```

應省略 `task_sources` 與 `includes`，而不是保留非空值。現有 phase 檔可作為 downstream source/archive，但 AO runtime 不會讀取它們。

## Migration 可行性

可以保留：

- 125 個原始 objective 文字。
- 完整的 124 條 dependency graph。
- 全部 290 條 acceptance criterion 的判定語意。
- 全部 source-of-truth、retry、side-effect level 與 artifact dependency ID。

但整份 Plan 無法「零決策、100% lossless」地機械轉換，因為 implementation、stop condition、file budgets、phase gates、acceptance IDs 及 output paths 沒有一對一欄位。

技術容量不是問題：

- 最大原始 objective：116 bytes。
- objective + implementation + stop condition 最大約 2,529 bytes，低於 AO 的 8,192-byte objective 上限。
- 預估最大 Task Context item count 為 20，低於 64。
- 最大 validation profile 數為 2，低於 16。
- 最大 writable roots 數為 4，低於 32。

因此，語意忠實的 migration 是可行的；真正需要明確決策的是如何把 implementation／stop-condition 轉入正式欄位，以及每個 Task 的實際 output file/path authority。

Antigravity advisory 因此路徑未在既有 trust 清單而安全停止；沒有新增 trust。上述結論全部由 Codex 直接對正式 schema、loader/runtime implementation 與 125 個 Task 驗證。