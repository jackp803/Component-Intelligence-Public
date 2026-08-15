#!/usr/bin/env python3
from pathlib import Path
import yaml, sys, re

ROOT = Path(__file__).resolve().parents[2]
PLAN = ROOT / "ao" / "tasks.v081.yaml"
AUTHORING = ROOT / "ao" / "authoring" / "tasks"

FORBIDDEN_ROOT = {
    "validation_profiles_source",
}
FORBIDDEN_TASK = {
    "implementation", "side_effects", "stop_condition", "model",
    "primary_actor", "fallback_actor", "fallback",
}
ALLOWED_VALIDATION = {
    "dotnet_build", "dotnet_test", "python_unittest", "python_pytest",
    "powershell_syntax", "json_parse", "json_schema",
    "typescript_typecheck", "javascript_npm_test",
}

def load(p):
    return yaml.safe_load(Path(p).read_text(encoding="utf-8"))

def main():
    plan = load(PLAN)
    errors = []

    if plan.get("schema_version") != "1.0":
        errors.append("schema_version must be '1.0'.")
    for k in FORBIDDEN_ROOT:
        if k in plan:
            errors.append(f"forbidden root field: {k}")
    if "tasks" not in plan or not plan["tasks"]:
        errors.append("root tasks must be non-empty.")
    if plan.get("task_sources"):
        errors.append("task_sources must be absent or empty in AO v0.8.1.")
    if plan.get("includes"):
        errors.append("includes must be absent or empty in AO v0.8.1.")

    tasks = plan.get("tasks", [])
    if len(tasks) != 125:
        errors.append(f"expected 125 tasks, got {len(tasks)}")

    ids = [t.get("id") for t in tasks]
    if len(set(ids)) != len(ids):
        errors.append("duplicate task IDs.")

    by_id = {t["id"]: t for t in tasks if t.get("id")}
    artifacts = {}
    edge_count = 0
    original_acceptance = 0
    migrated_original_acceptance = 0
    max_objective_bytes = 0

    # Count original acceptance semantics.
    for p in sorted(AUTHORING.glob("phase-*.yaml")):
        d = load(p)
        original_acceptance += sum(len(t.get("acceptance", [])) for t in d.get("tasks", []))

    for t in tasks:
        tid = t.get("id", "<missing>")
        req = ["id","version","title","objective","acceptance","validation","side_effect","outputs"]
        for f in req:
            if f not in t:
                errors.append(f"{tid}: missing required field {f}")
        if not isinstance(t.get("version"), str):
            errors.append(f"{tid}: version must be string")
        if not isinstance(t.get("inputs", []), list) or not all(isinstance(x, str) for x in t.get("inputs", [])):
            errors.append(f"{tid}: inputs must be string array")
        if not isinstance(t.get("priority"), int):
            errors.append(f"{tid}: priority must be integer")
        for k in FORBIDDEN_TASK:
            if k in t:
                errors.append(f"{tid}: forbidden field {k}")
        scope = t.get("execution_scope", {})
        extra_scope = set(scope) - {"allowed_roots","protected"}
        if extra_scope:
            errors.append(f"{tid}: unsupported execution_scope fields {sorted(extra_scope)}")
        if not (t.get("outputs") or {}).get("files"):
            errors.append(f"{tid}: outputs.files must not be empty")
        arts = (t.get("outputs") or {}).get("artifacts", [])
        if not arts:
            errors.append(f"{tid}: outputs.artifacts must not be empty")
        for a in arts:
            aid = a.get("id")
            if not a.get("path"):
                errors.append(f"{tid}: artifact {aid} missing path")
            if aid in artifacts:
                errors.append(f"duplicate artifact id {aid}")
            artifacts[aid] = (tid, a.get("path"))

        for dep in t.get("dependencies", []) or []:
            edge_count += 1
            did = dep.get("task_id") if isinstance(dep, dict) else dep
            if did not in by_id:
                errors.append(f"{tid}: missing dependency task {did}")
            if isinstance(dep, dict):
                for aid in dep.get("required_artifacts", []) or []:
                    if aid not in artifacts and aid not in {
                        f"{x}-result" for x in by_id
                    }:
                        errors.append(f"{tid}: unknown required artifact {aid}")

        for a in t.get("acceptance", []):
            if set(a) != {"category","description"}:
                errors.append(f"{tid}: acceptance shape must be category+description")
            if str(a.get("description","")).startswith("[AC-"):
                migrated_original_acceptance += 1

        for v in (t.get("validation") or {}).get("profiles", []):
            if isinstance(v, str):
                prof, target = v, None
            else:
                prof, target = v.get("profile"), v.get("target")
            if prof not in ALLOWED_VALIDATION:
                errors.append(f"{tid}: unsupported validation profile {prof}")
            if prof in {"dotnet_build","dotnet_test"} and not target:
                errors.append(f"{tid}: {prof} requires explicit target")
        max_objective_bytes = max(max_objective_bytes, len(str(t.get("objective","")).encode("utf-8")))

    if edge_count != 124:
        errors.append(f"expected 124 dependency edges, got {edge_count}")
    if len(artifacts) != 125:
        errors.append(f"expected 125 artifacts, got {len(artifacts)}")
    if migrated_original_acceptance != original_acceptance:
        errors.append(
            f"acceptance semantic preservation mismatch: original={original_acceptance}, "
            f"migrated={migrated_original_acceptance}"
        )
    if max_objective_bytes > 8192:
        errors.append(f"objective exceeds audited AO limit: {max_objective_bytes} bytes")

    # Detect explicit model pinning keys recursively by YAML key name.
    banned_keys = {"model","primary_actor","fallback_actor","fallback"}
    def walk(x, path="root"):
        if isinstance(x, dict):
            for k,v in x.items():
                if k in banned_keys:
                    errors.append(f"model-pinning key found at {path}.{k}")
                walk(v, f"{path}.{k}")
        elif isinstance(x, list):
            for i,v in enumerate(x):
                walk(v, f"{path}[{i}]")
    walk(plan)

    if errors:
        print(f"MIGRATION_INVALID: {len(errors)} error(s)")
        for e in errors:
            print(" -", e)
        return 1

    print("MIGRATION_VALID")
    print(f"TASKS={len(tasks)}")
    print(f"DEPENDENCY_EDGES={edge_count}")
    print(f"ARTIFACTS={len(artifacts)}")
    print(f"ORIGINAL_ACCEPTANCE_PRESERVED={migrated_original_acceptance}")
    print(f"MAX_OBJECTIVE_BYTES={max_objective_bytes}")
    print("MODEL_PINNING=NONE")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
