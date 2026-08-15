#!/usr/bin/env python3
from pathlib import Path
import sys, yaml

ROOT = Path(__file__).resolve().parents[1]

def load_yaml(path):
    with open(path, "r", encoding="utf-8") as f:
        return yaml.safe_load(f)

def dep_id(dep):
    return dep if isinstance(dep, str) else dep.get("task_id")

def main():
    plan = load_yaml(ROOT / "plan.yaml")
    tasks = []
    for rel in plan["task_sources"]:
        tasks.extend(load_yaml(ROOT / rel).get("tasks", []))

    errors = []
    ids = [t.get("id") for t in tasks]
    if len(ids) != len(set(ids)):
        errors.append("Duplicate Task IDs.")

    by_id = {t["id"]: t for t in tasks}
    artifact_provider = {}
    banned = {"model", "primary_actor", "fallback_actor"}

    required_fields = [
        "id","version","title","objective","dependencies","acceptance",
        "validation","side_effects","stop_condition"
    ]

    for t in tasks:
        for field in required_fields:
            if field not in t:
                errors.append(f"{t.get('id','<unknown>')}: missing {field}")
        bad = banned.intersection(t.keys())
        if bad:
            errors.append(f"{t['id']}: banned model-routing fields {sorted(bad)}")
        if not t.get("acceptance"):
            errors.append(f"{t['id']}: acceptance is empty")
        if not (t.get("validation") or {}).get("profiles"):
            errors.append(f"{t['id']}: validation.profiles is empty")
        for art in (t.get("outputs",{}) or {}).get("artifacts",[]) or []:
            aid = art["id"] if isinstance(art,dict) else art
            if aid in artifact_provider:
                errors.append(f"Artifact {aid} has multiple providers")
            artifact_provider[aid] = t["id"]

    graph = {tid:[] for tid in by_id}
    for t in tasks:
        for dep in t.get("dependencies",[]) or []:
            did = dep_id(dep)
            if did not in by_id:
                errors.append(f"{t['id']}: missing dependency {did}")
                continue
            if did == t["id"]:
                errors.append(f"{t['id']}: self dependency")
            graph[t["id"]].append(did)
            if isinstance(dep,dict):
                for aid in dep.get("required_artifacts",[]) or []:
                    provider = artifact_provider.get(aid)
                    if provider is None:
                        errors.append(f"{t['id']}: artifact {aid} has no provider")
                    elif provider != did:
                        errors.append(f"{t['id']}: artifact {aid} is provided by {provider}, expected {did}")

    visiting, visited = set(), set()
    def dfs(n):
        if n in visiting:
            errors.append(f"Dependency cycle detected at {n}")
            return
        if n in visited:
            return
        visiting.add(n)
        for d in graph.get(n,[]):
            dfs(d)
        visiting.remove(n)
        visited.add(n)
    for tid in graph:
        dfs(tid)

    if errors:
        print(f"PLAN INVALID: {len(errors)} error(s)")
        for e in errors:
            print(" -", e)
        return 1

    print(f"PLAN VALID: {len(tasks)} tasks, {len(artifact_provider)} artifacts")
    print(f"TASK SOURCES: {len(plan['task_sources'])}")
    print("MODEL PINNING: none")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
