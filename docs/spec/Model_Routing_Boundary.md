# Model Routing Boundary（模型調度邊界）

1. Project / Task Plan 不指定模型。
2. Skill A / AO Runtime 負責 Actor Inventory（執行者清單）、Connectivity（連線）、Capability（能力）、Priority（優先級）與 Escalation（升級）。
3. Task 只能表達工作需求，不表達誰執行。
4. 禁止欄位：`model`、`primary_actor`、`fallback_actor`。
5. Worker Prompt 只能得到當前 Task、必要 Artifact、相關 Source of Truth、允許檔案、Acceptance 與 Validation。
6. Worker 不得看到整份 Task DAG，除非 Planner / Reviewer 工作明確需要。
