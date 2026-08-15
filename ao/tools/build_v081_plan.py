#!/usr/bin/env python3
from pathlib import Path
import copy, yaml, sys

ROOT = Path(__file__).resolve().parents[2]
AO = ROOT / "ao"
AUTHORING = AO / "authoring"
AUTH = AO / "migration" / "output-authority.v081.yaml"
OUT = AO / "tasks.v081.yaml"
CONVENIENCE_OUT = AO / "plan.yaml"

PRIORITY = {"high": 100, "normal": 50, "low": 10}

class NoAliasDumper(yaml.SafeDumper):
    def ignore_aliases(self, data):
        return True

def load(path):
    with open(path, "r", encoding="utf-8") as f:
        return yaml.safe_load(f)

def stringify_impl(value, indent=0):
    lines = []
    if isinstance(value, dict):
        for k, v in value.items():
            if k == "spec_excerpt":
                continue
            lines.append(f"{k}:")
            for sub in stringify_impl(v, indent + 1):
                lines.append("  " + sub)
    elif isinstance(value, list):
        for item in value:
            if isinstance(item, (dict, list)):
                lines.append("-")
                for sub in stringify_impl(item, indent + 1):
                    lines.append("  " + sub)
            else:
                lines.append(f"- {item}")
    else:
        lines.append(str(value))
    return lines

def main():
    authority = load(AUTH)["tasks"]
    phase_files = sorted((AUTHORING / "tasks").glob("phase-*.yaml"))
    source_tasks = []
    phase_last = set()
    for p in phase_files:
        doc = load(p)
        tasks = doc.get("tasks", [])
        if tasks:
            phase_last.add(tasks[-1]["id"])
        source_tasks.extend(tasks)

    if len(source_tasks) != 125:
        raise SystemExit(f"Expected 125 authoring tasks, got {len(source_tasks)}")

    migrated = []
    for src in source_tasks:
        tid = src["id"]
        out_auth = authority[tid]

        objective_parts = [str(src["objective"]).strip()]
        impl = src.get("implementation") or {}
        impl_lines = stringify_impl(impl)
        if impl_lines:
            objective_parts.append(
                "Implementation requirements migrated from the authoring plan:\n"
                + "\n".join(impl_lines)
            )

        # Preserve the original spec excerpt because some implementation semantics
        # have no AO v0.8.1 one-to-one field.
        excerpt = impl.get("spec_excerpt")
        if excerpt:
            objective_parts.append("Original task specification excerpt:\n" + str(excerpt).strip())

        old_scope = src.get("execution_scope") or {}
        max_prod = old_scope.get("max_production_files_changed")
        max_test = old_scope.get("max_test_files_changed")

        inputs_obj = src.get("inputs") or {}
        inputs = []
        for x in inputs_obj.get("references", []) or []:
            if x not in inputs:
                inputs.append(x)
        # inputs.artifacts is deliberately not copied; required artifacts remain
        # authoritative in dependencies per AO v0.8.1 runtime semantics.

        acceptance = []
        for item in src.get("acceptance", []) or []:
            aid = item.get("id", "AC")
            category = item.get("type", "deterministic")
            description = item.get("statement", "")
            acceptance.append({
                "category": category,
                "description": f"[{aid}] {description}"
            })

        if max_prod is not None or max_test is not None:
            acceptance.append({
                "category": "deterministic",
                "description": (
                    "[SCOPE-BUDGET] Preserve the authoring-plan change budget: "
                    f"production files changed <= {max_prod}; test files changed <= {max_test}."
                )
            })

        stop = src.get("stop_condition")
        if stop:
            acceptance.append({
                "category": "deterministic",
                "description": f"[STOP] {stop}"
            })

        if tid in phase_last:
            acceptance.append({
                "category": "deterministic",
                "description": (
                    "[PHASE-GATE] This task must PASS before the dependency chain may enter the next phase."
                )
            })

        allowed_roots = list((old_scope.get("allowed_roots") or []))
        # Ensure every authorized output path has an allowed prefix. For root files,
        # add that exact path rather than broadening scope to the entire repository.
        for fp in out_auth["files"]:
            if not any(fp == r.rstrip("/") or fp.startswith(r) for r in allowed_roots):
                if "/" not in fp:
                    allowed_roots.append(fp)
                else:
                    parent = fp.rsplit("/", 1)[0] + "/"
                    if parent not in allowed_roots:
                        allowed_roots.append(parent)

        task = {
            "id": tid,
            "version": str(src["version"]),
            "title": src["title"],
            "objective": "\n\n".join(objective_parts),
            "non_goals": list(src.get("non_goals") or []),
            "dependencies": copy.deepcopy(src.get("dependencies") or []),
            "inputs": inputs,
            "source_of_truth": list(src.get("source_of_truth") or []),
            "execution_scope": {
                "allowed_roots": allowed_roots,
                "protected": list(old_scope.get("protected") or []),
            },
            "outputs": {
                "files": list(out_auth["files"]),
                "artifacts": [{
                    "id": f"{tid}-result",
                    "type": "task_result",
                    "required": True,
                    "path": out_auth["artifact_path"],
                }],
            },
            "acceptance": acceptance,
            "validation": {
                "profiles": [
                    {"profile": "dotnet_build", "target": "ComponentIntelligence.sln"},
                    {"profile": "dotnet_test", "target": "ComponentIntelligence.sln"},
                ],
                "all_must_pass": True,
            },
            "retry": copy.deepcopy(src.get("retry") or {"max_total_task_attempts": 3}),
            "side_effect": (src.get("side_effects") or {}).get("level", "LOCAL_REVERSIBLE"),
            "priority": PRIORITY.get(src.get("priority"), 50),
        }
        migrated.append(task)

    plan = {
        "schema_version": "1.0",
        "plan": {
            "id": "COMPONENT-INTELLIGENCE-V01",
            "project_id": "component-intelligence",
            "name": "Component Intelligence System Development Plan",
        },
        "policy": {
            "execution_mode": "sequential",
            "max_workers": 1,
            "stop_on_final_failure": True,
            "stop_on_human_gate": True,
        },
        "tasks": migrated,
    }

    text = yaml.dump(plan, Dumper=NoAliasDumper, allow_unicode=True, sort_keys=False, width=120)
    OUT.write_text(text, encoding="utf-8")
    CONVENIENCE_OUT.write_text(text, encoding="utf-8")
    print(f"WROTE {OUT.relative_to(ROOT)}")
    print(f"WROTE {CONVENIENCE_OUT.relative_to(ROOT)}")
    print(f"TASKS {len(migrated)}")

if __name__ == "__main__":
    main()
