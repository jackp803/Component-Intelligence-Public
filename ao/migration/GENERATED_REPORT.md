# Generated Migration Report

## Compiler

```text

```

## Deterministic migration validation

```text
MIGRATION_VALID
TASKS=125
DEPENDENCY_EDGES=124
ARTIFACTS=125
ORIGINAL_ACCEPTANCE_PRESERVED=290
MAX_OBJECTIVE_BYTES=2303
MODEL_PINNING=NONE
```

## Important qualification boundary

This environment does not contain the user's installed Agent Orchestrator v0.8.1 package,
so the official installed `agent_orchestrator.config/task_plan.schema.json` could not be
executed here.

The generated plan follows the complete AO v0.8.1 read-only compatibility audit supplied
with the project. On the downstream machine, run:

```powershell
py -3.12 .\ao\tools\validate_with_installed_ao.py
agent-orchestrator plan validate ".\ao\tasks.v081.yaml"
agent-orchestrator plan run ".\ao\tasks.v081.yaml" --repo . --dry-run
```

If the console script is not on PATH, use the installed AO Skill / its official bridge entrypoint.
Do not invent a replacement command.
