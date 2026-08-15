# AO v0.8.1 Task Plan

## Runtime entrypoint

AO v0.8.1 請使用：

```text
ao/tasks.v081.yaml
```

`ao/plan.yaml` 是 compiler 產生的相同內容副本，供既有操作流程使用。

## Authoring source

人與 GPT 維護：

```text
ao/authoring/tasks/phase-*.yaml
```

這些 Phase YAML 不是 AO v0.8.1 runtime input。

## Build

```powershell
py -3.12 .\ao\tools\build_v081_plan.py
py -3.12 .\ao\tools\validate_migration.py
```

如果本機已安裝 AO v0.8.1：

```powershell
py -3.12 .\ao\tools\validate_with_installed_ao.py
```

之後以 AO 正式入口：

```powershell
agent-orchestrator plan validate ".\ao\tasks.v081.yaml"
agent-orchestrator plan run ".\ao\tasks.v081.yaml" --repo . --dry-run
```

Validation 與 dry-run PASS 前不得正式執行 Task。

## Model routing

Plan 不固定任何模型。

Task 定義「做什麼」；Skill B / Skill A / AO Runtime 決定 capability requirement 與 Actor / Model。
