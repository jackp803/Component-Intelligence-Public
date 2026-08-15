#!/usr/bin/env python3
from pathlib import Path
import json, yaml, sys
from importlib.resources import files

try:
    import jsonschema
except ImportError:
    print("jsonschema is required for this helper.", file=sys.stderr)
    raise SystemExit(2)

ROOT = Path(__file__).resolve().parents[2]
PLAN = ROOT / "ao" / "tasks.v081.yaml"

def main():
    schema_resource = files("agent_orchestrator.config").joinpath("task_plan.schema.json")
    schema = json.loads(schema_resource.read_text(encoding="utf-8"))
    plan = yaml.safe_load(PLAN.read_text(encoding="utf-8"))
    validator = jsonschema.Draft202012Validator(schema)
    errors = sorted(validator.iter_errors(plan), key=lambda e: list(e.absolute_path))
    if errors:
        print(f"AO_SCHEMA_INVALID: {len(errors)} error(s)")
        for e in errors:
            path = ".".join(str(x) for x in e.absolute_path) or "<root>"
            print(f" - {path}: {e.message}")
        return 1
    print("AO_SCHEMA_VALID")
    print(schema_resource)
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
